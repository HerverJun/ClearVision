using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Desktop.Tests;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.AgentRun;

// Reuse the existing isolated TestServer. Only model/build execution is substituted;
// HTTP endpoints, session persistence, event storage and terminal projection are real.
var fixture = typeof(AgentRunEndpointsTests);
var hostType = fixture.GetNestedType("AgentRunEndpointTestHost", BindingFlags.NonPublic)!;
var create = hostType.GetMethod("CreateAsync")!;
var output = new List<object>();
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var calls = 0;
Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>> buildHandler = async (_, ct) =>
{
    Interlocked.Increment(ref calls);
    await release.Task.WaitAsync(ct);
    return (AiFlowGenerationResult)hostType.GetMethod("SuccessResult")!.Invoke(null, null)!;
};
var creating = (Task)create.Invoke(null, [buildHandler, false, null, null, null])!;
await creating;
var host = creating.GetType().GetProperty("Result")!.GetValue(creating)!;
await using var disposable = (IAsyncDisposable)host;
T Get<T>(string name) => (T)hostType.GetProperty(name)!.GetValue(host)!;
var client = Get<HttpClient>("Client");
var sessions = Get<ConversationalFlowService>("ConcreteConversationService");
var owner = Get<string>("OwnerHash");
var stream = Get<IAgentRunEventStreamService>("StreamService");
var plan = (VisionAgentPlanModeResult)fixture.GetMethod("LegacyBlockedAgentRunBuildFromPlanSnapshot", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
var answers = (List<VisionAgentPlanAnswer>)fixture.GetMethod("ConfirmedAgentRunBuildFromPlanAnswers", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
const string sessionId = "isolated-state-audit";
var initial = sessions.TryInitializeWorkspaceSnapshot(owner, sessionId, new VisionAgentWorkspaceSnapshotUpdate
{
    LifecycleState = "plan_ready", PendingPlanSnapshot = plan
});
object Build(long revision) => new
{
    description = "start build from confirmed plan", sessionId, mode = "new", requirementMode = "strict",
    useVisionAgentGenerateFlow = true, agentGenerateFlowMode = "scripted",
    buildFromPlan = new VisionAgentBuildFromPlanRequest
    {
        PlanId = plan.PlanId, PlanHash = plan.PlanHash, PlanSnapshot = plan,
        ConfirmedAnswers = answers, OriginalUserPrompt = plan.OriginalUserPrompt,
        WorkspaceExpectedRevision = revision, MetadataOnly = true
    }
};
async Task<JsonElement> Post(string path, object request)
{
    using var response = await client.PostAsJsonAsync(path, request);
    var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    output.Add(new { path, status = (int)response.StatusCode, data });
    response.EnsureSuccessStatusCode();
    return data;
}
async Task Until(Func<bool> condition)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    while (!condition()) await Task.Delay(20, deadline.Token);
}

try
{
    var first = await Post("/api/ai/agent-runs", Build(initial.Revision));
    var firstRunId = first.GetProperty("runId").GetString()!;
    await Until(() => Volatile.Read(ref calls) == 1);
    var duringFirst = sessions.GetSession(owner, sessionId)!.WorkspaceSnapshot!;
    var second = await Post("/api/ai/agent-runs", Build(duringFirst.Revision));
    var secondRunId = second.GetProperty("runId").GetString()!;
    await Until(() => Volatile.Read(ref calls) == 2);
    output.Add(new { id = "overlapping-builds", firstRunId, secondRunId, calls,
        firstStatus = stream.Replay(firstRunId)!.Summary.Status,
        secondStatus = stream.Replay(secondRunId)!.Summary.Status,
        snapshotBuild = sessions.GetSession(owner, sessionId)!.WorkspaceSnapshot!.BuildRunId });
    release.TrySetResult();
    await Until(() => stream.Replay(firstRunId)!.Summary.Status != "running" && stream.Replay(secondRunId)!.Summary.Status != "running");
    await Until(() => sessions.GetSession(owner, sessionId)!.WorkspaceSnapshot!.BuildRunStatus != "running");
    output.Add(new { id = "after-overlap", snapshot = sessions.GetSession(owner, sessionId)!.WorkspaceSnapshot });

    const string successSession = "isolated-success-audit";
    using var successResponse = await client.PostAsJsonAsync("/api/ai/agent-runs", new
    {
        description = "Detect scratches on a metal part", sessionId = successSession,
        mode = "new", useVisionAgentGenerateFlow = true, agentGenerateFlowMode = "scripted"
    });
    successResponse.EnsureSuccessStatusCode();
    await Until(() => sessions.GetSession(owner, successSession)?.WorkspaceSnapshot?.LifecycleState == "build_completed");
    var successful = sessions.GetSession(owner, successSession)!.WorkspaceSnapshot!;
    output.Add(new { id = "real-success-snapshot", snapshot = successful });
    await Post("/api/ai/agent-plan-runs", new VisionAgentPlanModeRequest
    {
        Description = "Detect scratches on a metal part from a file image and output OK NG",
        OriginalUserPrompt = "Detect scratches on a metal part from a file image and output OK NG",
        SessionId = successSession, WorkspaceExpectedRevision = successful.Revision,
        ClientMutationId = "audit-new-plan-revision"
    });
    await Until(() => sessions.GetSession(owner, successSession)!.WorkspaceSnapshot!.PlanRunStatus != "running");
    var nextPlan = sessions.GetSession(owner, successSession)!.WorkspaceSnapshot!;
    if (nextPlan.BuildRunId != successful.BuildRunId) throw new Exception("Expected inherited BuildRunId was not reproduced.");
    output.Add(new { id = "real-new-plan-inherits-build", snapshot = nextPlan });
}
finally
{
    release.TrySetResult();
    var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    await File.WriteAllTextAsync(Path.Combine(args[0], "backend-observations.json"), json);
    Console.WriteLine($"Saved {output.Count} HTTP/state observations. Build calls: {calls}.");
}
