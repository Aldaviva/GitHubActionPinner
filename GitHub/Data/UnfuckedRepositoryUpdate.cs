using Octokit;
using Octokit.Internal;

namespace GitHubActionPinner.GitHub.Data;

// The latest Octokit version (14) is 2 years old and still does not include this property, and the only people that updated that library all left Microsoft by 2026-04.
public class UnfuckedRepositoryUpdate: RepositoryUpdate {

    public PullRequestCreationPolicy? PullRequestCreationPolicy { get; set; }

}

public enum PullRequestCreationPolicy {

    [Parameter(Value = "all")]
    ALL,

    [Parameter(Value = "collaborators_only")]
    COLLABORATORS_ONLY

} 