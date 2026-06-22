// AiGenerationServiceExtensions.cs
// AI 服务注入扩展
// 提供 AI 相关服务的依赖注入扩展方法
// 作者：蘅芜君
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClearVision.Product.Infrastructure.AI;

using ClearVision.Product.Infrastructure.AI.Connectors;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.AI.Tools;

public static class AiGenerationServiceExtensions
{
    public static IServiceCollection AddAiFlowGeneration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册 IOptions<AiGenerationOptions>（供 AiConfigStore 初始化时读取 appsettings.json 默认值）
        services.Configure<AiGenerationOptions>(
            configuration.GetSection(AiGenerationOptions.SectionName));
        services.AddOptions<AgentGenerateFlowOptions>()
            .Bind(configuration.GetSection(AgentGenerateFlowOptions.SectionName))
            .Validate(options =>
                string.Equals(AiAgentGenerateFlowModes.Normalize(options.Mode), options.Mode, StringComparison.OrdinalIgnoreCase),
                "AI:VisionAgent:GenerateFlow:Mode must be scripted, planner, or tool_loop.")
            .ValidateOnStart();
        services.Configure<VisionAgentLoopOptions>(
            configuration.GetSection("AI:VisionAgent:Loop"));
        services.Configure<AgentPlannerCompletionOptions>(
            configuration.GetSection(AgentPlannerCompletionOptions.SectionName));
        services.Configure<VisionAgentPlanPlannerOptions>(
            configuration.GetSection(VisionAgentPlanPlannerOptions.SectionName));
        services.Configure<VisionAgentIntentRouterOptions>(
            configuration.GetSection(VisionAgentIntentRouterOptions.SectionName));
        services.Configure<VisionAgentSemanticExtractorOptions>(
            configuration.GetSection(VisionAgentSemanticExtractorOptions.SectionName));

        // 注册运行时配置管理器（单例：启动时从 ai_models.json 加载，必要时从 ai_config.json 迁移）
        services.AddSingleton<AiConfigStore>();

        // 注册 HttpClient
        services.AddHttpClient<AiApiClient>(client =>
        {
            // Request-level cancellation in AiApiClient owns per-model timeouts.
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        });

        // 注册核心组件
        services.AddSingleton<IOperatorKnowledgeGraphService, OperatorKnowledgeGraphService>();
        services.AddScoped<IOperatorKnowledgeRetriever, OperatorKnowledgeRetriever>();
        services.AddScoped<PromptBuilder>();
        services.AddSingleton<IConversationalFlowService, ConversationalFlowService>();
        services.AddSingleton<IFlowTemplateService, FlowTemplateService>();
        services.AddScoped<IScenarioMatcher, ScenarioMatcher>();
        services.AddScoped<IRequirementBriefExtractor, RequirementBriefExtractor>();
        services.AddScoped<IAiTurnRouter, AiTurnRouter>();
        services.AddScoped<IClarificationEngine, ClarificationEngine>();
        services.AddScoped<ITemplateConstraintValidator, TemplateConstraintValidator>();
        services.AddScoped<IAiFlowValidator, AiFlowValidator>();
        services.AddScoped<IAiFlowResponseParser, AiFlowResponseParser>();
        services.AddScoped<AutoLayoutService>();
        services.AddScoped<AgentPromptBuilder>();
        services.AddSingleton<AgentRunEventRedactor>();
        services.AddSingleton<AgentRunEventStore>();
        services.AddSingleton<IAgentRunEventStreamService, AgentRunEventStreamService>();
        services.AddScoped<IAgentRunEventSink, AgentRunEventSink>();
        services.AddScoped<AgentPlannerPromptBuilder>();
        services.AddScoped<AgentPlannerPromptComposer>();
        services.AddScoped<VisionAgentPlanPromptComposer>();
        services.AddScoped<JsonToolCallRepair>();
        services.AddScoped<AgentToolCallPolicy>();
        services.AddScoped<AgentWorkflowDraftEditor>();
        services.AddScoped<VisionAgentProtocolParser>();
        services.AddScoped<VisionAgentLoop>();
        services.AddScoped<IVisionAgentLoopCompletionSource, VisionAgentLoopCompletionSource>();
        services.AddScoped<IVisionAgentPlannerCompletionSource, LlmVisionAgentPlannerCompletionSource>();
        services.AddScoped<IVisionAgentPlannerService, VisionAgentPlannerService>();
        services.AddScoped<IVisionAgentPlanCompletionSource, LlmVisionAgentPlanCompletionSource>();
        services.AddScoped<IVisionAgentPlanPlannerService, VisionAgentPlanPlannerService>();
        services.AddScoped<IVisionAgentIntentRouterCompletionSource, LlmVisionAgentIntentRouterCompletionSource>();
        services.AddScoped<IVisionAgentIntentRouterService, VisionAgentIntentRouterService>();
        services.AddScoped<IVisionAgentSemanticExtractionCompletionSource, LlmVisionAgentSemanticExtractionCompletionSource>();
        services.AddScoped<IVisionAgentSemanticExtractorService, VisionAgentSemanticExtractorService>();
        services.AddScoped<IVisionAgentOperatorContractCatalog, VisionAgentOperatorContractCatalog>();
        services.AddScoped<VisionAgentPlanAnswerValidator>();
        services.AddScoped<VisionAgentPlanRequirementOverlay>();
        services.AddScoped<BuildToolRunner>();
        services.AddScoped<BuildPlanContextLoader>();
        services.AddScoped<BuildIntentResolver>();
        services.AddScoped<TemplateStrategyResolver>();
        services.AddScoped<PlanSelectionResolver>();
        services.AddScoped<OperatorPipelineSelector>();
        services.AddScoped<ParameterMappingService>();
        services.AddScoped<WorkflowDraftBuilder>();
        services.AddScoped<BuildReadinessReviewService>();
        services.AddScoped<WorkflowDiffService>();
        services.AddScoped<ApplyGateResolver>();
        services.AddScoped<BuildResultAssembler>();
        services.AddScoped<IVisionAgentBuildOrchestrator, VisionAgentBuildOrchestrator>();
        services.AddScoped<IVisionAgentBuildApplicationService, VisionAgentBuildApplicationService>();
        services.AddScoped<IVisionAgentBuildTerminalProjector, VisionAgentBuildTerminalProjector>();
        services.AddScoped<IVisionAgentBuildRunService, VisionAgentBuildRunService>();
        services.AddScoped<IVisionAgentStationStatusReader, NoOpVisionAgentStationStatusReader>();
        services.AddScoped<RuntimePreviewArtifactStore>();
        services.AddScoped<RuntimePreviewPilotResourceCatalog>();
        services.AddSingleton<RuntimePreviewGovernanceStore>();
        services.AddSingleton<RuntimePreviewSessionStore>();
        services.AddSingleton<RuntimePreviewAuditTrail>();
        services.AddSingleton<RuntimePreviewReportArchive>();
        services.AddScoped<RuntimePreviewResourceBroker>();
        services.AddScoped<RuntimePreviewPermissionBroker>();
        services.AddScoped<RuntimePreviewResourceAllowlistResolver>();
        services.AddScoped<RuntimePreviewPilotReadinessGate>();
        services.AddScoped<RuntimePreviewSimulatedExecutionHarness>();
        services.AddScoped<RuntimePreviewGovernanceMaintenanceService>();
        services.AddScoped<RuntimePreviewDeployReadinessService>();
        services.AddScoped<RuntimePackageManifestDryRunService>();
        services.AddScoped<RuntimePreviewPackageReadinessBridge>();
        services.AddScoped<RuntimePreviewScenarioCorpusService>();
        services.AddScoped<RuntimePreviewRedactedFlowCorpusService>();
        services.AddScoped<RuntimePreviewStationProfileCatalog>();
        services.AddScoped<RuntimePreviewOperatorContractRegistry>();
        services.AddScoped<RuntimePreviewStationCompatibilityDryRunService>();
        services.AddScoped<RuntimePreviewPreReleaseReviewService>();
        services.AddScoped<RuntimePreviewScenarioEvidenceService>();
        services.AddScoped<RuntimePreviewAgentExplanationService>();
        services.AddScoped<OfflineRuntimePreviewAdapter>();
        services.AddScoped<PilotRuntimePreviewAdapter>();
        services.AddScoped<IRuntimePreviewAdapter>(sp => sp.GetRequiredService<OfflineRuntimePreviewAdapter>());
        services.AddScoped<IRuntimePreviewAdapter>(sp => sp.GetRequiredService<PilotRuntimePreviewAdapter>());
        services.AddScoped<RuntimePreviewAdapterRegistry>();
        services.AddScoped<IVisionAgentTool, OperatorCatalogTool>();
        services.AddScoped<IVisionAgentTool, OperatorSchemaTool>();
        services.AddScoped<IVisionAgentTool, OperatorKnowledgeTool>();
        services.AddScoped<IVisionAgentTool, FlowTemplateMatchTool>();
        services.AddScoped<IVisionAgentTool, FlowTemplateSkeletonTool>();
        services.AddScoped<IVisionAgentTool, CurrentFlowInspectTool>();
        services.AddScoped<IVisionAgentTool, FlowValidationTool>();
        services.AddScoped<IVisionAgentTool, DryRunFlowTool>();
        services.AddScoped<RuntimePackagePrecheckTool>();
        services.AddScoped<IVisionAgentTool>(sp => sp.GetRequiredService<RuntimePackagePrecheckTool>());
        services.AddScoped<IVisionAgentTool, RuntimePreviewSimulateMetadataSessionTool>();
        services.AddScoped<IVisionAgentTool, RuntimePreviewCaptureStubTool>();
        services.AddScoped<IVisionAgentTool, RuntimePreviewReplayStubTool>();
        services.AddScoped<IVisionAgentToolRegistry, VisionAgentToolRegistry>();
        services.AddScoped<IVisionAgentGenerateFlowService, VisionAgentGenerateFlowService>();
        services.AddScoped<IAiFlowGenerationService, AiFlowGenerationService>();
        services.AddScoped<IVisionAgentOrchestrator, VisionAgentOrchestrator>();
        services.AddScoped<GenerateFlowMessageHandler>();

        // Stage A: unified AI runtime pipeline
        services.AddScoped<IAiModelRegistry, AiModelRegistry>();
        services.AddScoped<IAiModelSelector, RoleAwareAiModelSelector>();
        services.AddScoped<IAiConnectorFactory, AiConnectorFactory>();
        services.AddScoped<AiGenerationOrchestrator>();

        services.AddHttpClient("LLM", client =>
        {
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        });

        services.AddSingleton<ILLMConfigurationStore, JsonLLMConfigurationStore>();
        services.AddSingleton<IPromptVersionManager, PromptVersionManager>();
        services.AddSingleton<IAIGeneratedFlowVersionManager, AIGeneratedFlowVersionManager>();
        services.AddScoped<LLMConnectorFactory>();
        services.AddScoped<ILLMConnector, DynamicLLMConnector>();

        return services;
    }
}
