using GitHubActionPinner;
using GitHubActionPinner.GitHub.Data;
using McMaster.Extensions.CommandLineUtils;
using Octokit;
using System.Reflection;
using System.Text.RegularExpressions;
using Unfucked;
using Unfucked.Caching;
using static Unfucked.ConsoleControl;

Assembly          assembly          = Assembly.GetEntryAssembly()!;
CancellationToken cancellationToken = new CancellationTokenSource().CancelOnCtrlC().Token;

try {
    using CommandLineApplication argumentParser = new() {
        Description = "Set all owned GitHub repository actions permissions to whitelist third-party actions by immutable SHA-1 commit hashes, instead of by mutable tags, for improved security.",
        UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw
    };
    argumentParser.Conventions.UseDefaultConventions();
    argumentParser.VersionOptionFromAssemblyAttributes(assembly);

    CommandOption<string> gitHubAccessToken = argumentParser.Option<string>("--github-access-token",
        "Token with repository administration write access your repositories", CommandOptionType.SingleValue).IsRequired();
    CommandOption<bool> isDryRun = argumentParser.Option<bool>("-n|--dry-run", "Don't actually make any changes", CommandOptionType.NoValue);
    argumentParser.Parse(args);
    if (argumentParser.IsShowingInformation) return 0;

    GitHubClient gitHubClient = new(new ProductHeaderValue(assembly.GetName().Name!, assembly.GetName().Version!.ToString(3))) { Credentials = new Credentials(gitHubAccessToken.ParsedValue) };

    using Cache<string, IDictionary<string, string>> repoTagToCommitCache = new InMemoryCache<string, IDictionary<string, string>>(loader: async repoFullName => {
        string[]                     ownerAndRepo = repoFullName.Split('/', 2);
        IReadOnlyList<RepositoryTag> allTags      = await gitHubClient.Repository.GetAllTags(ownerAndRepo[0], ownerAndRepo[1]);
        return allTags.ToDictionary(tag => tag.Name, tag => tag.Commit.Sha, StringComparer.OrdinalIgnoreCase);
    });

    IEnumerable<Repository> allUserRepositories = (await gitHubClient.Repository.GetAllForCurrent(new RepositoryRequest { Type = RepositoryType.Owner }, new ApiOptions { PageSize = 100 }))
        .Where(repo => !repo.Archived);

    foreach (Repository repo in allUserRepositories) {
        cancellationToken.ThrowIfCancellationRequested();
        ActionsPermissions permissions = await gitHubClient.Actions.PermissionsUnfucked.Get(repo.Owner.Login, repo.Name);
        if (!permissions.Enabled) continue;

        switch (permissions.AllowedActions) {
            case AllowedActions.LocalOnly:
                break;
            case AllowedActions.All:
                int workflowCount = (await gitHubClient.Actions.Workflows.List(repo.Owner.Login, repo.Name, new ApiOptions { PageCount = 1, PageSize = 1 })).TotalCount;
                if (workflowCount == 0) {
                    if (!isDryRun.ParsedValue) {
                        await gitHubClient.Actions.PermissionsUnfucked.Set(repo.Owner.Login, repo.Name, new ActionsPermissions(false, null));
                    }
                    WriteLine($"{(isDryRun.ParsedValue ? "Would have disabled" : "Disabled")} actions on {repo.FullName} because it has no workflows", ConsoleColor.Magenta);
                } else {
                    WriteLine($"{repo.FullName} allows all actions to run and has {workflowCount:N0} workflows, consider changing this to selected actions only", ConsoleColor.Yellow);
                }
                break;
            case AllowedActions.Selected:
                SelectedActions selectedActions = await gitHubClient.Actions.PermissionsUnfucked.GetSelectedActions(repo.Owner.Login, repo.Name);
                WriteLine(repo.FullName, ConsoleColor.Blue);
                bool changed = false;
                for (int index = 0; index < selectedActions.PatternsAllowed.Count; index++) {
                    Match match = allowedActionPattern.Match(selectedActions.PatternsAllowed[index]);
                    if (match.Groups["blocked"].Success) continue;

                    string commitOrTag = match.Groups["commitish"].Value;
                    bool   isPinned    = gitSha1CommitHashPattern.IsMatch(commitOrTag);
                    Console.WriteLine(
                        $"  {match.Groups["owner"].Value}/{match.Groups["repo"].Value}@{commitOrTag}: {Color(isPinned ? "pinned" : "unpinned", isPinned ? ConsoleColor.Green : ConsoleColor.Red)}");
                    string ownerAndRepo = match.Groups["ownerAndRepo"].Value;
                    if (!isPinned && (await repoTagToCommitCache.Get(ownerAndRepo)).GetValueOrNull(commitOrTag) is {} commitForTag) {
                        selectedActions.PatternsAllowed[index] = $"{ownerAndRepo}@{commitForTag}";
                        changed                                = true;
                    }
                }

                if (changed) {
                    if (!isDryRun.ParsedValue) {
                        cancellationToken.ThrowIfCancellationRequested();
                        await gitHubClient.Actions.PermissionsUnfucked.SetSelectedActions(repo.Owner.Login, repo.Name, selectedActions);
                    }
                    WriteLine($"{(isDryRun.ParsedValue ? "Would have set" : "Set")} {repo.FullName} allowed actions to {selectedActions.PatternsAllowed.Join(", ")}", ConsoleColor.Magenta);
                }
                break;
        }
    }
} catch (OperationCanceledException) {
    return 1;
}
return 0;

internal static partial class Program {

    // Identically-named tags do not shadow commits in Git, so this is sufficient to detect commit hashes and not tags.
    [GeneratedRegex(@"^[\da-f]{40}$", RegexOptions.IgnoreCase)]
    private static partial Regex gitSha1CommitHashPattern { get; }

    [GeneratedRegex(@"^(?<blocked>!)?(?<ownerAndRepo>(?<owner>[a-z-]+?)/(?<repo>[\w./*-]+))(?:@(?<commitish>[\w.*-]+?))?$", RegexOptions.IgnoreCase)]
    private static partial Regex allowedActionPattern { get; }

}