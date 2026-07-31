namespace ClearVision.Product.Infrastructure.AI.Agent;

internal static class VisionAgentRecoveryConflictMutationIdentity
{
    public static string Build(string runId, VisionAgentWorkspaceSnapshotUpdate update)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Run id is required.", nameof(runId));
        }

        ArgumentNullException.ThrowIfNull(update);
        var fingerprint = ConversationalFlowService.ComputeWorkspaceMutationFingerprint(update);
        var separator = fingerprint.IndexOf(':', StringComparison.Ordinal);
        var hash = separator >= 0 ? fingerprint[(separator + 1)..] : fingerprint;
        var suffix = hash.Length <= 16 ? hash : hash[..16];
        return $"recovery-conflict:{runId.Trim()}:{suffix}";
    }

    public static bool Matches(string? candidate, string runId, string expected)
    {
        if (string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            candidate,
            $"recovery-conflict:{runId.Trim()}",
            StringComparison.OrdinalIgnoreCase);
    }
}
