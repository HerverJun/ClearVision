namespace ClearVision.Product.Core.Continuous;

public sealed class ContinuousInspectionConfig
{
    public ContinuousInspectionMode Mode { get; set; } = ContinuousInspectionMode.Disabled;
    public ContinuousInspectionHardwareProfile HardwareProfile { get; set; } = ContinuousInspectionHardwareProfile.Medium;
    public int TargetFps { get; set; } = 25;
    public int BufferCapacity { get; set; } = 24;
    public int DetectEveryNFrames { get; set; } = 2;
    public int PreEventFrames { get; set; } = 4;
    public int PostEventFrames { get; set; } = 4;
    public int MinConsensusFrames { get; set; } = 3;
    public double ConsensusThreshold { get; set; } = 0.6;
    public int SchedulerQueueLength { get; set; } = 8;
    public int MaxLatencyMs { get; set; } = 250;
    public bool SaveReplayOnNgOnly { get; set; } = true;
    public bool ShadowOutputDisabled { get; set; } = true;

    public void Normalize()
    {
        TargetFps = Math.Clamp(TargetFps, 1, 120);
        BufferCapacity = Math.Clamp(BufferCapacity, 1, 512);
        DetectEveryNFrames = Math.Max(1, DetectEveryNFrames);
        PreEventFrames = Math.Max(0, PreEventFrames);
        PostEventFrames = Math.Max(0, PostEventFrames);
        MinConsensusFrames = Math.Max(1, MinConsensusFrames);
        ConsensusThreshold = Math.Clamp(ConsensusThreshold, 0.0, 1.0);
        SchedulerQueueLength = Math.Clamp(SchedulerQueueLength, 1, 1024);
        MaxLatencyMs = Math.Max(1, MaxLatencyMs);
    }
}

public enum ContinuousInspectionHardwareProfile
{
    Low = 0,
    Medium = 1,
    High = 2
}

public static class ContinuousInspectionConfigTemplates
{
    public static IReadOnlyDictionary<string, ContinuousInspectionConfig> CreateDefaults() =>
        new Dictionary<string, ContinuousInspectionConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["low"] = Low(),
            ["medium"] = Medium(),
            ["high"] = High()
        };

    public static ContinuousInspectionConfig Low() => new()
    {
        HardwareProfile = ContinuousInspectionHardwareProfile.Low,
        TargetFps = 10,
        BufferCapacity = 16,
        DetectEveryNFrames = 3,
        PreEventFrames = 2,
        PostEventFrames = 2,
        MinConsensusFrames = 2,
        SchedulerQueueLength = 4,
        MaxLatencyMs = 500
    };

    public static ContinuousInspectionConfig Medium() => new()
    {
        HardwareProfile = ContinuousInspectionHardwareProfile.Medium,
        TargetFps = 25,
        BufferCapacity = 24,
        DetectEveryNFrames = 2,
        PreEventFrames = 4,
        PostEventFrames = 4,
        MinConsensusFrames = 3,
        SchedulerQueueLength = 8,
        MaxLatencyMs = 250
    };

    public static ContinuousInspectionConfig High() => new()
    {
        HardwareProfile = ContinuousInspectionHardwareProfile.High,
        TargetFps = 60,
        BufferCapacity = 96,
        DetectEveryNFrames = 1,
        PreEventFrames = 8,
        PostEventFrames = 8,
        MinConsensusFrames = 5,
        SchedulerQueueLength = 16,
        MaxLatencyMs = 120
    };
}
