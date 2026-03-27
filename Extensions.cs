using Fasterflect;
using GitHubActionPinner.GitHub;
using Octokit;
using System.ComponentModel;

namespace GitHubActionPinner;

[EditorBrowsable(EditorBrowsableState.Advanced)]
internal static class Extensions {

    extension(IActionsClient actionsClient) {

        /// <summary>
        /// Get a non-broken alternative to <see cref="IActionsClient.Permissions"/>.
        /// </summary>
        public IUnfuckedActionsPermissionsClient PermissionsUnfucked {
            get {
                IActionsPermissionsClient original = actionsClient.Permissions;
                if (original is not IUnfuckedActionsPermissionsClient unfucked) {
                    unfucked = new UnfuckedActionsPermissionsClient((IApiConnection) original.GetPropertyValue("ApiConnection"));
                    actionsClient.SetPropertyValue(nameof(IActionsClient.Permissions), unfucked);
                }
                return unfucked;
            }
        }

    }

    private static readonly MethodInvoker FORMAT_URI = typeof(GitHubClient).Assembly.GetType($"{nameof(Octokit)}.StringExtensions")
        .DelegateForCallMethod("FormatUri", typeof(string), typeof(object[]));

    extension(string pattern) {

        private Uri FormatUri(params object[] args) => (Uri) FORMAT_URI(null, pattern, args);

    }

    extension(ApiUrls) {

        internal static Uri ActionsPermissions(string owner, string repo) =>
            "repos/{0}/{1}/actions/permissions".FormatUri(owner, repo);

        internal static Uri ActionsPermissionsSelectedActions(string owner, string repo) =>
            "repos/{0}/{1}/actions/permissions/selected-actions".FormatUri(owner, repo);

    }

}