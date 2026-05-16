// AiGenerationServiceExtensions.cs
// AI 服务注入扩展
// 提供 AI 相关服务的依赖注入扩展方法
// 作者：蘅芜君
using Acme.Product.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.Product.Infrastructure.AI;

using Acme.Product.Infrastructure.AI.Connectors;
using Acme.Product.Infrastructure.AI.Runtime;

public static class AiGenerationServiceExtensions
{
    public static IServiceCollection AddAiFlowGeneration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册 IOptions<AiGenerationOptions>（供 AiConfigStore 初始化时读取 appsettings.json 默认值）
        services.Configure<AiGenerationOptions>(
            configuration.GetSection(AiGenerationOptions.SectionName));

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
        services.AddScoped<IClarificationEngine, ClarificationEngine>();
        services.AddScoped<ITemplateConstraintValidator, TemplateConstraintValidator>();
        services.AddScoped<IAiFlowValidator, AiFlowValidator>();
        services.AddScoped<IAiFlowResponseParser, AiFlowResponseParser>();
        services.AddScoped<AutoLayoutService>();
        services.AddScoped<IAiFlowGenerationService, AiFlowGenerationService>();
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
