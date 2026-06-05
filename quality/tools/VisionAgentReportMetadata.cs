internal sealed record VisionAgentWorkflowRunMetadata(
    string CommitSha,
    string BranchName,
    string RunId,
    string RunAttempt,
    string GeneratedAtUtc)
{
    public static VisionAgentWorkflowRunMetadata FromEnvironment()
    {
        return new VisionAgentWorkflowRunMetadata(
            Read("GITHUB_SHA", "local"),
            Read("GITHUB_REF_NAME", Read("GITHUB_HEAD_REF", "local")),
            Read("GITHUB_RUN_ID", "local"),
            Read("GITHUB_RUN_ATTEMPT", "local"),
            DateTimeOffset.UtcNow.ToString("O"));
    }

    private static string Read(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
