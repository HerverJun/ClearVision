// AiFlowGenerationService.cs
// AI 流程生成服务实现
// 负责流程草案生成、修正与结果封装
// 作者：蘅芜君
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.DryRun;
using ClearVision.Product.Infrastructure.AI.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.AI;

public class AiFlowGenerationService : IAiFlowGenerationService
{
    private readonly AiGenerationOrchestrator _aiOrchestrator;
    private readonly PromptBuilder _promptBuilder;
    private readonly IConversationalFlowService _conversationalFlowService;
    private readonly IAiFlowValidator _validator;
    private readonly AutoLayoutService _layoutService;
    private readonly IOperatorFactory _operatorFactory;
    private readonly IFlowTemplateService _templateService;
    private readonly IScenarioMatcher _scenarioMatcher;
    private readonly IRequirementBriefExtractor _requirementBriefExtractor;
    private readonly IAiTurnRouter _turnRouter;
    private readonly ClarificationEngine _clarificationEngine = new();
    private readonly ITemplateConstraintValidator _templateConstraintValidator;
    private readonly IAiFlowResponseParser _responseParser;
    private readonly DryRunService _dryRunService;
    private readonly VisionAgentLoop? _visionAgentLoop;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IPromptVersionManager _promptVersionManager;
    private readonly Microsoft.Extensions.Logging.ILogger<AiFlowGenerationService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new FlexibleStringDictionaryJsonConverter()
        }
    };
    private const int DefaultMaxMultimodalAttachmentCount = 4;
    private const int AgentFinalRepairAttempts = 2;
    public AiFlowGenerationService(
        AiGenerationOrchestrator aiOrchestrator,
        PromptBuilder promptBuilder,
        IConversationalFlowService conversationalFlowService,
        IAiFlowValidator validator,
        AutoLayoutService layoutService,
        IOperatorFactory operatorFactory,
        IFlowTemplateService templateService,
        IScenarioMatcher scenarioMatcher,
        IRequirementBriefExtractor requirementBriefExtractor,
        IAiTurnRouter turnRouter,
        ITemplateConstraintValidator templateConstraintValidator,
        IAiFlowResponseParser responseParser,
        DryRunService dryRunService,
        IHostEnvironment hostEnvironment,
        IPromptVersionManager promptVersionManager,
        Microsoft.Extensions.Logging.ILogger<AiFlowGenerationService> logger)
        : this(
            aiOrchestrator,
            promptBuilder,
            conversationalFlowService,
            validator,
            layoutService,
            operatorFactory,
            templateService,
            scenarioMatcher,
            requirementBriefExtractor,
            turnRouter,
            templateConstraintValidator,
            responseParser,
            dryRunService,
            null,
            hostEnvironment,
            promptVersionManager,
            logger)
    {
    }

    public AiFlowGenerationService(
        AiGenerationOrchestrator aiOrchestrator,
        PromptBuilder promptBuilder,
        IConversationalFlowService conversationalFlowService,
        IAiFlowValidator validator,
        AutoLayoutService layoutService,
        IOperatorFactory operatorFactory,
        IFlowTemplateService templateService,
        IScenarioMatcher scenarioMatcher,
        IRequirementBriefExtractor requirementBriefExtractor,
        IAiTurnRouter turnRouter,
        ITemplateConstraintValidator templateConstraintValidator,
        IAiFlowResponseParser responseParser,
        DryRunService dryRunService,
        VisionAgentLoop? visionAgentLoop,
        IHostEnvironment hostEnvironment,
        IPromptVersionManager promptVersionManager,
        Microsoft.Extensions.Logging.ILogger<AiFlowGenerationService> logger)
    {
        _aiOrchestrator = aiOrchestrator;
        _promptBuilder = promptBuilder;
        _conversationalFlowService = conversationalFlowService;
        _validator = validator;
        _layoutService = layoutService;
        _operatorFactory = operatorFactory;
        _templateService = templateService;
        _scenarioMatcher = scenarioMatcher;
        _requirementBriefExtractor = requirementBriefExtractor;
        _turnRouter = turnRouter;
        _templateConstraintValidator = templateConstraintValidator;
        _responseParser = responseParser;
        _dryRunService = dryRunService;
        _visionAgentLoop = visionAgentLoop;
        _hostEnvironment = hostEnvironment;
        _promptVersionManager = promptVersionManager;
        _logger = logger;
    }

    public async Task<AiFlowGenerationResult> GenerateFlowAsync(
        AiFlowGenerationRequest request,
        Action<string>? onProgress = null,
        Action<AiStreamChunk>? onStreamChunk = null,
        CancellationToken cancellationToken = default,
        Action<GenerateFlowAttachmentReport>? onAttachmentReport = null)
    {
        var progressMessages = new List<string>();
        void ReportProgress(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            progressMessages.Add(message);
            onProgress?.Invoke(message);
        }

        var pipeline = new AiGenerationPipelineContext();

        // 获取当前活跃 Prompt 版本
        var activePromptVersion = await _promptVersionManager.GetActiveVersionAsync();

        // 推送：构建提示词
        ReportProgress("正在分析需求并构建提示词...");
        var conversationContext = pipeline.Measure(
            "conversation",
            () => _conversationalFlowService.PrepareContext(request),
            context => $"session={context.SessionId}, mode={context.Mode.ToWireValue()}");
        var sessionSnapshot = _conversationalFlowService.GetSession(conversationContext.SessionId);
        var hasExistingFlow = !string.IsNullOrWhiteSpace(conversationContext.ExistingFlowJson);
        var turnRoute = pipeline.Measure(
            "turn_router",
            () => _turnRouter.Route(new AiTurnRouteRequest(
                request.Description,
                request.AdditionalContext,
                request.Mode,
                sessionSnapshot,
                hasExistingFlow,
                request.Attachments)),
            route => $"intent={route.TurnIntent}, confidence={route.Confidence}",
            route => new Dictionary<string, string>
            {
                ["turnIntent"] = route.TurnIntent,
                ["interactionState"] = route.InteractionState,
                ["confidence"] = route.Confidence
            });
        if (turnRoute.ShouldShortCircuit)
        {
            return CreateInteractionMessageResult(
                conversationContext.SessionId,
                turnRoute,
                progressMessages,
                pipeline.Timeline.ToList());
        }

        var clarificationHistory = BuildClarificationHistoryContext(
            sessionSnapshot,
            request.Description);
        var effectiveMode = ResolveEffectiveMode(turnRoute, conversationContext.Mode, clarificationHistory);
        var detectedIntent = ResolveDetectedIntent(turnRoute, conversationContext.Intent);
        var manualRetryHistory = BuildManualRetryHistoryContext(
            sessionSnapshot,
            request.Description);
        var answeredClarificationFields = BuildAnsweredClarificationFields(
            clarificationHistory.AnsweredFields,
            request.Description,
            request.AdditionalContext);
        var priorUserRequirementContext = BuildPriorUserRequirementContext(
            conversationContext.SessionSummary,
            request.Description,
            clarificationHistory,
            manualRetryHistory);
        var templatePriority = await pipeline.MeasureAsync(
            "scenario_match",
            () => BuildTemplatePriorityContextAsync(request, priorUserRequirementContext, cancellationToken),
            context => context.IsTemplateFirst
                ? $"matched {context.Template?.Name ?? context.ScenarioName} ({context.Confidence:P0})"
                : "no confident template match",
            context => new Dictionary<string, string>
            {
                ["matchMode"] = context.MatchMode,
                ["scenarioKey"] = context.ScenarioKey,
                ["templateName"] = context.Template?.Name ?? string.Empty,
                ["confidence"] = context.Confidence.ToString("F4", CultureInfo.InvariantCulture)
            });
        var attachmentContext = BuildAttachmentContext(request.Attachments);
        var requirementContext = string.Join(
            Environment.NewLine,
            new[]
            {
                request.AdditionalContext,
                priorUserRequirementContext,
                attachmentContext
            }.Where(item => !string.IsNullOrWhiteSpace(item)));
        var requirementBrief = pipeline.Measure(
            "requirement_brief",
            () =>
            {
                var extracted = _requirementBriefExtractor.Extract(
                    request.Description,
                    requirementContext,
                    templatePriority.PrimaryMatch);
                ApplyExplicitCurrentAnswerOverrides(extracted, request.Description, request.AdditionalContext);
                ApplyAnsweredClarificationFields(extracted, answeredClarificationFields);
                var evaluated = _clarificationEngine.ApplyPolicy(extracted, effectiveMode, request.RequirementMode);
                evaluated = ApplyBlockingClarificationPolicy(evaluated, templatePriority, clarificationHistory);
                evaluated = ApplyTurnRoutePolicy(evaluated, turnRoute);
                evaluated = RelaxClarificationForTemplatePriority(evaluated, templatePriority);
                evaluated = EnforceClarificationRoundLimit(evaluated, clarificationHistory.ClarificationRounds);
                return RelaxClarificationForManualRetry(evaluated, manualRetryHistory);
            },
            brief => string.IsNullOrWhiteSpace(brief.ScenarioName)
                ? "requirement brief extracted without a scenario"
                : $"scenario={brief.ScenarioName}, clarifications={brief.ClarificationQuestions.Count}, blocking={brief.ClarificationRequired}",
            brief => new Dictionary<string, string>
            {
                ["scenarioKey"] = brief.ScenarioKey,
                ["intentType"] = brief.IntentType,
                ["clarificationCount"] = brief.ClarificationQuestions.Count.ToString(CultureInfo.InvariantCulture),
                ["clarificationRequired"] = brief.ClarificationRequired.ToString(CultureInfo.InvariantCulture),
                ["requirementMode"] = brief.RequirementMode
            });
        if (templatePriority.IsTemplateFirst)
        {
            ReportProgress($"已命中模板优先场景：{templatePriority.Template?.Name ?? templatePriority.ScenarioName}，进入 {templatePriority.GenerationMode} 模式...");
        }

        if (requirementBrief.ClarificationRequired)
        {
            ReportProgress("当前需求缺少关键字段，已进入澄清阶段。");
            pipeline.AddStage(
                "clarification",
                "blocked",
                $"missing={requirementBrief.MissingFacts.Count}, questions={requirementBrief.ClarificationQuestions.Count}",
                TimeSpan.Zero,
                new Dictionary<string, string>
                {
                    ["requirementMode"] = requirementBrief.RequirementMode,
                    ["missingCount"] = requirementBrief.MissingFacts.Count.ToString(CultureInfo.InvariantCulture),
                    ["questionCount"] = requirementBrief.ClarificationQuestions.Count.ToString(CultureInfo.InvariantCulture)
                });

            return CreateClarificationResult(
                conversationContext.SessionId,
                requirementBrief,
                templatePriority,
                detectedIntent,
                turnRoute,
                clarificationHistory,
                progressMessages,
                promptTrace: null,
                pipeline.Timeline.ToList());
        }

        // 读取当前激活模型快照（需在 BuildSystemPrompt 之前，以便传入 supportsJsonMode）
        var activeModel = _aiOrchestrator.ResolveGenerationModel();
        var selectionReason = _aiOrchestrator.ResolveSelectionReason();
        var options = activeModel.ToGenerationOptions();
        var capabilities = _aiOrchestrator.ResolveCapabilities(activeModel);
        var promptMode = AiPromptModes.Normalize(request.PromptMode);
        var useAgentTools = AiPromptModes.UsesAgentTools(promptMode) && _visionAgentLoop != null;

        var systemPrompt = useAgentTools
            ? string.Empty
            : pipeline.Measure(
                "prompt_context",
                () => _promptBuilder.BuildSystemPrompt(request.Description, capabilities.SupportsJsonMode),
                prompt => $"system prompt chars={prompt.Length}");
        var referenceFlowSummary = !useAgentTools && ShouldIncludeReferenceFlowSummary(effectiveMode)
            ? AiPromptComposer.BuildReferenceFlowSummary(conversationContext.ExistingFlowJson)
            : string.Empty;
        var userMessage = AiPromptComposer.BuildUserPrompt(new AiPromptRequest(
            Task: request.Description,
            Mode: effectiveMode,
            AdditionalContext: request.AdditionalContext,
            InteractionInstructions: BuildTurnRoutePromptSection(turnRoute),
            TemplatePriority: useAgentTools
                ? BuildAgentTemplatePriorityPromptSection(templatePriority)
                : BuildTemplatePriorityPromptSection(templatePriority),
            AttachmentContext: attachmentContext,
            SessionSummary: conversationContext.SessionSummary,
            ReferenceFlowSummary: referenceFlowSummary,
            RequirementBriefSection: BuildRequirementBriefPromptSection(requirementBrief)));
        GenerateFlowAttachmentReport promptTraceAttachmentReport = new();
        var promptTrace = !useAgentTools && ShouldIncludePromptTrace(request.DebugPrompt)
            ? new AiPromptTrace
            {
                Mode = effectiveMode.ToWireValue(),
                Provider = options.Provider,
                Model = options.Model,
                BaseUrl = options.BaseUrl,
                Capabilities = capabilities.Clone(),
                SystemPrompt = systemPrompt,
                UserPrompt = userMessage,
                UsedReferenceFlowSummary = referenceFlowSummary,
                PromptVersionId = activePromptVersion.Id.ToString(),
                PromptVersionName = activePromptVersion.Name,
                SelectionReason = selectionReason
            }
            : null;

        var maxAttachmentCount = capabilities.MaxImageCount > 0
            ? Math.Min(DefaultMaxMultimodalAttachmentCount, capabilities.MaxImageCount)
            : 0;
        var maxImageBytes = Math.Min(
            AiApiClient.MaxImageBytes,
            capabilities.MaxImageBytes > 0 ? capabilities.MaxImageBytes : AiApiClient.MaxImageBytes);

        var attachmentSelection = AnalyzeMultimodalAttachments(request.Attachments, maxAttachmentCount, maxImageBytes);
        promptTraceAttachmentReport = attachmentSelection.Report;
        if (request.Attachments is { Count: > 0 })
        {
            onAttachmentReport?.Invoke(attachmentSelection.Report);
        }

        IReadOnlyList<string> activeSendablePaths = attachmentSelection.SendablePaths;
        if (activeSendablePaths.Count > 0 && !capabilities.SupportsVisionInput)
        {
            _logger.LogInformation(
                "Model {Model} capability says vision input is unsupported. Falling back to text-only mode.",
                options.Model);
            activeSendablePaths = Array.Empty<string>();
            promptTraceAttachmentReport = BuildFallbackAttachmentReport(attachmentSelection.Report, "model_not_support_image");
            onAttachmentReport?.Invoke(promptTraceAttachmentReport);
            ReportProgress("当前模型不支持图片输入，已自动切换为文本模式（附件仅用于元信息）。");
        }
        if (promptTrace != null)
            promptTrace.AttachmentReport = promptTraceAttachmentReport;
        var currentUserMessage = useAgentTools
            ? new ChatMessage("user", userMessage)
            : BuildUserChatMessage(userMessage, activeSendablePaths);

        if (useAgentTools)
        {
            return await RunAgentToolsFlowAsync(
                request,
                conversationContext,
                detectedIntent,
                turnRoute,
                templatePriority,
                requirementBrief,
                progressMessages,
                pipeline,
                activePromptVersion,
                activeModel,
                capabilities,
                options,
                selectionReason,
                userMessage,
                promptTraceAttachmentReport,
                ReportProgress,
                cancellationToken);
        }

        AiGeneratedFlowJson? generatedFlow = null;
        AiValidationResult? lastValidation = null;
        List<AiAttemptDiagnostic> lastAttemptDiagnostics = new();
        string? lastRawResponse = null;
        int retryCount = 0;

        for (int attempt = 0; attempt <= options.MaxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Calling AI API, attempt {Attempt}", attempt + 1);

                if (attempt > 0)
                    ReportProgress($"上一轮请求未完成，正在重试（第 {attempt}/{options.MaxRetries} 次）...");
                else
                    ReportProgress("正在请求 AI 模型生成方案...");

                var messages = new List<ChatMessage> { currentUserMessage };

                // 调用 API（使用流式接口）
                var llmStopwatch = Stopwatch.StartNew();
                AiCompletionResult completionResult;
                try
                {
                    completionResult = await _aiOrchestrator.StreamCompleteAsync(
                        systemPrompt,
                        messages,
                        chunk => onStreamChunk?.Invoke(chunk),
                        activeModel,
                        cancellationToken);
                }
                catch (Exception llmEx)
                {
                    pipeline.AddStage("llm", "failed", llmEx.Message, llmStopwatch.Elapsed);
                    try
                    {
                        await _promptVersionManager.RecordMetricsAsync(
                            activePromptVersion.Id,
                            success: false,
                            tokenUsage: 0,
                            latencyMs: (long)llmStopwatch.Elapsed.TotalMilliseconds);
                    }
                    catch
                    {
                        // Never let metrics recording mask the original LLM exception
                    }

                    // 尝试备用模型（仅当存在独立的 fallback 绑定时）
                    var fallbackModel = _aiOrchestrator.ResolveFallbackModel();
                    if (fallbackModel.Id != activeModel.Id)
                    {
                        _logger.LogWarning(llmEx,
                            "Primary model {PrimaryModel} failed, attempting fallback model {FallbackModel}",
                            activeModel.Model, fallbackModel.Model);
                        ReportProgress($"主模型调用失败，正在切换到备用模型 {fallbackModel.Name}...");
                        try
                        {
                            completionResult = await _aiOrchestrator.StreamCompleteAsync(
                                systemPrompt,
                                messages,
                                chunk => onStreamChunk?.Invoke(chunk),
                                fallbackModel,
                                cancellationToken);
                            // 备用模型成功，切换后续上下文
                            activeModel = fallbackModel;
                            options = fallbackModel.ToGenerationOptions();
                            capabilities = _aiOrchestrator.ResolveCapabilities(fallbackModel);
                            selectionReason = "fallback";
                            if (promptTrace != null)
                            {
                                promptTrace.Model = fallbackModel.Model;
                                promptTrace.Provider = options.Provider;
                                promptTrace.SelectionReason = "fallback";
                            }
                            pipeline.AddStage("llm_fallback", "completed",
                                $"fallback_model={fallbackModel.Model}", llmStopwatch.Elapsed);
                        }
                        catch (Exception fallbackEx)
                        {
                            _logger.LogError(fallbackEx, "Fallback model {FallbackModel} also failed", fallbackModel.Model);
                            pipeline.AddStage("llm_fallback", "failed", fallbackEx.Message, llmStopwatch.Elapsed);
                            throw; // 主模型和备用模型都失败，抛出原始异常
                        }
                    }
                    else
                    {
                        throw;
                    }
                }
                var estimatedInputTokens = completionResult.TokenUsage?.InputTokens
                    ?? EstimateTokens(systemPrompt) + EstimateTokens(userMessage);
                var estimatedOutputTokens = completionResult.TokenUsage?.OutputTokens
                    ?? EstimateTokens(completionResult.Content);
                pipeline.EstimatedInputTokens = estimatedInputTokens;
                pipeline.EstimatedOutputTokens = estimatedOutputTokens;

                pipeline.AddStage(
                    "llm",
                    "completed",
                    $"model={activeModel.Model}, responseChars={completionResult.Content?.Length ?? 0}",
                    llmStopwatch.Elapsed,
                    new Dictionary<string, string>
                    {
                        ["provider"] = options.Provider,
                        ["model"] = options.Model,
                        ["supportsVision"] = capabilities.SupportsVisionInput.ToString(CultureInfo.InvariantCulture),
                        ["supportsJsonMode"] = capabilities.SupportsJsonMode.ToString(CultureInfo.InvariantCulture),
                        ["estimatedInputTokens"] = estimatedInputTokens.ToString(CultureInfo.InvariantCulture),
                        ["estimatedOutputTokens"] = estimatedOutputTokens.ToString(CultureInfo.InvariantCulture)
                    });

                _logger.LogInformation(
                    "LLM call completed. Model={Model}, InputTokens={InputTokens}, OutputTokens={OutputTokens}, LatencyMs={LatencyMs}",
                    activeModel.Model, estimatedInputTokens, estimatedOutputTokens, (long)llmStopwatch.Elapsed.TotalMilliseconds);

                // 记录 Prompt 版本指标
                await _promptVersionManager.RecordMetricsAsync(
                    activePromptVersion.Id,
                    success: true,
                    tokenUsage: estimatedInputTokens + estimatedOutputTokens,
                    latencyMs: (long)llmStopwatch.Elapsed.TotalMilliseconds);

                // 回填 token 估算到 promptTrace
                if (promptTrace != null)
                {
                    promptTrace.EstimatedInputTokens = estimatedInputTokens;
                    promptTrace.EstimatedOutputTokens = estimatedOutputTokens;
                }

                var rawResponse = completionResult.Content;
                lastRawResponse = rawResponse;
                _logger.LogDebug("AI 原始响应长度：{Length}", rawResponse?.Length ?? 0);
                if (!string.IsNullOrEmpty(completionResult.Reasoning))
                {
                    _logger.LogDebug("AI 思维链：{Reasoning}", completionResult.Reasoning[..Math.Min(200, completionResult.Reasoning.Length)] + "...");
                }
                if (string.IsNullOrWhiteSpace(rawResponse))
                {
                    var reasoningLength = completionResult.Reasoning?.Length ?? 0;
                    _logger.LogWarning("AI 流式响应正文为空。ReasoningLength={ReasoningLength}", reasoningLength);
                }

                // 推送：解析结果
                ReportProgress("收到 AI 响应，正在解析 JSON 数据...");
                // 解析 AI 输出的 JSON
                var parseResult = pipeline.Measure(
                    "parse",
                    () => _responseParser.Parse(rawResponse ?? string.Empty),
                    result => result.Success
                        ? $"operators={result.Flow!.Operators?.Count ?? 0}, connections={result.Flow.Connections?.Count ?? 0}, candidates={result.CandidateCount}"
                        : $"parse failed: {result.Code}, candidates={result.CandidateCount}");
                generatedFlow = parseResult.Flow;
                if (generatedFlow == null)
                {
                    lastValidation = BuildParseValidationResult(parseResult);
                    lastAttemptDiagnostics.AddRange(BuildAttemptDiagnostics(
                        attempt + 1,
                        "parse",
                        lastValidation,
                        lastRawResponse));
                    if (attempt < options.MaxRetries)
                    {
                        retryCount++;
                        ReportProgress($"AI 输出未通过解析，正在自动修复（第 {retryCount}/{options.MaxRetries} 次）...");
                        currentUserMessage = BuildUserChatMessage(
                            BuildRetryMessage(request.Description, lastValidation, lastRawResponse),
                            activeSendablePaths);
                        continue;
                    }

                    return CreateManualRetryResult(
                        stage: "parse",
                        conversationContext.SessionId,
                        request.Description,
                        lastValidation,
                        lastAttemptDiagnostics,
                        retryCount,
                        lastRawResponse,
                        promptTrace,
                        progressMessages,
                        requirementBrief,
                        turnRoute,
                        templatePriority.GenerationMode,
                        templatePriority.TemplateLockLevel,
                        BuildTemplateCandidates(templatePriority),
                        pipeline.Timeline.ToList());
                }

                ApplyTemplateMetadata(generatedFlow, templatePriority);
                ApplyModelEmbeddedNmsDefaults(generatedFlow);

                // 推送：校验结果
                ReportProgress("正在校验生成的算子和参数有效性...");
                // 校验
                lastValidation = pipeline.Measure(
                    "validator",
                    () => _validator.Validate(generatedFlow),
                    validation => validation.IsValid
                        ? $"valid with warnings={validation.Warnings.Count}"
                        : $"errors={validation.Errors.Count}, warnings={validation.Warnings.Count}");
                if (lastValidation.IsValid && templatePriority.IsTemplateFirst)
                {
                    var templateGate = pipeline.Measure(
                        "template_gate",
                        () => _templateConstraintValidator.Validate(
                            generatedFlow,
                            templatePriority.Template,
                            string.Equals(templatePriority.TemplateLockLevel, "strict", StringComparison.OrdinalIgnoreCase)),
                        validation => validation.IsValid
                            ? $"template gate passed with warnings={validation.Warnings.Count}"
                            : $"template gate errors={validation.Errors.Count}, warnings={validation.Warnings.Count}");
                    MergeValidationResult(lastValidation, templateGate);
                }
                lastAttemptDiagnostics.AddRange(BuildAttemptDiagnostics(
                    attempt + 1,
                    "validation",
                    lastValidation,
                    lastRawResponse));
                if (lastValidation.IsValid)
                {
                    // 校验通过，转换为 DTO 并返回
                    var (flowDto, actualOperatorIdMap) = ConvertToFlowDto(generatedFlow, request.Description);
                    pipeline.Measure(
                        "layout",
                        () => { _layoutService.ApplyLayout(flowDto); return true; },
                        _ => $"applied layout to {flowDto.Operators?.Count ?? 0} operators");

                    ReportProgress("正在进行 Dry-Run 沙箱安全校验与分支覆盖率统计...");

                    // S6-003: 转换并在虚拟沙箱中运行以收集覆盖率
                    object? dryRunReport = null;
                    try
                    {
                        var dryRunStopwatch = Stopwatch.StartNew();
                        var flowEntity = ConvertDtoToEntity(flowDto); // 暂时需转换为 Entity 供仿真使用
                        var drResult = await _dryRunService.RunAsync(
                            flowEntity,
                            new Dictionary<string, object>(), // 空输入
                            new DryRunStubRegistry(),
                            cancellationToken);
                        pipeline.AddStage(
                            "dryrun",
                            "completed",
                            $"success={drResult.IsSuccess}, coverage={drResult.CoveragePercentage:F1}%",
                            dryRunStopwatch.Elapsed);

                        dryRunReport = new
                        {
                            drResult.CoveragePercentage,
                            drResult.CoveredBranches,
                            drResult.TotalBranches,
                            drResult.IsSuccess
                        };
                    }
                    catch (Exception ex)
                    {
                        pipeline.AddStage("dryrun", "warning", ex.Message, TimeSpan.Zero);
                        _logger.LogWarning(ex, "DryRun 预演阶段异常，跳过覆盖率采集");
                    }

                    var recommendedTemplate = ResolveRecommendedTemplate(generatedFlow, templatePriority);
                    var pendingParameters = BuildPendingParameters(generatedFlow, actualOperatorIdMap);
                    var missingResources = BuildMissingResources(generatedFlow, templatePriority);
                    generatedFlow.PendingParameters = pendingParameters;
                    var assistantReply = BuildAssistantReply(generatedFlow, flowDto, recommendedTemplate);
                    var assistantPayload = new ConversationTurnPayload
                    {
                        Kind = "assistant_result",
                        Status = AiFlowGenerationResult.CompletionStatusCompleted,
                        InteractionState = ResolveCompletedInteractionState(turnRoute, pendingParameters),
                        TurnIntent = turnRoute.TurnIntent,
                        RouterConfidence = turnRoute.Confidence,
                        BlockingClarificationFields = requirementBrief.BlockingClarificationFields.ToList(),
                        NonBlockingMissingFields = requirementBrief.NonBlockingMissingFields.ToList(),
                        Reply = assistantReply,
                        Reasoning = ShouldIncludePromptTrace(request.DebugPrompt) ? completionResult.Reasoning : null,
                        Progress = progressMessages.ToList(),
                        RequirementBrief = requirementBrief,
                        ClarificationRequired = requirementBrief.ClarificationRequired
                    };

                    _conversationalFlowService.RecordAssistantResponse(
                        conversationContext.SessionId,
                        assistantReply,
                        JsonSerializer.Serialize(generatedFlow, _jsonOptions),
                        JsonSerializer.Serialize(flowDto, _jsonOptions),
                        assistantPayload);

                    return new AiFlowGenerationResult
                    {
                        Success = true,
                        CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                        Flow = flowDto,
                        AiExplanation = generatedFlow.Explanation,
                        Reasoning = ShouldIncludePromptTrace(request.DebugPrompt) ? completionResult.Reasoning : null,
                        ParametersNeedingReview = generatedFlow.ParametersNeedingReview,
                        RetryCount = retryCount,
                        SessionId = conversationContext.SessionId,
                        DetectedIntent = detectedIntent,
                        DryRunResult = dryRunReport,
                        ValidationPreview = new AiValidationPreview
                        {
                            FinalDryRun = dryRunReport
                        },
                        RecommendedTemplate = recommendedTemplate,
                        GenerationMode = templatePriority.GenerationMode,
                        TemplateLockLevel = templatePriority.TemplateLockLevel,
                        PendingParameters = pendingParameters,
                        MissingResources = missingResources,
                        PromptTrace = promptTrace,
                        RequirementBrief = requirementBrief,
                        TemplateCandidates = BuildTemplateCandidates(templatePriority),
                        StageTimeline = pipeline.Timeline.ToList(),
                        KnowledgeDiagnostics = ExtractKnowledgeDiagnostics(lastValidation),
                        TurnIntent = turnRoute.TurnIntent,
                        InteractionState = ResolveCompletedInteractionState(turnRoute, pendingParameters),
                        RouterConfidence = turnRoute.Confidence,
                        BlockingClarificationFields = requirementBrief.BlockingClarificationFields.ToList(),
                        NonBlockingMissingFields = requirementBrief.NonBlockingMissingFields.ToList()
                    };
                }

                _logger.LogWarning("AI 生成内容校验失败，错误：{Errors}",
                    string.Join("; ", lastValidation.Errors));
                if (attempt < options.MaxRetries)
                {
                    retryCount++;
                    ReportProgress($"AI 输出未通过结构校验，正在自动修复（第 {retryCount}/{options.MaxRetries} 次）...");
                    currentUserMessage = BuildUserChatMessage(
                        BuildRetryMessage(request.Description, lastValidation, lastRawResponse),
                        activeSendablePaths);
                    continue;
                }

                return CreateManualRetryResult(
                    stage: "validation",
                    conversationContext.SessionId,
                    request.Description,
                    lastValidation,
                    lastAttemptDiagnostics,
                    retryCount,
                    lastRawResponse,
                    promptTrace,
                    progressMessages,
                    requirementBrief,
                    turnRoute,
                    templatePriority.GenerationMode,
                    templatePriority.TemplateLockLevel,
                    BuildTemplateCandidates(templatePriority),
                    pipeline.Timeline.ToList());
            }
            catch (OperationCanceledException)
            {
                var wasUserCancelled = cancellationToken.IsCancellationRequested;
                var failureType = wasUserCancelled
                    ? AiFlowGenerationResult.FailureTypeUserCancelled
                    : AiFlowGenerationResult.FailureTypeTimeout;
                var completionStatus = wasUserCancelled
                    ? AiFlowGenerationResult.CompletionStatusCancelled
                    : AiFlowGenerationResult.CompletionStatusTimedOut;
                var errorMessage = wasUserCancelled
                    ? "用户已取消本次生成。"
                    : "AI generation timed out. Please retry.";
                var cancelledValidation = new AiValidationResult();
                cancelledValidation.AddError(
                    errorMessage,
                    code: wasUserCancelled ? "user_cancelled" : "generation_timeout",
                    category: "execution",
                    relatedFields: ["request"],
                    repairHint: wasUserCancelled
                        ? "如需继续，请在补充信息后重新发起生成。"
                        : "请稍后重试，或缩短输入与附件规模。");
                lastAttemptDiagnostics = BuildAttemptDiagnostics(
                    attempt + 1,
                    "execution",
                    cancelledValidation,
                    lastRawResponse);

                _logger.LogWarning(
                    "AI 生成被中断。WasUserCancelled={WasUserCancelled}, SessionId={SessionId}",
                    wasUserCancelled,
                    conversationContext.SessionId);
                var failureSummary = BuildFailureSummary(
                    cancelledValidation,
                    retryCount,
                    errorMessage,
                    lastRawResponse,
                    fallbackCode: wasUserCancelled ? "user_cancelled" : "generation_timeout",
                    fallbackCategory: "execution");
                RecordFailureResponse(
                    conversationContext.SessionId,
                    errorMessage,
                    lastRawResponse,
                    BuildFailureTurnPayload(
                        status: completionStatus,
                        summaryText: errorMessage,
                        failureSummary: failureSummary,
                        diagnostics: lastAttemptDiagnostics,
                        progressMessages: progressMessages,
                        requirementBrief: requirementBrief,
                        turnRoute: turnRoute));
                return new AiFlowGenerationResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    CompletionStatus = completionStatus,
                    FailureType = failureType,
                    FailureSummary = failureSummary,
                    LastAttemptDiagnostics = lastAttemptDiagnostics,
                    PromptTrace = promptTrace,
                    RequirementBrief = requirementBrief,
                    GenerationMode = templatePriority.GenerationMode,
                    TemplateLockLevel = templatePriority.TemplateLockLevel,
                    TemplateCandidates = BuildTemplateCandidates(templatePriority),
                    StageTimeline = pipeline.Timeline.ToList(),
                    TurnIntent = turnRoute.TurnIntent,
                    InteractionState = AiInteractionStates.Failed,
                    RouterConfidence = turnRoute.Confidence,
                    BlockingClarificationFields = requirementBrief.BlockingClarificationFields.ToList(),
                    NonBlockingMissingFields = requirementBrief.NonBlockingMissingFields.ToList()
                };
            }
            catch (Exception ex)
            {
                // 附件发送导致 400 时，自动降级为文本模式并重试一次，避免直接失败。
                if (activeSendablePaths.Count > 0 && IsBadRequestHttpException(ex))
                {
                    _logger.LogWarning(
                        ex,
                        "Multimodal request failed with 400. Fallback to text-only mode. Model={Model}, Provider={Provider}",
                        options.Model,
                        options.Provider);

                    activeSendablePaths = Array.Empty<string>();
                    currentUserMessage = BuildUserChatMessage(userMessage, activeSendablePaths);
                    promptTraceAttachmentReport = BuildFallbackAttachmentReport(attachmentSelection.Report, "model_not_support_image");
                    onAttachmentReport?.Invoke(promptTraceAttachmentReport);
                    if (promptTrace != null)
                        promptTrace.AttachmentReport = promptTraceAttachmentReport;
                    ReportProgress("图片附件暂不被当前模型/接口支持，已自动改为文本模式重试...");
                    retryCount++;
                    attempt--;
                    continue;
                }

                _logger.LogError(ex, "AI API 调用失败");
                var failureValidation = new AiValidationResult();
                failureValidation.AddError(
                    $"AI service call failed: {ex.Message}",
                    code: "service_call_failed",
                    category: "execution",
                    relatedFields: ["request"],
                    repairHint: "请检查模型服务状态、网络环境或输入约束后重试。");
                lastAttemptDiagnostics = BuildAttemptDiagnostics(
                    attempt + 1,
                    "execution",
                    failureValidation,
                    lastRawResponse);
                var errorMessage = $"AI service call failed: {ex.Message}";
                var failureSummary = BuildFailureSummary(
                    failureValidation,
                    retryCount,
                    errorMessage,
                    lastRawResponse,
                    fallbackCode: "service_call_failed",
                    fallbackCategory: "execution");
                RecordFailureResponse(
                    conversationContext.SessionId,
                    errorMessage,
                    lastRawResponse,
                    BuildFailureTurnPayload(
                        status: AiFlowGenerationResult.CompletionStatusFailed,
                        summaryText: errorMessage,
                        failureSummary: failureSummary,
                        diagnostics: lastAttemptDiagnostics,
                        progressMessages: progressMessages,
                        requirementBrief: requirementBrief,
                        turnRoute: turnRoute));
                return new AiFlowGenerationResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
                    FailureType = AiFlowGenerationResult.FailureTypeSystemError,
                    FailureSummary = failureSummary,
                    LastAttemptDiagnostics = lastAttemptDiagnostics,
                    PromptTrace = promptTrace,
                    RequirementBrief = requirementBrief,
                    GenerationMode = templatePriority.GenerationMode,
                    TemplateLockLevel = templatePriority.TemplateLockLevel,
                    TemplateCandidates = BuildTemplateCandidates(templatePriority),
                    StageTimeline = pipeline.Timeline.ToList(),
                    TurnIntent = turnRoute.TurnIntent,
                    InteractionState = AiInteractionStates.Failed,
                    RouterConfidence = turnRoute.Confidence,
                    BlockingClarificationFields = requirementBrief.BlockingClarificationFields.ToList(),
                    NonBlockingMissingFields = requirementBrief.NonBlockingMissingFields.ToList()
                };
            }
        }

        // 所有重试均失败
        var finalErrorMessage = BuildFinalValidationErrorMessage(lastValidation, retryCount);
        var finalFailureSummary = BuildFailureSummary(
            lastValidation,
            retryCount,
            finalErrorMessage,
            lastRawResponse,
            fallbackCode: "validation_failed",
            fallbackCategory: "validation");
        RecordFailureResponse(
            conversationContext.SessionId,
            finalErrorMessage,
            lastRawResponse,
            BuildFailureTurnPayload(
                status: AiFlowGenerationResult.CompletionStatusFailed,
                summaryText: finalErrorMessage,
                failureSummary: finalFailureSummary,
                diagnostics: lastAttemptDiagnostics,
                progressMessages: progressMessages,
                requirementBrief: requirementBrief,
                turnRoute: turnRoute));
        return new AiFlowGenerationResult
        {
            Success = false,
            ErrorMessage = finalErrorMessage,
            RetryCount = retryCount,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
            FailureSummary = finalFailureSummary,
            LastAttemptDiagnostics = lastAttemptDiagnostics,
            PromptTrace = promptTrace,
            RequirementBrief = requirementBrief,
            GenerationMode = templatePriority.GenerationMode,
            TemplateLockLevel = templatePriority.TemplateLockLevel,
            TemplateCandidates = BuildTemplateCandidates(templatePriority),
            StageTimeline = pipeline.Timeline.ToList(),
            KnowledgeDiagnostics = ExtractKnowledgeDiagnostics(lastValidation),
            TurnIntent = turnRoute.TurnIntent,
            InteractionState = AiInteractionStates.Failed,
            RouterConfidence = turnRoute.Confidence,
            BlockingClarificationFields = requirementBrief.BlockingClarificationFields.ToList(),
            NonBlockingMissingFields = requirementBrief.NonBlockingMissingFields.ToList()
        };
    }

    private static bool ShouldIncludeReferenceFlowSummary(GenerateFlowMode mode)
    {
        return mode is GenerateFlowMode.Modify or GenerateFlowMode.Explain or GenerateFlowMode.ReviewPendingParameters;
    }

    private static GenerateFlowMode ResolveEffectiveMode(
        AiTurnRoute route,
        GenerateFlowMode fallbackMode,
        ClarificationHistoryContext clarificationHistory)
    {
        if (route.TurnIntent == AiTurnIntents.ClarificationAnswer)
        {
            var clarifiedMode = ResolveModeFromTurnIntent(clarificationHistory.PendingTurnIntent);
            if (clarifiedMode.HasValue)
                return clarifiedMode.Value;
        }

        return route.TurnIntent switch
        {
            var intent when intent == AiTurnIntents.ModifyFlow => GenerateFlowMode.Modify,
            var intent when intent == AiTurnIntents.ExplainFlow => GenerateFlowMode.Explain,
            var intent when intent == AiTurnIntents.ReviewPendingParameters => GenerateFlowMode.ReviewPendingParameters,
            var intent when intent == AiTurnIntents.NewFlow => GenerateFlowMode.New,
            _ => fallbackMode
        };
    }

    private static GenerateFlowMode? ResolveModeFromTurnIntent(string? turnIntent)
    {
        return turnIntent switch
        {
            var intent when string.Equals(intent, AiTurnIntents.ModifyFlow, StringComparison.OrdinalIgnoreCase) => GenerateFlowMode.Modify,
            var intent when string.Equals(intent, AiTurnIntents.ExplainFlow, StringComparison.OrdinalIgnoreCase) => GenerateFlowMode.Explain,
            var intent when string.Equals(intent, AiTurnIntents.ReviewPendingParameters, StringComparison.OrdinalIgnoreCase) => GenerateFlowMode.ReviewPendingParameters,
            var intent when string.Equals(intent, AiTurnIntents.NewFlow, StringComparison.OrdinalIgnoreCase) => GenerateFlowMode.New,
            _ => null
        };
    }

    private static string ResolveDetectedIntent(AiTurnRoute route, ConversationIntent fallbackIntent)
    {
        return route.TurnIntent switch
        {
            var intent when intent == AiTurnIntents.ModifyFlow => "MODIFY",
            var intent when intent == AiTurnIntents.ExplainFlow => "EXPLAIN",
            var intent when intent == AiTurnIntents.ReviewPendingParameters => "REVIEW_PENDING_PARAMETERS",
            var intent when intent == AiTurnIntents.NewFlow => "NEW",
            var intent when intent == AiTurnIntents.ClarificationAnswer => "CLARIFICATION_ANSWER",
            var intent when intent == AiTurnIntents.ManualRetryRepair => "MANUAL_RETRY_REPAIR",
            _ => fallbackIntent.ToString().ToUpperInvariant()
        };
    }

    private static string ResolveCompletedInteractionState(
        AiTurnRoute route,
        IReadOnlyCollection<AiPendingParameterInfo>? pendingParameters)
    {
        if (pendingParameters is { Count: > 0 })
            return AiInteractionStates.ReviewingParameters;

        return AiInteractionStates.Completed;
    }

    private AiFlowGenerationResult CreateInteractionMessageResult(
        string sessionId,
        AiTurnRoute turnRoute,
        IReadOnlyList<string> progressMessages,
        IReadOnlyList<AiGenerationStageDiagnostic> stageTimeline)
    {
        var reply = string.IsNullOrWhiteSpace(turnRoute.Reply)
            ? "我还不能确定这一轮要做什么。请描述要检测、测量或识别的对象，以及希望输出到哪里。"
            : turnRoute.Reply.Trim();
        var assistantPayload = new ConversationTurnPayload
        {
            Kind = "assistant_interaction",
            Status = AiFlowGenerationResult.CompletionStatusCompleted,
            InteractionState = turnRoute.InteractionState,
            TurnIntent = turnRoute.TurnIntent,
            RouterConfidence = turnRoute.Confidence,
            Reply = reply,
            Progress = progressMessages.Where(item => !string.IsNullOrWhiteSpace(item)).ToList(),
            ClarificationRequired = false
        };

        _conversationalFlowService.RecordAssistantResponse(
            sessionId,
            reply,
            null,
            payload: assistantPayload);

        return new AiFlowGenerationResult
        {
            Success = true,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
            AiExplanation = reply,
            SessionId = sessionId,
            TurnIntent = turnRoute.TurnIntent,
            InteractionState = turnRoute.InteractionState,
            RouterConfidence = turnRoute.Confidence,
            StageTimeline = stageTimeline.ToList()
        };
    }

    private bool ShouldIncludePromptTrace(bool debugPrompt)
    {
        return debugPrompt || _hostEnvironment.IsDevelopment() || System.Diagnostics.Debugger.IsAttached;
    }

    private async Task<AiFlowGenerationResult> RunAgentToolsFlowAsync(
        AiFlowGenerationRequest request,
        ConversationContext conversationContext,
        string detectedIntent,
        AiTurnRoute turnRoute,
        TemplatePriorityContext templatePriority,
        AiRequirementBrief requirementBrief,
        IReadOnlyList<string> progressMessages,
        AiGenerationPipelineContext pipeline,
        PromptVersion activePromptVersion,
        AiModelConfig activeModel,
        AiModelCapabilities capabilities,
        AiGenerationOptions options,
        string selectionReason,
        string userMessage,
        GenerateFlowAttachmentReport attachmentReport,
        Action<string> reportProgress,
        CancellationToken cancellationToken)
    {
        reportProgress("Vision Agent 正在按需调用 ClearVision 内部工具...");
        var stopwatch = Stopwatch.StartNew();
        var allowRuntimePreviewTools = request.AllowRuntimePreviewTools || options.EnableRuntimePreviewTools;
        var agentAllowedPermissions = BuildAgentAllowedPermissions(allowRuntimePreviewTools);
        var agentResult = await _visionAgentLoop!.RunAsync(new VisionAgentLoopRequest
        {
            UserPrompt = userMessage,
            Model = activeModel,
            Capabilities = capabilities,
            ToolContext = new VisionAgentToolContext
            {
                UserDescription = request.Description,
                AdditionalContext = request.AdditionalContext,
                SessionId = conversationContext.SessionId,
                ExistingFlowJson = conversationContext.ExistingFlowJson,
                PromptMode = AiPromptModes.Normalize(request.PromptMode),
                DebugPrompt = request.DebugPrompt,
                AllowedPermissions = agentAllowedPermissions
            },
            Progress = progress => reportProgress($"{progress.Stage}|{progress.Message}")
        }, cancellationToken);
        stopwatch.Stop();

        var estimatedInputTokens = agentResult.TokenUsage?.InputTokens
            ?? EstimateTokens(agentResult.SystemPrompt) + EstimateTokens(agentResult.UserPrompt);
        var estimatedOutputTokens = agentResult.TokenUsage?.OutputTokens
            ?? EstimateTokens(agentResult.FinalContent);
        pipeline.EstimatedInputTokens = estimatedInputTokens;
        pipeline.EstimatedOutputTokens = estimatedOutputTokens;
        pipeline.AddStage(
            "agent_loop",
            agentResult.Success ? "completed" : "failed",
            agentResult.Success
                ? $"toolRounds={agentResult.ToolRounds}, toolCalls={agentResult.ToolTrace.Count}, responseChars={agentResult.FinalContent.Length}"
                : agentResult.ErrorMessage ?? "agent loop failed",
            stopwatch.Elapsed,
            new Dictionary<string, string>
            {
                ["provider"] = options.Provider,
                ["model"] = options.Model,
                ["promptMode"] = AiPromptModes.Normalize(request.PromptMode),
                ["estimatedInputTokens"] = estimatedInputTokens.ToString(CultureInfo.InvariantCulture),
                ["estimatedOutputTokens"] = estimatedOutputTokens.ToString(CultureInfo.InvariantCulture)
            });

        var promptTrace = ShouldIncludePromptTrace(request.DebugPrompt)
            ? new AiPromptTrace
            {
                Mode = AiPromptModes.Normalize(request.PromptMode),
                Provider = options.Provider,
                Model = options.Model,
                BaseUrl = options.BaseUrl,
                Capabilities = capabilities.Clone(),
                SystemPrompt = agentResult.SystemPrompt,
                UserPrompt = agentResult.UserPrompt,
                UsedReferenceFlowSummary = string.Empty,
                PromptVersionId = activePromptVersion.Id.ToString(),
                PromptVersionName = activePromptVersion.Name,
                SelectionReason = selectionReason,
                AttachmentReport = attachmentReport,
                EstimatedInputTokens = estimatedInputTokens,
                EstimatedOutputTokens = estimatedOutputTokens,
                ToolCallingMode = agentResult.ToolCallingMode
            }
            : null;

        if (!agentResult.Success)
        {
            return new AiFlowGenerationResult
            {
                Success = false,
                ErrorMessage = agentResult.ErrorMessage,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
                FailureType = AiFlowGenerationResult.FailureTypeManualRetryRequired,
                RetryCount = 0,
                SessionId = conversationContext.SessionId,
                DetectedIntent = detectedIntent,
                PromptTrace = promptTrace,
                RequirementBrief = requirementBrief,
                GenerationMode = templatePriority.GenerationMode,
                TemplateLockLevel = templatePriority.TemplateLockLevel,
                TemplateCandidates = BuildTemplateCandidates(templatePriority),
                StageTimeline = pipeline.Timeline.ToList(),
                ToolTrace = agentResult.ToolTrace,
                PendingActions = agentResult.PendingActions,
                TurnIntent = turnRoute.TurnIntent,
                InteractionState = AiInteractionStates.ManualRetry,
                RouterConfidence = turnRoute.Confidence,
                BlockingClarificationFields = requirementBrief.BlockingClarificationFields.ToList(),
                NonBlockingMissingFields = requirementBrief.NonBlockingMissingFields.ToList()
            };
        }

        reportProgress("Vision Agent 已返回 final_flow，正在解析和复用现有校验链...");
        var parseResult = pipeline.Measure(
            "parse",
            () => _responseParser.Parse(agentResult.FinalContent),
            result => result.Success
                ? $"operators={result.Flow!.Operators?.Count ?? 0}, connections={result.Flow.Connections?.Count ?? 0}, candidates={result.CandidateCount}"
                : $"parse failed: {result.Code}, candidates={result.CandidateCount}");
        var generatedFlow = parseResult.Flow;
        AiValidationResult? lastValidation = null;
        var repairAttempts = 0;
        if (generatedFlow == null)
        {
            var validation = BuildParseValidationResult(parseResult);
            var diagnostics = BuildAttemptDiagnostics(1, "parse", validation, agentResult.FinalContent);
            var repair = await TryRepairAgentFinalFlowAsync(
                request,
                conversationContext,
                activeModel,
                capabilities,
                agentResult,
                validation,
                diagnostics,
                "parse",
                templatePriority,
                pipeline,
                allowRuntimePreviewTools,
                reportProgress,
                cancellationToken);
            if (!repair.Success)
            {
                var manualRetry = CreateManualRetryResult(
                    stage: "agent_parse",
                    conversationContext.SessionId,
                    request.Description,
                    repair.Validation ?? validation,
                    repair.Diagnostics.Count > 0 ? repair.Diagnostics : diagnostics,
                    retryCount: repair.RepairAttempts,
                    lastRawResponse: repair.AgentResult?.FinalContent ?? agentResult.FinalContent,
                    promptTrace,
                    progressMessages,
                    requirementBrief,
                    turnRoute,
                    templatePriority.GenerationMode,
                    templatePriority.TemplateLockLevel,
                    BuildTemplateCandidates(templatePriority),
                    pipeline.Timeline.ToList());
                manualRetry.ToolTrace = repair.ToolTrace.Count > 0 ? repair.ToolTrace : agentResult.ToolTrace;
                manualRetry.PendingActions = repair.PendingActions.Count > 0 ? repair.PendingActions : agentResult.PendingActions;
                return manualRetry;
            }

            agentResult = repair.AgentResult!;
            generatedFlow = repair.GeneratedFlow!;
            lastValidation = repair.Validation!;
            repairAttempts += repair.RepairAttempts;
            if (promptTrace != null)
            {
                promptTrace.SystemPrompt = agentResult.SystemPrompt;
                promptTrace.UserPrompt = agentResult.UserPrompt;
                promptTrace.ToolCallingMode = agentResult.ToolCallingMode;
            }
        }

        ApplyTemplateMetadata(generatedFlow, templatePriority);
        ApplyModelEmbeddedNmsDefaults(generatedFlow);

        if (lastValidation == null)
        {
            lastValidation = pipeline.Measure(
                "validator",
                () => _validator.Validate(generatedFlow),
                validation => validation.IsValid
                    ? $"valid with warnings={validation.Warnings.Count}"
                    : $"errors={validation.Errors.Count}, warnings={validation.Warnings.Count}");
            if (lastValidation.IsValid && templatePriority.IsTemplateFirst)
            {
                var templateGate = pipeline.Measure(
                    "template_gate",
                    () => _templateConstraintValidator.Validate(
                        generatedFlow,
                        templatePriority.Template,
                        string.Equals(templatePriority.TemplateLockLevel, "strict", StringComparison.OrdinalIgnoreCase)),
                    validation => validation.IsValid
                        ? $"template gate passed with warnings={validation.Warnings.Count}"
                        : $"template gate errors={validation.Errors.Count}, warnings={validation.Warnings.Count}");
                MergeValidationResult(lastValidation, templateGate);
            }
        }

        if (!lastValidation.IsValid)
        {
            var diagnostics = BuildAttemptDiagnostics(1, "validation", lastValidation, agentResult.FinalContent);
            var repair = await TryRepairAgentFinalFlowAsync(
                request,
                conversationContext,
                activeModel,
                capabilities,
                agentResult,
                lastValidation,
                diagnostics,
                "validation",
                templatePriority,
                pipeline,
                allowRuntimePreviewTools,
                reportProgress,
                cancellationToken);
            if (!repair.Success)
            {
                var manualRetry = CreateManualRetryResult(
                    stage: "agent_validation",
                    conversationContext.SessionId,
                    request.Description,
                    repair.Validation ?? lastValidation,
                    repair.Diagnostics.Count > 0 ? repair.Diagnostics : diagnostics,
                    retryCount: repair.RepairAttempts,
                    lastRawResponse: repair.AgentResult?.FinalContent ?? agentResult.FinalContent,
                    promptTrace,
                    progressMessages,
                    requirementBrief,
                    turnRoute,
                    templatePriority.GenerationMode,
                    templatePriority.TemplateLockLevel,
                    BuildTemplateCandidates(templatePriority),
                    pipeline.Timeline.ToList());
                manualRetry.ToolTrace = repair.ToolTrace.Count > 0 ? repair.ToolTrace : agentResult.ToolTrace;
                manualRetry.PendingActions = repair.PendingActions.Count > 0 ? repair.PendingActions : agentResult.PendingActions;
                return manualRetry;
            }

            agentResult = repair.AgentResult!;
            generatedFlow = repair.GeneratedFlow!;
            lastValidation = repair.Validation!;
            repairAttempts += repair.RepairAttempts;
            if (promptTrace != null)
            {
                promptTrace.SystemPrompt = agentResult.SystemPrompt;
                promptTrace.UserPrompt = agentResult.UserPrompt;
                promptTrace.ToolCallingMode = agentResult.ToolCallingMode;
            }
        }

        var (flowDto, actualOperatorIdMap) = ConvertToFlowDto(generatedFlow, request.Description);
        pipeline.Measure(
            "layout",
            () => { _layoutService.ApplyLayout(flowDto); return true; },
            _ => $"applied layout to {flowDto.Operators?.Count ?? 0} operators");

        object? dryRunReport = null;
        try
        {
            var dryRunStopwatch = Stopwatch.StartNew();
            var flowEntity = ConvertDtoToEntity(flowDto);
            var drResult = await _dryRunService.RunAsync(
                flowEntity,
                new Dictionary<string, object>(),
                new DryRunStubRegistry(),
                cancellationToken);
            pipeline.AddStage(
                "dryrun",
                "completed",
                $"success={drResult.IsSuccess}, coverage={drResult.CoveragePercentage:F1}%",
                dryRunStopwatch.Elapsed);
            dryRunReport = new
            {
                drResult.CoveragePercentage,
                drResult.CoveredBranches,
                drResult.TotalBranches,
                drResult.IsSuccess
            };
        }
        catch (Exception ex)
        {
            pipeline.AddStage("dryrun", "warning", ex.Message, TimeSpan.Zero);
            _logger.LogWarning(ex, "Agent DryRun preview failed.");
        }

        var recommendedTemplate = ResolveRecommendedTemplate(generatedFlow, templatePriority);
        var pendingParameters = BuildPendingParameters(generatedFlow, actualOperatorIdMap);
        var missingResources = BuildMissingResources(generatedFlow, templatePriority);
        var pendingActions = agentResult.PendingActions
            .Concat(generatedFlow.PendingActions ?? new List<VisionAgentPendingAction>())
            .ToList();
        generatedFlow.PendingParameters = pendingParameters;

        var assistantReply = BuildAssistantReply(generatedFlow, flowDto, recommendedTemplate);
        var assistantPayload = new ConversationTurnPayload
        {
            Kind = "assistant_result",
            Status = AiFlowGenerationResult.CompletionStatusCompleted,
            InteractionState = ResolveCompletedInteractionState(turnRoute, pendingParameters),
            TurnIntent = turnRoute.TurnIntent,
            RouterConfidence = turnRoute.Confidence,
            BlockingClarificationFields = requirementBrief.BlockingClarificationFields.ToList(),
            NonBlockingMissingFields = requirementBrief.NonBlockingMissingFields.ToList(),
            Reply = assistantReply,
            Reasoning = ShouldIncludePromptTrace(request.DebugPrompt) ? agentResult.Reasoning : null,
            Progress = progressMessages.ToList(),
            RequirementBrief = requirementBrief,
            ClarificationRequired = requirementBrief.ClarificationRequired
        };

        _conversationalFlowService.RecordAssistantResponse(
            conversationContext.SessionId,
            assistantReply,
            JsonSerializer.Serialize(generatedFlow, _jsonOptions),
            JsonSerializer.Serialize(flowDto, _jsonOptions),
            assistantPayload);

        return new AiFlowGenerationResult
        {
            Success = true,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
            Flow = flowDto,
            AiExplanation = generatedFlow.Explanation,
            Reasoning = ShouldIncludePromptTrace(request.DebugPrompt) ? agentResult.Reasoning : null,
            ParametersNeedingReview = generatedFlow.ParametersNeedingReview,
            RetryCount = repairAttempts,
            SessionId = conversationContext.SessionId,
            DetectedIntent = detectedIntent,
            DryRunResult = dryRunReport,
            RecommendedTemplate = recommendedTemplate,
            GenerationMode = templatePriority.GenerationMode,
            TemplateLockLevel = templatePriority.TemplateLockLevel,
            PendingParameters = pendingParameters,
            MissingResources = missingResources,
            PendingActions = pendingActions,
            ToolTrace = agentResult.ToolTrace,
            ValidationPreview = BuildValidationPreview(dryRunReport, agentResult.ToolTrace),
            PromptTrace = promptTrace,
            RequirementBrief = requirementBrief,
            TemplateCandidates = BuildTemplateCandidates(templatePriority),
            StageTimeline = pipeline.Timeline.ToList(),
            KnowledgeDiagnostics = ExtractKnowledgeDiagnostics(lastValidation),
            TurnIntent = turnRoute.TurnIntent,
            InteractionState = ResolveCompletedInteractionState(turnRoute, pendingParameters),
            RouterConfidence = turnRoute.Confidence,
            BlockingClarificationFields = requirementBrief.BlockingClarificationFields.ToList(),
            NonBlockingMissingFields = requirementBrief.NonBlockingMissingFields.ToList()
        };
    }

    private async Task<AgentFinalRepairResult> TryRepairAgentFinalFlowAsync(
        AiFlowGenerationRequest request,
        ConversationContext conversationContext,
        AiModelConfig activeModel,
        AiModelCapabilities capabilities,
        VisionAgentLoopResult initialAgentResult,
        AiValidationResult initialValidation,
        List<AiAttemptDiagnostic> initialDiagnostics,
        string failureStage,
        TemplatePriorityContext templatePriority,
        AiGenerationPipelineContext pipeline,
        bool allowRuntimePreviewTools,
        Action<string> reportProgress,
        CancellationToken cancellationToken)
    {
        var diagnostics = initialDiagnostics.Select(CloneAttemptDiagnostic).ToList();
        var toolTrace = initialAgentResult.ToolTrace.ToList();
        var pendingActions = initialAgentResult.PendingActions.ToList();
        var lastValidation = initialValidation;
        var lastAgentResult = initialAgentResult;
        AiGeneratedFlowJson? generatedFlow = null;

        for (var attempt = 1; attempt <= AgentFinalRepairAttempts; attempt++)
        {
            var errorCount = Math.Max(1, lastValidation.Diagnostics.Count(item => item.Severity == AiValidationSeverity.Error));
            reportProgress($"agent_repair|发现 {errorCount} 个错误，正在回灌 Agent 修复（第 {attempt}/{AgentFinalRepairAttempts} 轮）...");
            var repairPrompt = BuildAgentFinalRepairPrompt(
                request.Description,
                lastAgentResult.FinalContent,
                lastValidation,
                failureStage,
                attempt);

            var stopwatch = Stopwatch.StartNew();
            var repairResult = await _visionAgentLoop!.RunAsync(new VisionAgentLoopRequest
            {
                UserPrompt = repairPrompt,
                Model = activeModel,
                Capabilities = capabilities,
                ToolContext = new VisionAgentToolContext
                {
                    UserDescription = request.Description,
                    AdditionalContext = request.AdditionalContext,
                    SessionId = conversationContext.SessionId,
                    ExistingFlowJson = conversationContext.ExistingFlowJson,
                    PromptMode = AiPromptModes.Normalize(request.PromptMode),
                    DebugPrompt = request.DebugPrompt,
                    AllowedPermissions = BuildAgentAllowedPermissions(allowRuntimePreviewTools)
                },
                Progress = progress => reportProgress($"{progress.Stage}|{progress.Message}")
            }, cancellationToken);
            stopwatch.Stop();

            toolTrace.AddRange(repairResult.ToolTrace);
            pendingActions.AddRange(repairResult.PendingActions);
            pipeline.AddStage(
                "agent_final_repair",
                repairResult.Success ? "completed" : "failed",
                repairResult.Success
                    ? $"attempt={attempt}, toolRounds={repairResult.ToolRounds}, responseChars={repairResult.FinalContent.Length}"
                    : repairResult.ErrorMessage ?? "agent final repair failed",
                stopwatch.Elapsed,
                new Dictionary<string, string>
                {
                    ["attempt"] = attempt.ToString(CultureInfo.InvariantCulture),
                    ["failureStage"] = failureStage,
                    ["toolCallingMode"] = repairResult.ToolCallingMode
                });

            lastAgentResult = repairResult with
            {
                ToolTrace = toolTrace.ToList(),
                PendingActions = pendingActions.ToList()
            };

            if (!repairResult.Success)
            {
                return AgentFinalRepairResult.Failed(
                    lastAgentResult,
                    lastValidation,
                    diagnostics,
                    toolTrace,
                    pendingActions,
                    attempt);
            }

            var parseResult = pipeline.Measure(
                "parse",
                () => _responseParser.Parse(repairResult.FinalContent),
                result => result.Success
                    ? $"operators={result.Flow!.Operators?.Count ?? 0}, connections={result.Flow.Connections?.Count ?? 0}, candidates={result.CandidateCount}"
                    : $"parse failed: {result.Code}, candidates={result.CandidateCount}");
            generatedFlow = parseResult.Flow;
            if (generatedFlow == null)
            {
                lastValidation = BuildParseValidationResult(parseResult);
                diagnostics.AddRange(BuildAttemptDiagnostics(
                    attempt + 1,
                    "parse",
                    lastValidation,
                    repairResult.FinalContent));
                continue;
            }

            ApplyTemplateMetadata(generatedFlow, templatePriority);
            ApplyModelEmbeddedNmsDefaults(generatedFlow);
            lastValidation = pipeline.Measure(
                "validator",
                () => _validator.Validate(generatedFlow),
                validation => validation.IsValid
                    ? $"valid with warnings={validation.Warnings.Count}"
                    : $"errors={validation.Errors.Count}, warnings={validation.Warnings.Count}");

            if (lastValidation.IsValid && templatePriority.IsTemplateFirst)
            {
                var templateGate = pipeline.Measure(
                    "template_gate",
                    () => _templateConstraintValidator.Validate(
                        generatedFlow,
                        templatePriority.Template,
                        string.Equals(templatePriority.TemplateLockLevel, "strict", StringComparison.OrdinalIgnoreCase)),
                    validation => validation.IsValid
                        ? $"template gate passed with warnings={validation.Warnings.Count}"
                        : $"template gate errors={validation.Errors.Count}, warnings={validation.Warnings.Count}");
                MergeValidationResult(lastValidation, templateGate);
            }

            diagnostics.AddRange(BuildAttemptDiagnostics(
                attempt + 1,
                "validation",
                lastValidation,
                repairResult.FinalContent));

            if (lastValidation.IsValid)
            {
                return AgentFinalRepairResult.Repaired(
                    lastAgentResult,
                    generatedFlow,
                    lastValidation,
                    diagnostics,
                    toolTrace,
                    pendingActions,
                    attempt);
            }
        }

        return AgentFinalRepairResult.Failed(
            lastAgentResult,
            lastValidation,
            diagnostics,
            toolTrace,
            pendingActions,
            AgentFinalRepairAttempts);
    }

    private static HashSet<VisionAgentToolPermission> BuildAgentAllowedPermissions(bool allowRuntimePreviewTools)
    {
        var permissions = new HashSet<VisionAgentToolPermission>
        {
            VisionAgentToolPermission.ReadOnly,
            VisionAgentToolPermission.Simulation,
            VisionAgentToolPermission.ConfigDraft,
            VisionAgentToolPermission.DeploymentPrepare
        };

        if (allowRuntimePreviewTools)
        {
            permissions.Add(VisionAgentToolPermission.RuntimePreview);
        }

        return permissions;
    }

    private static string BuildAgentFinalRepairPrompt(
        string originalDescription,
        string previousFinalContent,
        AiValidationResult validation,
        string failureStage,
        int repairAttempt)
    {
        var payload = new
        {
            kind = "backend_final_flow_repair",
            repairAttempt,
            failureStage,
            originalDescription,
            validation_result = BuildStructuredValidationPayload(
                validation,
                includeTemplateDiagnostics: false),
            template_gate_result = BuildStructuredValidationPayload(
                validation,
                includeTemplateDiagnostics: true),
            previous_final_flow = TrimRetryOutput(previousFinalContent)
        };

        return "Backend parse/validator/template_gate rejected the previous final_flow. " +
               "Use the structured result below to repair only the failing fields and return one final_flow JSON object.\n" +
               JsonSerializer.Serialize(payload, _jsonOptions);
    }

    private static object BuildStructuredValidationPayload(
        AiValidationResult validation,
        bool includeTemplateDiagnostics)
    {
        var diagnostics = validation.Diagnostics
            .Where(issue => includeTemplateDiagnostics == IsTemplateGateDiagnostic(issue))
            .ToList();
        var issues = diagnostics.Select(issue => new
        {
            severity = issue.Severity,
            code = issue.Code,
            category = issue.Category,
            message = issue.Message,
            relatedFields = issue.RelatedFields,
            operatorId = issue.OperatorId,
            parameterName = issue.ParameterName,
            portName = issue.TargetPortName ?? issue.SourcePortName,
            sourcePortName = issue.SourcePortName,
            targetPortName = issue.TargetPortName,
            repairHint = issue.RepairHint
        }).ToList();

        return new
        {
            isValid = includeTemplateDiagnostics
                ? diagnostics.All(issue => issue.Severity != AiValidationSeverity.Error)
                : validation.IsValid,
            errors = issues
                .Where(issue => issue.severity == AiValidationSeverity.Error)
                .ToList(),
            warnings = issues
                .Where(issue => issue.severity == AiValidationSeverity.Warning)
                .ToList(),
            issues
        };
    }

    private static bool IsTemplateGateDiagnostic(AiValidationDiagnostic issue)
    {
        return string.Equals(issue.Category, "template", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(issue.Category, "template_gate", StringComparison.OrdinalIgnoreCase);
    }

    private static AiValidationPreview BuildValidationPreview(
        object? finalDryRun,
        IReadOnlyList<VisionAgentToolTrace> toolTrace)
    {
        var toolDryRunTrace = toolTrace
            .Where(item =>
                string.Equals(item.ToolName, "dryrun_flow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.ToolName, "replay_flow_with_frame", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.ToolName, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase))
            .Select(item => new
            {
                item.ToolName,
                item.Success,
                ResultSummary = item.ValidationPreviewSummary ?? item.ResultSummary,
                item.ErrorMessage,
                item.DurationMs,
                item.ToolCallingMode
            })
            .Cast<object>()
            .ToList();

        return new AiValidationPreview
        {
            StructuralDryRun = FindLastToolResult(toolTrace, "dryrun_flow"),
            FrameReplay = FindLastToolResult(toolTrace, "replay_flow_with_frame"),
            FinalDryRun = finalDryRun,
            RuntimePackagePrecheck = FindLastToolResult(toolTrace, "runtime_package_precheck"),
            ToolDryRunTrace = toolDryRunTrace
        };
    }

    private static object? FindLastToolResult(
        IReadOnlyList<VisionAgentToolTrace> toolTrace,
        string toolName)
    {
        return toolTrace
            .LastOrDefault(item => string.Equals(item.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
            is { } trace
                ? trace.ValidationPreviewSummary ?? trace.ResultSummary
                : null;
    }

    private static string BuildTurnRoutePromptSection(AiTurnRoute route)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"turnIntent={route.TurnIntent}");
        sb.AppendLine($"routerConfidence={route.Confidence}");

        if (route.TurnIntent == AiTurnIntents.ModifyFlow)
        {
            sb.AppendLine("This is an incremental modification of the current workflow.");
            sb.AppendLine("Only change the user-requested parts. Keep unrelated operators, connections, parameter values, and resource placeholders unchanged.");
            sb.AppendLine("If the user asks for Chinese localization, localize only user-visible displayName, explanation, and notes. Keep operatorType, port names, parameter keys, and JSON runtime contracts exactly as the catalog defines them.");
        }
        else if (route.TurnIntent == AiTurnIntents.ExplainFlow)
        {
            sb.AppendLine("Explain the current workflow without changing operators, connections, runtime keys, or parameter values.");
        }
        else if (route.TurnIntent == AiTurnIntents.ReviewPendingParameters)
        {
            sb.AppendLine("Review and fill only pending parameters or missing resources the user explicitly provides. Keep topology stable.");
        }
        else if (route.TurnIntent == AiTurnIntents.ManualRetryRepair)
        {
            sb.AppendLine("This is a repair after parser or validator failure. Do not re-enter requirement clarification. Return a complete corrected workflow JSON.");
        }

        return sb.ToString().Trim();
    }

    private static string BuildTemplatePriorityPromptSection(TemplatePriorityContext templatePriority)
    {
        if (!templatePriority.IsTemplateFirst)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("templateFirst=true");
        sb.AppendLine($"matchMode={templatePriority.MatchMode}");
        sb.AppendLine($"generationMode={templatePriority.GenerationMode}");
        sb.AppendLine($"templateLockLevel={templatePriority.TemplateLockLevel}");
        if (!string.IsNullOrWhiteSpace(templatePriority.ScenarioKey))
            sb.AppendLine($"scenarioKey={templatePriority.ScenarioKey}");
        sb.AppendLine($"matchReason={templatePriority.MatchReason}");
        if (templatePriority.Confidence > 0)
            sb.AppendLine($"confidence={templatePriority.Confidence:F2}");
        if (templatePriority.MatchedFields.Count > 0)
            sb.AppendLine($"matchedFields={string.Join(",", templatePriority.MatchedFields)}");
        if (templatePriority.MissingSignals.Count > 0)
            sb.AppendLine($"missingSignals={string.Join(",", templatePriority.MissingSignals)}");

        if (templatePriority.Template != null)
        {
            sb.AppendLine($"templateId={templatePriority.Template.Id}");
            sb.AppendLine($"templateName={templatePriority.Template.Name}");
            sb.AppendLine($"templateVersion={templatePriority.Template.TemplateVersion}");
            sb.AppendLine($"templateIndustry={templatePriority.Template.Industry}");

            if (!string.IsNullOrWhiteSpace(templatePriority.Template.FlowJson))
            {
                sb.AppendLine("templateSkeletonJson:");
                sb.AppendLine("```json");
                sb.AppendLine(TrimTemplateFlowJson(templatePriority.Template.FlowJson));
                sb.AppendLine("```");
                if (string.Equals(templatePriority.GenerationMode, "template_fill", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("Strict template_fill rules: do not remove required template operators, do not replace the core topology, and do not invent operators outside the template skeleton. Only fill parameters, explanation, pendingParameters, missingResources, and user-facing notes.");
                }
                else
                {
                    sb.AppendLine("Reuse the template skeleton as the starting point unless the request explicitly asks to replace it.");
                }
            }
        }
        else
        {
            sb.AppendLine("No exact reusable template asset is available. Keep the workflow in the wire-sequence pattern.");
        }

        sb.AppendLine("Include recommendedTemplate, pendingParameters, and missingResources in the JSON output.");
        return sb.ToString().Trim();
    }

    private static string BuildAgentTemplatePriorityPromptSection(TemplatePriorityContext templatePriority)
    {
        if (!templatePriority.IsTemplateFirst)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("templateFirst=true");
        sb.AppendLine($"matchMode={templatePriority.MatchMode}");
        sb.AppendLine($"generationMode={templatePriority.GenerationMode}");
        sb.AppendLine($"templateLockLevel={templatePriority.TemplateLockLevel}");
        if (!string.IsNullOrWhiteSpace(templatePriority.ScenarioKey))
            sb.AppendLine($"scenarioKey={templatePriority.ScenarioKey}");
        if (templatePriority.Template != null)
        {
            sb.AppendLine($"templateId={templatePriority.Template.Id}");
            sb.AppendLine($"templateName={templatePriority.Template.Name}");
            sb.AppendLine("Call get_flow_template_skeleton if the template backbone is needed.");
        }
        else
        {
            sb.AppendLine("Call match_flow_template to find a reusable template if needed.");
        }

        return sb.ToString().Trim();
    }

    private static string BuildRequirementBriefPromptSection(AiRequirementBrief requirementBrief)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"requirementMode={requirementBrief.RequirementMode}");
        sb.AppendLine($"scenarioKey={requirementBrief.ScenarioKey}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.ScenarioName))
            sb.AppendLine($"scenarioName={requirementBrief.ScenarioName}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.IntentType))
            sb.AppendLine($"intentType={requirementBrief.IntentType}");
        sb.AppendLine($"confidence={requirementBrief.Confidence.ToString("F4", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"clarificationRequired={requirementBrief.ClarificationRequired.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"hasOpenQuestions={requirementBrief.HasOpenQuestions.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"canGenerateDraftNow={requirementBrief.CanGenerateDraftNow.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"draftRiskLevel={requirementBrief.DraftRiskLevel}");

        if (!string.IsNullOrWhiteSpace(requirementBrief.ObjectName))
            sb.AppendLine($"objectName={requirementBrief.ObjectName}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.ImageSource))
            sb.AppendLine($"imageSource={requirementBrief.ImageSource}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.OutputTarget))
            sb.AppendLine($"outputTarget={requirementBrief.OutputTarget}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.DecisionRule))
            sb.AppendLine($"decisionRule={requirementBrief.DecisionRule}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.RoiRequirement))
            sb.AppendLine($"roiRequirement={requirementBrief.RoiRequirement}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.CalibrationRequirement))
            sb.AppendLine($"calibrationRequirement={requirementBrief.CalibrationRequirement}");

        if (requirementBrief.ObjectTypes.Count > 0)
            sb.AppendLine($"objectTypes={string.Join(",", requirementBrief.ObjectTypes.Take(6))}");
        if (requirementBrief.DefectTypes.Count > 0)
            sb.AppendLine($"defectTypes={string.Join(",", requirementBrief.DefectTypes.Take(6))}");
        if (requirementBrief.MeasurementTargets.Count > 0)
            sb.AppendLine($"measurementTargets={string.Join(",", requirementBrief.MeasurementTargets.Take(6))}");
        if (requirementBrief.RequiredResources.Count > 0)
            sb.AppendLine($"requiredResources={string.Join(",", requirementBrief.RequiredResources.Take(6))}");
        if (requirementBrief.RequiredFields.Count > 0)
            sb.AppendLine($"requiredFields={string.Join(",", requirementBrief.RequiredFields.Take(8))}");
        if (requirementBrief.BlockingClarificationFields.Count > 0)
            sb.AppendLine($"blockingClarificationFields={string.Join(",", requirementBrief.BlockingClarificationFields.Take(8))}");
        if (requirementBrief.NonBlockingMissingFields.Count > 0)
            sb.AppendLine($"nonBlockingMissingFields={string.Join(",", requirementBrief.NonBlockingMissingFields.Take(8))}");

        if (requirementBrief.KnownFacts.Count > 0)
        {
            sb.AppendLine("knownFacts:");
            foreach (var fact in requirementBrief.KnownFacts.Take(8))
            {
                sb.AppendLine($"- {fact}");
            }
        }

        if (requirementBrief.MissingFacts.Count > 0)
        {
            sb.AppendLine("missingFacts:");
            foreach (var fact in requirementBrief.MissingFacts.Take(8))
            {
                sb.AppendLine($"- {fact}");
            }
        }

        if (requirementBrief.AttachmentFacts.Count > 0)
        {
            sb.AppendLine("attachmentFacts:");
            foreach (var fact in requirementBrief.AttachmentFacts.Take(4))
            {
                sb.AppendLine($"- {fact}");
            }
        }

        if (requirementBrief.ClarificationQuestions.Count > 0)
        {
            sb.AppendLine("clarificationQuestions:");
            foreach (var question in requirementBrief.ClarificationQuestions.Take(3))
            {
                var options = question.Options.Count > 0
                    ? $" | options={string.Join(" / ", question.Options.Take(4))}"
                    : string.Empty;
                sb.AppendLine($"- [{question.Priority}] {question.Question}{options}");
                if (!string.IsNullOrWhiteSpace(question.Reason))
                {
                    sb.AppendLine($"  reason={question.Reason}");
                }
            }
        }

        return sb.ToString().Trim();
    }

    private static ChatMessage BuildUserChatMessage(string userMessage, IReadOnlyList<string> sendablePaths)
    {
        if (sendablePaths.Count == 0)
            return new ChatMessage("user", userMessage);

        var parts = new List<ChatMessageContentPart>(sendablePaths.Count + 1)
        {
            ChatMessageContentPart.TextPart(userMessage)
        };
        parts.AddRange(sendablePaths.Select(path => ChatMessageContentPart.ImageFile(path, "high")));
        return new ChatMessage("user", parts);
    }

    private static AttachmentSelectionResult AnalyzeMultimodalAttachments(
        IReadOnlyList<string>? attachments,
        int maxCount,
        int maxImageBytes)
    {
        if (attachments == null || attachments.Count == 0 || maxCount <= 0)
        {
            return new AttachmentSelectionResult(Array.Empty<string>(), new GenerateFlowAttachmentReport());
        }

        var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sendablePaths = new List<string>(Math.Min(attachments.Count, maxCount));
        var sent = new List<GenerateFlowAttachmentSentItem>();
        var skipped = new List<GenerateFlowAttachmentSkippedItem>();

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment))
                continue;

            var normalizedPath = attachment.Trim();
            if (!dedup.Add(normalizedPath))
                continue;

            var name = Path.GetFileName(normalizedPath);
            if (sendablePaths.Count >= maxCount)
            {
                skipped.Add(new GenerateFlowAttachmentSkippedItem
                {
                    Path = normalizedPath,
                    Name = name,
                    Reason = "limit_exceeded"
                });
                continue;
            }

            if (!File.Exists(normalizedPath))
            {
                skipped.Add(new GenerateFlowAttachmentSkippedItem
                {
                    Path = normalizedPath,
                    Name = name,
                    Reason = "file_missing"
                });
                continue;
            }

            var extension = Path.GetExtension(normalizedPath);
            if (!AiApiClient.IsSupportedImageExtension(extension))
            {
                skipped.Add(new GenerateFlowAttachmentSkippedItem
                {
                    Path = normalizedPath,
                    Name = name,
                    Reason = "unsupported_format"
                });
                continue;
            }

            try
            {
                var info = new FileInfo(normalizedPath);
                if (info.Length <= 0 || info.Length > maxImageBytes)
                {
                    skipped.Add(new GenerateFlowAttachmentSkippedItem
                    {
                        Path = normalizedPath,
                        Name = name,
                        Reason = "file_too_large"
                    });
                    continue;
                }

                using var stream = File.Open(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length <= 0)
                {
                    skipped.Add(new GenerateFlowAttachmentSkippedItem
                    {
                        Path = normalizedPath,
                        Name = name,
                        Reason = "read_failed"
                    });
                    continue;
                }
            }
            catch
            {
                skipped.Add(new GenerateFlowAttachmentSkippedItem
                {
                    Path = normalizedPath,
                    Name = name,
                    Reason = "read_failed"
                });
                continue;
            }

            sendablePaths.Add(normalizedPath);
            sent.Add(new GenerateFlowAttachmentSentItem
            {
                Path = normalizedPath,
                Name = name
            });
        }

        return new AttachmentSelectionResult(
            sendablePaths,
            new GenerateFlowAttachmentReport
            {
                Sent = sent,
                Skipped = skipped
            });
    }

    private static List<string> NormalizeAttachmentPaths(IReadOnlyList<string>? attachments, int maxCount)
    {
        if (attachments == null || attachments.Count == 0 || maxCount <= 0)
            return new List<string>();

        var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedPaths = new List<string>(Math.Min(attachments.Count, maxCount));

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment))
                continue;

            var normalized = attachment.Trim();
            if (!dedup.Add(normalized))
                continue;

            normalizedPaths.Add(normalized);
            if (normalizedPaths.Count >= maxCount)
                break;
        }

        return normalizedPaths;
    }

    private static bool IsBadRequestHttpException(Exception ex)
    {
        if (ex is not HttpRequestException httpEx)
            return false;

        if (httpEx.StatusCode == HttpStatusCode.BadRequest)
            return true;

        return httpEx.Message.Contains("400", StringComparison.OrdinalIgnoreCase);
    }

    private static GenerateFlowAttachmentReport BuildFallbackAttachmentReport(GenerateFlowAttachmentReport originalReport, string reason)
    {
        var skipped = new List<GenerateFlowAttachmentSkippedItem>(originalReport.Skipped);
        foreach (var sent in originalReport.Sent)
        {
            skipped.Add(new GenerateFlowAttachmentSkippedItem
            {
                Path = sent.Path,
                Name = sent.Name,
                Reason = reason
            });
        }

        return new GenerateFlowAttachmentReport
        {
            Sent = new List<GenerateFlowAttachmentSentItem>(),
            Skipped = skipped
        };
    }

    private string BuildAttachmentContext(IReadOnlyList<string>? attachments)
    {
        var normalizedPaths = NormalizeAttachmentPaths(attachments, maxCount: 8);
        if (normalizedPaths.Count == 0)
            return string.Empty;

        var lines = new List<string>(normalizedPaths.Count);
        for (var i = 0; i < normalizedPaths.Count; i++)
        {
            lines.Add($"{i + 1}. {DescribeAttachment(normalizedPaths[i])}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string DescribeAttachment(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (!File.Exists(filePath))
            return $"{fileName} | path={filePath} | status=missing";

        try
        {
            var fileInfo = new FileInfo(filePath);
            var extension = string.IsNullOrWhiteSpace(fileInfo.Extension)
                ? "unknown"
                : fileInfo.Extension.TrimStart('.').ToLowerInvariant();

            var imageSize = TryGetImageSize(filePath);
            var sizeText = FormatByteSize(fileInfo.Length);

            if (imageSize.HasValue)
            {
                var (width, height) = imageSize.Value;
                return $"{fileName} | path={filePath} | type={extension} | size={sizeText} | resolution={width}x{height}";
            }

            return $"{fileName} | path={filePath} | type={extension} | size={sizeText} | resolution=unknown";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read attachment metadata: {AttachmentPath}", filePath);
            return $"{fileName} | path={filePath} | status=metadata_unavailable";
        }
    }

    private static (int Width, int Height)? TryGetImageSize(string filePath)
    {
        try
        {
            using var image = Cv2.ImRead(filePath, ImreadModes.Unchanged);
            if (image.Empty())
                return null;

            return (image.Width, image.Height);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatByteSize(long byteSize)
    {
        if (byteSize < 1024)
            return $"{byteSize}B";
        if (byteSize < 1024 * 1024)
            return $"{(byteSize / 1024d).ToString("F1", CultureInfo.InvariantCulture)}KB";

        return $"{(byteSize / 1024d / 1024d).ToString("F2", CultureInfo.InvariantCulture)}MB";
    }

    private sealed record AttachmentSelectionResult(
        IReadOnlyList<string> SendablePaths,
        GenerateFlowAttachmentReport Report);

    private async Task<TemplatePriorityContext> BuildTemplatePriorityContextAsync(
        AiFlowGenerationRequest request,
        string? priorUserRequirementContext,
        CancellationToken cancellationToken)
    {
        var matcherContext = string.Join(
            Environment.NewLine,
            new[] { request.AdditionalContext, priorUserRequirementContext }
                .Where(item => !string.IsNullOrWhiteSpace(item)));
        var matches = await _scenarioMatcher.MatchAsync(
            request.Description,
            matcherContext,
            request.Attachments,
            topN: 3,
            cancellationToken);
        var top = matches.FirstOrDefault();
        var selectionMode = NormalizeTemplateSelectionMode(request.TemplateSelection?.Mode);

        if (selectionMode == "free_generate")
        {
            return TemplatePriorityContext.None with
            {
                MatchMode = "user-selected-free",
                Confidence = top?.Confidence ?? 0,
                PrimaryMatch = top,
                Candidates = matches.ToList()
            };
        }

        if (selectionMode is "template_fill" or "template_adapt")
        {
            var selectedMatch = await ResolveSelectedTemplateMatchAsync(
                request.TemplateSelection,
                matches,
                cancellationToken);
            if (selectedMatch != null)
            {
                return BuildSelectedTemplatePriorityContext(
                    selectedMatch,
                    matches,
                    selectionMode);
            }
        }

        if (top == null || top.Confidence < 0.35)
            return TemplatePriorityContext.None with { Candidates = matches.ToList() };

        var explicitFreeGenerate = IsExplicitFreeGenerateRequest(request);
        var generationMode = explicitFreeGenerate
            ? "free_generate"
            : top.Confidence >= 0.75 ? "template_fill" : "template_adapt";
        var lockLevel = generationMode switch
        {
            "template_fill" => "strict",
            "template_adapt" => "relaxed",
            _ => "none"
        };

        return new TemplatePriorityContext(
            IsTemplateFirst: !explicitFreeGenerate,
            Template: top.Template,
            MatchReason: top.MatchReason,
            MatchMode: "scenario-matcher",
            Confidence: top.Confidence,
            MatchedKeywords: top.MatchedFields,
            ScenarioKey: top.Scenario.ScenarioKey,
            ScenarioName: top.Scenario.ScenarioName,
            GenerationMode: generationMode,
            TemplateLockLevel: lockLevel,
            MatchedFields: top.MatchedFields,
            MissingSignals: top.MissingSignals,
            PrimaryMatch: top,
            Candidates: matches.ToList());
    }

    private async Task<ScenarioMatchResult?> ResolveSelectedTemplateMatchAsync(
        AiTemplateSelectionInfo? selection,
        IReadOnlyList<ScenarioMatchResult> matches,
        CancellationToken cancellationToken)
    {
        if (selection == null)
            return null;

        var selected = matches.FirstOrDefault(match => MatchesTemplateSelection(match, selection));
        if (selected != null)
            return selected;

        FlowTemplate? template = null;
        if (Guid.TryParse(selection.TemplateId, out var templateId))
        {
            var templateTask = _templateService.GetTemplateAsync(templateId, cancellationToken);
            template = templateTask == null ? null : await templateTask;
        }

        if (template == null && !string.IsNullOrWhiteSpace(selection.ScenarioKey))
        {
            var templatesTask = _templateService.GetTemplatesAsync(cancellationToken: cancellationToken);
            var templates = templatesTask == null
                ? Array.Empty<FlowTemplate>()
                : await templatesTask;
            template = templates.FirstOrDefault(item =>
                string.Equals(item.ScenarioKey, selection.ScenarioKey, StringComparison.OrdinalIgnoreCase));
        }

        if (template == null)
            return null;

        var scenarioKey = !string.IsNullOrWhiteSpace(template.ScenarioKey)
            ? template.ScenarioKey!
            : selection.ScenarioKey ?? string.Empty;
        return new ScenarioMatchResult
        {
            Template = template,
            Confidence = 1,
            MatchReason = "User selected template",
            MatchedFields = ["userSelection"],
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = scenarioKey,
                ScenarioName = template.Name,
                TemplateName = template.Name,
                TemplateVersion = template.TemplateVersion,
                Industry = template.Industry,
                TemplateId = template.Id == Guid.Empty ? null : template.Id.ToString()
            }
        };
    }

    private static AiRequirementBrief RelaxClarificationForTemplatePriority(
        AiRequirementBrief brief,
        TemplatePriorityContext templatePriority)
    {
        if (!brief.ClarificationRequired)
            return brief;

        if (!templatePriority.IsTemplateFirst ||
            string.IsNullOrWhiteSpace(templatePriority.ScenarioKey) ||
            templatePriority.Confidence < 0.75)
        {
            return brief;
        }

        if (!string.Equals(templatePriority.GenerationMode, "template_fill", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(templatePriority.GenerationMode, "template_adapt", StringComparison.OrdinalIgnoreCase))
        {
            return brief;
        }

        if (!brief.CanGenerateDraftNow)
            return brief;

        if (HasBlockingMeasurementClarification(brief))
            return brief;

        // 高频模板命中已经给出主拓扑，缺陷类别、ROI、模型资源等应作为待确认项进入草稿，
        // 不能把用户锁死在反复澄清里。
        brief.ClarificationRequired = false;
        brief.RequirementMode = AiRequirementModes.Draft;
        if (string.Equals(brief.DraftRiskLevel, "low", StringComparison.OrdinalIgnoreCase) &&
            (brief.MissingFacts.Count > 0 || brief.ClarificationQuestions.Count > 0))
        {
            brief.DraftRiskLevel = "medium";
        }

        return brief;
    }

    private static bool HasBlockingMeasurementClarification(AiRequirementBrief brief)
    {
        if (!brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase))
            return false;

        return brief.MissingFacts.Any(fact =>
            string.Equals(MapMissingFactToField(fact), "measurement_target", StringComparison.OrdinalIgnoreCase));
    }

    private static AiRequirementBrief ApplyBlockingClarificationPolicy(
        AiRequirementBrief brief,
        TemplatePriorityContext templatePriority,
        ClarificationHistoryContext clarificationHistory)
    {
        var allFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in brief.RequiredFields.Where(field => !string.IsNullOrWhiteSpace(field)))
            allFields.Add(field);
        foreach (var fact in brief.MissingFacts)
        {
            var field = MapMissingFactToField(fact);
            if (!string.IsNullOrWhiteSpace(field))
                allFields.Add(field);
        }
        foreach (var question in brief.ClarificationQuestions)
        {
            if (!string.IsNullOrWhiteSpace(question.Field))
                allFields.Add(question.Field);
        }

        var blockingFields = allFields
            .Where(field => IsBlockingClarificationField(field, brief, templatePriority))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var nonBlockingFields = allFields
            .Where(field => !string.IsNullOrWhiteSpace(field) &&
                            !blockingFields.Contains(field, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var askedFields = clarificationHistory.PendingQuestions
            .Select(question => question.Field)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var askedFingerprints = clarificationHistory.PendingQuestions
            .Select(BuildQuestionFingerprint)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reachedClarificationLimit = clarificationHistory.ClarificationRounds >= 2;
        var activeBlockingFields = blockingFields
            .Where(field =>
                !askedFields.Contains(field) ||
                clarificationHistory.AnsweredFields.Count > 0 ||
                !reachedClarificationLimit)
            .ToList();

        brief.NonBlockingMissingFields = nonBlockingFields;
        brief.BlockingClarificationFields = activeBlockingFields;
        brief.RequiredFields = brief.RequiredFields
            .Where(field => activeBlockingFields.Contains(field, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        brief.MissingFacts = brief.MissingFacts
            .Where(fact =>
            {
                var field = MapMissingFactToField(fact);
                return !string.IsNullOrWhiteSpace(field) &&
                       activeBlockingFields.Contains(field, StringComparer.OrdinalIgnoreCase);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        brief.ClarificationQuestions = brief.ClarificationQuestions
            .Where(question => activeBlockingFields.Contains(question.Field, StringComparer.OrdinalIgnoreCase))
            .Where(question => clarificationHistory.AnsweredFields.Count > 0 ||
                               !reachedClarificationLimit ||
                               !askedFingerprints.Contains(BuildQuestionFingerprint(question)))
            .GroupBy(question => question.Field, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(3)
            .ToList();

        if (blockingFields.Count > 0 && activeBlockingFields.Count == 0 && clarificationHistory.ClarificationRounds > 0)
        {
            brief.ClarificationRequired = false;
            brief.CanGenerateDraftNow = true;
            brief.RequirementMode = AiRequirementModes.Draft;
            brief.DraftRiskLevel = "high";
            AddKnownFact(brief, "已避免重复澄清同一字段，本轮按高风险草稿继续生成。");
        }
        else
        {
            brief.ClarificationRequired = activeBlockingFields.Count > 0 &&
                                          brief.ClarificationQuestions.Count > 0;
            if (!brief.ClarificationRequired)
            {
                brief.CanGenerateDraftNow = true;
                if (nonBlockingFields.Count > 0)
                    brief.RequirementMode = AiRequirementModes.Draft;
                if (nonBlockingFields.Count > 0 &&
                    string.Equals(brief.DraftRiskLevel, "low", StringComparison.OrdinalIgnoreCase))
                {
                    brief.DraftRiskLevel = "medium";
                }
            }
            else
            {
                brief.CanGenerateDraftNow = false;
            }
        }

        brief.HasOpenQuestions = brief.MissingFacts.Count > 0 || brief.ClarificationQuestions.Count > 0;
        return brief;
    }

    private static bool IsBlockingClarificationField(
        string field,
        AiRequirementBrief brief,
        TemplatePriorityContext templatePriority)
    {
        if (string.IsNullOrWhiteSpace(field))
            return false;

        return field switch
        {
            "scene" => string.IsNullOrWhiteSpace(brief.IntentType) &&
                       string.IsNullOrWhiteSpace(brief.ScenarioKey) &&
                       !templatePriority.IsTemplateFirst &&
                       brief.Confidence < 0.35,
            "object_type" => (RequiresInspectionObject(brief) ||
                              brief.RequiredFields.Contains("object_type", StringComparer.OrdinalIgnoreCase) ||
                              brief.MissingFacts.Any(fact => string.Equals(MapMissingFactToField(fact), "object_type", StringComparison.OrdinalIgnoreCase))) &&
                             string.IsNullOrWhiteSpace(brief.ObjectName) &&
                             brief.ObjectTypes.Count == 0 &&
                             !templatePriority.IsTemplateFirst,
            "defect_type" => brief.IntentType.Contains("defect", StringComparison.OrdinalIgnoreCase) &&
                             brief.DefectTypes.Count == 0 &&
                             !templatePriority.IsTemplateFirst,
            "measurement_target" => brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase) &&
                                    brief.MeasurementTargets.Count == 0,
            "ambiguous_negative_signal" => true,
            _ => false
        };
    }

    private static bool RequiresInspectionObject(AiRequirementBrief brief)
    {
        if (!string.IsNullOrWhiteSpace(brief.IntentType))
        {
            return ContainsAny(
                brief.IntentType,
                ["defect", "presence", "sequence", "measurement", "classification", "ocr", "code"]);
        }

        return brief.RequiredFields.Contains("object_type", StringComparer.OrdinalIgnoreCase);
    }

    private static AiRequirementBrief ApplyTurnRoutePolicy(
        AiRequirementBrief brief,
        AiTurnRoute turnRoute)
    {
        if (!turnRoute.ShouldBypassClarification)
            return brief;

        brief.ClarificationRequired = false;
        brief.HasOpenQuestions = false;
        brief.CanGenerateDraftNow = true;
        brief.RequirementMode = AiRequirementModes.Draft;
        var bypassedFields = brief.MissingFacts
            .Select(MapMissingFactToField)
            .Concat(brief.ClarificationQuestions.Select(question => question.Field))
            .Concat(brief.RequiredFields)
            .Where(field => !string.IsNullOrWhiteSpace(field));
        brief.NonBlockingMissingFields = brief.NonBlockingMissingFields
            .Concat(bypassedFields)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        brief.BlockingClarificationFields.Clear();
        brief.RequiredFields.Clear();
        brief.ClarificationQuestions.Clear();
        brief.MissingFacts.Clear();

        if (turnRoute.TurnIntent == AiTurnIntents.ModifyFlow)
        {
            AddKnownFact(brief, "本轮是对当前工程的增量修改，不重新进入需求澄清。");
        }
        else if (turnRoute.TurnIntent == AiTurnIntents.ManualRetryRepair)
        {
            AddKnownFact(brief, "本轮是上一轮格式/结构失败后的手动修复，不重新进入需求澄清。");
        }

        return brief;
    }

    private static AiRequirementBrief EnforceClarificationRoundLimit(
        AiRequirementBrief brief,
        int clarificationRounds)
    {
        if (!brief.ClarificationRequired || clarificationRounds < 2)
            return brief;

        brief.ClarificationRequired = false;
        brief.RequirementMode = AiRequirementModes.Draft;
        brief.CanGenerateDraftNow = true;
        brief.DraftRiskLevel = "high";
        if (!brief.KnownFacts.Contains("已达到澄清上限，本轮将按高风险草稿继续生成。", StringComparer.OrdinalIgnoreCase))
        {
            brief.KnownFacts.Add("已达到澄清上限，本轮将按高风险草稿继续生成。");
        }

        return brief;
    }

    private static AiRequirementBrief RelaxClarificationForManualRetry(
        AiRequirementBrief brief,
        ManualRetryHistoryContext manualRetryHistory)
    {
        if (!manualRetryHistory.IsRepairRequest || !manualRetryHistory.HasPendingManualRetry)
            return brief;

        // A manual retry is a repair of a model/validator failure after the requirement gate
        // has already been passed. Re-entering clarification here loses the original task.
        brief.ClarificationRequired = false;
        brief.RequirementMode = AiRequirementModes.Draft;
        brief.CanGenerateDraftNow = true;
        if (brief.MissingFacts.Count > 0 || brief.ClarificationQuestions.Count > 0)
        {
            brief.DraftRiskLevel = "high";
        }

        AddKnownFact(brief, "本轮是上一轮格式/结构失败后的手动修复，不重新进入需求澄清。");
        return brief;
    }

    private static void ApplyAnsweredClarificationFields(
        AiRequirementBrief brief,
        IReadOnlySet<string> answeredFields)
    {
        if (answeredFields.Count == 0)
            return;

        brief.RequiredFields = brief.RequiredFields
            .Where(field => !answeredFields.Contains(field))
            .ToList();
        brief.ClarificationQuestions = brief.ClarificationQuestions
            .Where(question => !answeredFields.Contains(question.Field))
            .ToList();
        brief.MissingFacts = brief.MissingFacts
            .Where(fact => !answeredFields.Contains(MapMissingFactToField(fact)))
            .ToList();

        brief.HasOpenQuestions = brief.MissingFacts.Count > 0 || brief.ClarificationQuestions.Count > 0;
        if (!brief.HasOpenQuestions)
        {
            brief.ClarificationRequired = false;
            brief.CanGenerateDraftNow = true;
            if (string.Equals(brief.DraftRiskLevel, "high", StringComparison.OrdinalIgnoreCase))
                brief.DraftRiskLevel = "medium";
        }
    }

    private static IReadOnlySet<string> BuildAnsweredClarificationFields(
        IReadOnlySet<string> historyAnsweredFields,
        string? description,
        string? additionalContext)
    {
        var fields = new HashSet<string>(historyAnsweredFields, StringComparer.OrdinalIgnoreCase);
        var text = $"{description} {additionalContext}".Trim();
        if (string.IsNullOrWhiteSpace(text))
            return fields;

        if (ContainsFieldScopedAnswer(text, "calibration", ["标定", "换算", "像素到物理", "物理单位"]))
            fields.Add("calibration");
        if (ContainsFieldScopedAnswer(text, "output_target", ["输出目标", "输出", "结果去向"]))
            fields.Add("output_target");
        if (ContainsFieldScopedAnswer(text, "roi", ["ROI", "区域", "范围"]))
            fields.Add("roi");
        if (ContainsFieldScopedAnswer(text, "measurement_target", ["测量目标", "测量项", "尺寸", "距离", "孔距", "圆心距"]))
            fields.Add("measurement_target");
        if (ContainsFieldScopedAnswer(text, "object_type", ["检测对象", "对象", "产品"]))
            fields.Add("object_type");
        if (ContainsFieldScopedAnswer(text, "defect_type", ["缺陷类别", "缺陷类型", "缺陷", "瑕疵"]))
            fields.Add("defect_type");

        return fields;
    }

    private static void ApplyExplicitCurrentAnswerOverrides(
        AiRequirementBrief brief,
        string? description,
        string? additionalContext)
    {
        var text = $"{description} {additionalContext}".Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (HasCalibrationNotRequiredAnswer(text))
        {
            brief.CalibrationRequirement = "none";
            AddKnownFact(brief, "标定：不需要");
        }

        if (ContainsAny(text, ["ROI", "区域", "范围"]) &&
            ContainsAny(text, ["固定ROI", "固定 ROI", "固定区域", "固定范围"]))
        {
            brief.RoiRequirement = "region";
        }
    }

    private static bool ContainsFieldScopedAnswer(
        string text,
        string field,
        IEnumerable<string> labels)
    {
        if (!ContainsAny(text, labels))
            return false;

        return field switch
        {
            "calibration" => HasCalibrationNotRequiredAnswer(text) ||
                             ContainsAny(text, ["像素到物理单位换算", "手眼标定", "相机标定", "标定文件", "mm", "毫米"]),
            "output_target" => ContainsAny(text, ["PLC", "数据库", "界面", "UI", "屏幕", "文件", "CSV", "Excel"]),
            "roi" => ContainsAny(text, ["固定", "整图", "多ROI", "自动定位", "区域", "范围"]),
            "measurement_target" => ContainsAny(text, ["孔距", "圆心距", "间距", "距离", "直径", "半径", "角度", "mm", "毫米"]),
            "object_type" => ContainsAny(text, ["包装箱", "纸箱", "箱体", "产品", "金属件", "连接器", "端子", "空调", "内机", "外机", "遥控器", "铜孔", "圆孔", "孔位", "标签"]),
            "defect_type" => ContainsAny(text, ["破损", "裂纹", "划伤", "划痕", "压痕", "凹坑", "脏污", "污渍", "标签异常", "封箱异常", "scratch", "dent", "damage", "broken", "stain"]),
            _ => false
        };
    }

    private static bool HasCalibrationNotRequiredAnswer(string text)
    {
        if (!ContainsAny(text, ["标定", "换算", "像素到物理", "物理单位"]))
            return false;

        return ContainsAny(text, ["不需要", "无需", "不用", "不做", "不要", "none", "no calibration"]);
    }

    private static void AddKnownFact(AiRequirementBrief brief, string fact)
    {
        if (!brief.KnownFacts.Contains(fact, StringComparer.OrdinalIgnoreCase))
            brief.KnownFacts.Add(fact);
    }

    private static ClarificationHistoryContext BuildClarificationHistoryContext(
        ConversationSession? session,
        string? currentDescription)
    {
        if (session == null || session.History.Count == 0)
            return ClarificationHistoryContext.Empty;

        var answeredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingQuestions = new List<AiClarificationQuestion>();
        var latestPendingQuestions = new List<AiClarificationQuestion>();
        var latestPendingTurnIntent = string.Empty;
        var clarificationRounds = 0;
        var normalizedCurrent = NormalizeRequirementContextText(currentDescription);

        foreach (var turn in session.History.OrderBy(item => item.TimestampUtc))
        {
            if (turn.Payload?.ClarificationRequired == true)
            {
                clarificationRounds++;
                pendingQuestions = turn.Payload.RequirementBrief?.ClarificationQuestions
                    ?.Where(question => !string.IsNullOrWhiteSpace(question.Field))
                    .ToList() ?? new List<AiClarificationQuestion>();
                latestPendingQuestions = pendingQuestions.ToList();
                latestPendingTurnIntent = ResolveClarificationPayloadIntent(turn.Payload, latestPendingTurnIntent);
                continue;
            }

            if (turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            {
                if (IsIgnorableInteractionTurn(turn))
                    continue;

                // 一旦出现非澄清助手回复，上一段澄清回合已经结束。
                clarificationRounds = 0;
                answeredFields.Clear();
                pendingQuestions.Clear();
                latestPendingQuestions.Clear();
                latestPendingTurnIntent = string.Empty;
                continue;
            }

            if (!turn.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(turn.Message))
            {
                continue;
            }

            var isCurrentUserTurn = string.Equals(
                NormalizeRequirementContextText(turn.Message),
                normalizedCurrent,
                StringComparison.OrdinalIgnoreCase);
            if (isCurrentUserTurn && ShouldResetClarificationHistoryForCurrentTurn(turn.Message, pendingQuestions))
            {
                clarificationRounds = 0;
                answeredFields.Clear();
                pendingQuestions.Clear();
                latestPendingQuestions.Clear();
                latestPendingTurnIntent = string.Empty;
                continue;
            }

            if (pendingQuestions.Count == 0)
                continue;

            foreach (var question in pendingQuestions)
            {
                if (LooksLikeAnswerForField(turn.Message, question))
                    answeredFields.Add(question.Field);
            }
        }

        if (ShouldResetClarificationHistoryForCurrentTurn(currentDescription, latestPendingQuestions))
            return ClarificationHistoryContext.Empty;

        return new ClarificationHistoryContext(clarificationRounds, answeredFields, latestPendingQuestions, latestPendingTurnIntent);
    }

    private static bool ShouldResetClarificationHistoryForCurrentTurn(
        string? currentDescription,
        IReadOnlyList<AiClarificationQuestion> pendingQuestions)
    {
        var text = currentDescription ?? string.Empty;
        if (!LooksLikeNewRequirementTurn(text))
            return false;

        var answersPendingQuestion = pendingQuestions.Any(question => LooksLikeAnswerForField(text, question));
        if (answersPendingQuestion && !LooksLikeExplicitNewFlowCommand(text))
            return false;

        if (IsSelfContainedNewRequirement(text))
            return true;

        return pendingQuestions.Count == 0 ||
               !answersPendingQuestion;
    }

    private static bool LooksLikeExplicitNewFlowCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return ContainsAny(text,
        [
            "生成", "创建", "新建", "构建", "搭建", "做一个", "做套", "帮我做", "帮我构建", "帮我搭建",
            "重新做", "重做", "从头", "new flow", "create", "build", "generate", "start over", "from scratch"
        ]);
    }

    private static string ResolveClarificationPayloadIntent(
        ConversationTurnPayload payload,
        string previousPendingTurnIntent)
    {
        return string.Equals(payload.TurnIntent, AiTurnIntents.ClarificationAnswer, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(previousPendingTurnIntent)
            ? previousPendingTurnIntent
            : payload.TurnIntent ?? string.Empty;
    }

    private static ManualRetryHistoryContext BuildManualRetryHistoryContext(
        ConversationSession? session,
        string? currentDescription)
    {
        var isRepairRequest = IsManualRetryRepairRequest(currentDescription);
        if (!isRepairRequest || session == null || session.History.Count == 0)
            return new ManualRetryHistoryContext(isRepairRequest, false);

        var normalizedCurrent = NormalizeRequirementContextText(currentDescription);
        foreach (var turn in session.History.OrderByDescending(item => item.TimestampUtc))
        {
            if (turn.Role.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeRequirementContextText(turn.Message), normalizedCurrent, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsIgnorableInteractionTurn(turn))
                continue;

            var manualRetry = turn.Payload?.ManualRetry;
            return new ManualRetryHistoryContext(
                isRepairRequest,
                manualRetry?.Required == true ||
                string.Equals(turn.Payload?.Status, AiFlowGenerationResult.FailureTypeManualRetryRequired, StringComparison.OrdinalIgnoreCase));
        }

        return new ManualRetryHistoryContext(isRepairRequest, false);
    }

    private static bool IsIgnorableInteractionTurn(ConversationTurn turn)
    {
        var payload = turn.Payload;
        return string.Equals(payload?.Kind, "assistant_interaction", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(payload?.TurnIntent, AiTurnIntents.ChatOrHelp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(payload?.TurnIntent, AiTurnIntents.Unknown, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsManualRetryRepairRequest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return ContainsAny(text,
        [
            "请基于上一轮需求继续修正工作流 JSON",
            "请只返回一个完整且可解析的 JSON 对象",
            "上一轮输出摘要",
            "上一轮模型原始输出",
            "优先修复：",
            "[format/invalid_json]",
            "[validation/"
        ]);
    }

    private static bool LooksLikeAnswerForField(string message, AiClarificationQuestion question)
    {
        var text = message.Trim();
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(question.Field))
            return false;

        if (ContainsAny(text, FieldLabels(question.Field)))
            return true;

        if (question.Options.Any(option =>
                !string.IsNullOrWhiteSpace(option) &&
                text.Contains(option, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return question.Field switch
        {
            "scene" => ContainsAny(text, ["外观", "缺陷", "漏装", "有无", "线序", "尺寸", "测量", "分类"]),
            "object_type" => ContainsAny(text, ["包装箱", "纸箱", "箱体", "产品", "金属件", "连接器", "端子", "空调", "内机", "外机", "遥控器", "铜孔", "孔位", "标签"]),
            "defect_type" => ContainsAny(text, ["破损", "裂纹", "划伤", "划痕", "压痕", "凹坑", "脏污", "污渍", "标签异常", "封箱异常", "scratch", "dent", "damage", "broken", "stain"]),
            "measurement_target" => ContainsAny(text, ["孔距", "圆心距", "间距", "距离", "缝隙", "直径", "半径", "角度", "mm", "毫米"]),
            "output_target" => ContainsAny(text, ["PLC", "数据库", "界面", "UI", "屏幕", "文件", "CSV", "Excel"]),
            "model_path" => ContainsAny(text, ["模型", "训练", "YOLO", "onnx", "pt", "路径", "已有模型", "传统视觉"]),
            "roi" => ContainsAny(text, ["ROI", "整图", "固定", "区域", "多ROI", "范围"]),
            "calibration" => ContainsAny(text, ["标定", "像素", "物理", "毫米", "mm", "手眼"]),
            "ambiguous_negative_signal" => text.Length >= 4,
            _ => text.Length >= 2
        };
    }

    private static IReadOnlyList<string> FieldLabels(string field)
    {
        return field switch
        {
            "scene" => ["场景", "场景类型"],
            "object_type" => ["检测对象", "对象", "产品"],
            "defect_type" => ["缺陷类别", "缺陷类型", "缺陷", "瑕疵"],
            "measurement_target" => ["测量目标", "测量项", "尺寸"],
            "output_target" => ["输出目标", "输出", "结果"],
            "model_path" => ["模型资源", "模型", "标签资源"],
            "roi" => ["ROI", "区域", "范围"],
            "calibration" => ["标定", "换算"],
            "ambiguous_negative_signal" => ["歧义", "补充信息"],
            _ => [field]
        };
    }

    private static string MapMissingFactToField(string missingFact)
    {
        if (missingFact.Contains("场景", StringComparison.OrdinalIgnoreCase))
            return "scene";
        if (missingFact.Contains("检测对象", StringComparison.OrdinalIgnoreCase) ||
            missingFact.Contains("对象", StringComparison.OrdinalIgnoreCase))
            return "object_type";
        if (missingFact.Contains("缺陷", StringComparison.OrdinalIgnoreCase))
            return "defect_type";
        if (missingFact.Contains("输出", StringComparison.OrdinalIgnoreCase))
            return "output_target";
        if (missingFact.Contains("模型", StringComparison.OrdinalIgnoreCase) ||
            missingFact.Contains("标签资源", StringComparison.OrdinalIgnoreCase))
            return "model_path";
        if (missingFact.Contains("ROI", StringComparison.OrdinalIgnoreCase))
            return "roi";
        if (missingFact.Contains("标定", StringComparison.OrdinalIgnoreCase) ||
            missingFact.Contains("像素", StringComparison.OrdinalIgnoreCase))
            return "calibration";
        if (missingFact.Contains("测量", StringComparison.OrdinalIgnoreCase) ||
            missingFact.Contains("单位", StringComparison.OrdinalIgnoreCase))
            return "measurement_target";
        if (missingFact.Contains("歧义", StringComparison.OrdinalIgnoreCase))
            return "ambiguous_negative_signal";

        return string.Empty;
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               terms.Any(term => !string.IsNullOrWhiteSpace(term) &&
                                 text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildPriorUserRequirementContext(
        string? sessionSummary,
        string? currentDescription,
        ClarificationHistoryContext clarificationHistory,
        ManualRetryHistoryContext manualRetryHistory)
    {
        if (string.IsNullOrWhiteSpace(sessionSummary))
            return string.Empty;

        if (!ShouldUsePriorRequirementContext(currentDescription, clarificationHistory, manualRetryHistory))
            return string.Empty;

        var current = NormalizeRequirementContextText(currentDescription);
        var userLines = sessionSummary
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("- user:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["- user:".Length..].Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !IsSameRequirementContextLine(line, current))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(3)
            .ToList();

        return userLines.Count == 0
            ? string.Empty
            : "历史用户需求： " + string.Join("；", userLines);
    }

    private static bool IsSameRequirementContextLine(string line, string normalizedCurrent)
    {
        var normalizedLine = NormalizeRequirementContextText(line);
        if (string.Equals(normalizedLine, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalizedLine.EndsWith("...", StringComparison.Ordinal) && normalizedLine.Length > 3)
        {
            var prefix = normalizedLine[..^3].TrimEnd();
            return prefix.Length > 0 &&
                   normalizedCurrent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool ShouldUsePriorRequirementContext(
        string? currentDescription,
        ClarificationHistoryContext clarificationHistory,
        ManualRetryHistoryContext manualRetryHistory)
    {
        if (manualRetryHistory.IsRepairRequest && manualRetryHistory.HasPendingManualRetry)
            return true;

        var current = currentDescription ?? string.Empty;
        if (IsSelfContainedNewRequirement(current))
            return false;

        if (clarificationHistory.PendingQuestions.Count == 0)
            return false;

        return clarificationHistory.PendingQuestions.Any(question => LooksLikeAnswerForField(current, question));
    }

    private static bool LooksLikeNewRequirementTurn(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Trim().Length >= 4 &&
               ContainsAny(text,
               [
                   "生成", "创建", "新建", "构建", "搭建", "做一个", "帮我做", "帮我构建", "帮我搭建",
                   "流程", "检测", "测量", "识别", "判断", "读取", "定位",
                   "new flow", "create", "build", "generate", "measure", "detect", "inspect"
               ]);
    }

    private static bool IsSelfContainedNewRequirement(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Trim().Length >= 8 &&
               ContainsAny(text, ["检测", "测量", "识别", "判断", "读取", "定位", "生成", "创建", "做一个", "做套", "measure", "detect", "inspect"]) &&
               ContainsAny(text, ["包装箱", "纸箱", "箱体", "产品", "金属件", "连接器", "端子", "空调", "内机", "外机", "遥控器", "铜孔", "圆孔", "孔位", "圆形孔", "圆心", "标签", "缺陷", "距离", "间距", "孔距"]);
    }

    private static string NormalizeRequirementContextText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        return normalized;
    }

    private static TemplatePriorityContext BuildSelectedTemplatePriorityContext(
        ScenarioMatchResult selectedMatch,
        IReadOnlyList<ScenarioMatchResult> candidates,
        string generationMode)
    {
        var lockLevel = generationMode == "template_fill" ? "strict" : "relaxed";
        var template = selectedMatch.Template;
        var scenarioKey = !string.IsNullOrWhiteSpace(template?.ScenarioKey)
            ? template!.ScenarioKey!
            : selectedMatch.Scenario.ScenarioKey;
        var scenarioName = !string.IsNullOrWhiteSpace(selectedMatch.Scenario.ScenarioName)
            ? selectedMatch.Scenario.ScenarioName
            : template?.Name ?? string.Empty;

        return new TemplatePriorityContext(
            IsTemplateFirst: true,
            Template: template,
            MatchReason: string.IsNullOrWhiteSpace(selectedMatch.MatchReason)
                ? "User selected template"
                : selectedMatch.MatchReason,
            MatchMode: "user-selected-template",
            Confidence: selectedMatch.Confidence > 0 ? selectedMatch.Confidence : 1,
            MatchedKeywords: selectedMatch.MatchedFields,
            ScenarioKey: scenarioKey,
            ScenarioName: scenarioName,
            GenerationMode: generationMode,
            TemplateLockLevel: lockLevel,
            MatchedFields: selectedMatch.MatchedFields,
            MissingSignals: selectedMatch.MissingSignals,
            PrimaryMatch: selectedMatch,
            Candidates: candidates.Count > 0 ? candidates : new[] { selectedMatch });
    }

    private static bool MatchesTemplateSelection(
        ScenarioMatchResult match,
        AiTemplateSelectionInfo selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.TemplateId))
        {
            var templateId = match.Template?.Id == Guid.Empty ? null : match.Template?.Id.ToString();
            if (!string.IsNullOrWhiteSpace(templateId) &&
                string.Equals(templateId, selection.TemplateId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(match.Scenario.TemplateId) &&
                string.Equals(match.Scenario.TemplateId, selection.TemplateId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(selection.ScenarioKey))
            return false;

        return string.Equals(match.Template?.ScenarioKey, selection.ScenarioKey, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(match.Scenario.ScenarioKey, selection.ScenarioKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTemplateSelectionMode(string? mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "template_fill" or "strict" or "fill" or "force" or "recommended" or "use_template" => "template_fill",
            "template_adapt" or "relaxed" or "adapt" or "reference" => "template_adapt",
            "free_generate" or "free" or "none" or "no_template" or "without_template" => "free_generate",
            _ => string.Empty
        };
    }

    private static bool IsExplicitFreeGenerateRequest(AiFlowGenerationRequest request)
    {
        var text = $"{request.Description} {request.AdditionalContext}".Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("不要用模板", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("不用模板", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("自由生成", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("换一种方案", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("free generate", StringComparison.OrdinalIgnoreCase);
    }

    private static AiRecommendedTemplateInfo? ResolveRecommendedTemplate(
        AiGeneratedFlowJson? generatedFlow,
        TemplatePriorityContext templatePriority)
    {
        var modelTemplate = generatedFlow?.RecommendedTemplate;
        if (modelTemplate != null && !string.IsNullOrWhiteSpace(modelTemplate.TemplateName))
        {
            modelTemplate.MatchMode = string.IsNullOrWhiteSpace(modelTemplate.MatchMode)
                ? "template-first"
                : modelTemplate.MatchMode;
            if (modelTemplate.Confidence <= 0)
                modelTemplate.Confidence = templatePriority.Confidence > 0 ? templatePriority.Confidence : 0.75;
            if (string.IsNullOrWhiteSpace(modelTemplate.MatchReason))
                modelTemplate.MatchReason = templatePriority.MatchReason;
            return modelTemplate;
        }

        if (!templatePriority.IsTemplateFirst)
            return null;

        return new AiRecommendedTemplateInfo
        {
            TemplateId = templatePriority.Template?.Id == Guid.Empty
                ? null
                : templatePriority.Template?.Id.ToString(),
            TemplateName = templatePriority.Template?.Name ?? "端子线序检测",
            TemplateVersion = templatePriority.Template?.TemplateVersion,
            ScenarioKey = templatePriority.ScenarioKey,
            Industry = templatePriority.Template?.Industry,
            MatchReason = templatePriority.MatchReason,
            MatchMode = templatePriority.MatchMode,
            Confidence = templatePriority.Confidence,
            MatchedFields = templatePriority.MatchedFields.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MissingSignals = templatePriority.MissingSignals.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static List<AiPendingParameterInfo> BuildPendingParameters(
        AiGeneratedFlowJson generatedFlow,
        IReadOnlyDictionary<string, string>? actualOperatorIdMap = null)
    {
        var merged = new Dictionary<string, (HashSet<string> Names, string ActualOperatorId)>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in generatedFlow.PendingParameters ?? new List<AiPendingParameterInfo>())
        {
            if (string.IsNullOrWhiteSpace(item.OperatorId))
                continue;

            if (!merged.TryGetValue(item.OperatorId, out var entry))
            {
                entry = (
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    ResolveActualOperatorId(item.OperatorId, item.ActualOperatorId, actualOperatorIdMap));
                merged[item.OperatorId] = entry;
            }
            else if (string.IsNullOrWhiteSpace(entry.ActualOperatorId))
            {
                merged[item.OperatorId] = (
                    entry.Names,
                    ResolveActualOperatorId(item.OperatorId, item.ActualOperatorId, actualOperatorIdMap));
                entry = merged[item.OperatorId];
            }

            foreach (var name in item.ParameterNames ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(name))
                    entry.Names.Add(name);
            }
        }

        foreach (var pair in generatedFlow.ParametersNeedingReview ?? new Dictionary<string, List<string>>())
        {
            if (!merged.TryGetValue(pair.Key, out var entry))
            {
                entry = (
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    ResolveActualOperatorId(pair.Key, null, actualOperatorIdMap));
                merged[pair.Key] = entry;
            }

            foreach (var name in pair.Value)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    entry.Names.Add(name);
            }
        }

        return merged
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => new AiPendingParameterInfo
            {
                OperatorId = item.Key,
                ActualOperatorId = item.Value.ActualOperatorId,
                ParameterNames = item.Value.Names.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .ToList();
    }

    private static string ResolveActualOperatorId(
        string pendingOperatorId,
        string? existingActualOperatorId,
        IReadOnlyDictionary<string, string>? actualOperatorIdMap)
    {
        if (!string.IsNullOrWhiteSpace(existingActualOperatorId))
            return existingActualOperatorId;

        if (actualOperatorIdMap != null &&
            actualOperatorIdMap.TryGetValue(pendingOperatorId, out var actualOperatorId) &&
            !string.IsNullOrWhiteSpace(actualOperatorId))
        {
            return actualOperatorId;
        }

        return string.Empty;
    }

    private static List<AiTemplateCandidateInfo> BuildTemplateCandidates(TemplatePriorityContext templatePriority)
    {
        var candidateMatches = templatePriority.Candidates.Count > 0
            ? templatePriority.Candidates
            : templatePriority.PrimaryMatch == null
                ? Array.Empty<ScenarioMatchResult>()
                : new[] { templatePriority.PrimaryMatch };

        var candidates = new List<AiTemplateCandidateInfo>();
        var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in candidateMatches
                     .Where(static item => item != null)
                     .OrderByDescending(item => item.Confidence))
        {
            var template = match.Template;
            var templateId = template?.Id == Guid.Empty ? null : template?.Id.ToString();
            var scenarioKey = !string.IsNullOrWhiteSpace(template?.ScenarioKey)
                ? template!.ScenarioKey
                : match.Scenario.ScenarioKey;
            var templateName = !string.IsNullOrWhiteSpace(template?.Name)
                ? template!.Name
                : !string.IsNullOrWhiteSpace(match.Scenario.TemplateName)
                    ? match.Scenario.TemplateName
                    : match.Scenario.ScenarioName;
            var dedupKey = $"{templateId}|{scenarioKey}|{templateName}";
            if (!dedup.Add(dedupKey))
                continue;

            candidates.Add(new AiTemplateCandidateInfo
            {
                TemplateId = templateId,
                TemplateName = templateName,
                TemplateVersion = !string.IsNullOrWhiteSpace(template?.TemplateVersion)
                    ? template!.TemplateVersion
                    : match.Scenario.TemplateVersion,
                ScenarioKey = scenarioKey,
                Industry = !string.IsNullOrWhiteSpace(template?.Industry)
                    ? template!.Industry
                    : match.Scenario.Industry,
                Confidence = match.Confidence,
                MatchReason = string.IsNullOrWhiteSpace(match.MatchReason)
                    ? "deterministic scenario match"
                    : match.MatchReason,
                MatchedFields = match.MatchedFields
                    .Where(field => !string.IsNullOrWhiteSpace(field))
                    .Select(field => field.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                MissingSignals = match.MissingSignals
                    .Where(field => !string.IsNullOrWhiteSpace(field))
                    .Select(field => field.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        if (candidates.Count == 0 && templatePriority.Template != null)
        {
            candidates.Add(new AiTemplateCandidateInfo
            {
                TemplateId = templatePriority.Template.Id == Guid.Empty
                    ? null
                    : templatePriority.Template.Id.ToString(),
                TemplateName = templatePriority.Template.Name,
                TemplateVersion = templatePriority.Template.TemplateVersion,
                ScenarioKey = templatePriority.ScenarioKey,
                Industry = templatePriority.Template.Industry,
                Confidence = templatePriority.Confidence,
                MatchReason = templatePriority.MatchReason,
                MatchedFields = templatePriority.MatchedFields
                    .Where(field => !string.IsNullOrWhiteSpace(field))
                    .Select(field => field.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                MissingSignals = templatePriority.MissingSignals
                    .Where(field => !string.IsNullOrWhiteSpace(field))
                    .Select(field => field.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        return candidates;
    }

    private static void ApplyTemplateMetadata(AiGeneratedFlowJson generatedFlow, TemplatePriorityContext templatePriority)
    {
        if (string.IsNullOrWhiteSpace(generatedFlow.GenerationMode))
            generatedFlow.GenerationMode = templatePriority.GenerationMode;
        if (string.IsNullOrWhiteSpace(generatedFlow.TemplateLockLevel))
            generatedFlow.TemplateLockLevel = templatePriority.TemplateLockLevel;

        if (!templatePriority.IsTemplateFirst)
            return;

        generatedFlow.RecommendedTemplate ??= new AiRecommendedTemplateInfo();
        var recommended = generatedFlow.RecommendedTemplate;
        recommended.TemplateId ??= templatePriority.Template?.Id == Guid.Empty
            ? null
            : templatePriority.Template?.Id.ToString();
        if (string.IsNullOrWhiteSpace(recommended.TemplateName))
        {
            recommended.TemplateName = templatePriority.Template?.Name
                ?? templatePriority.ScenarioName
                ?? "模板候选";
        }
        recommended.TemplateVersion ??= templatePriority.Template?.TemplateVersion;
        recommended.ScenarioKey ??= string.IsNullOrWhiteSpace(templatePriority.ScenarioKey)
            ? null
            : templatePriority.ScenarioKey;
        if (string.IsNullOrWhiteSpace(recommended.Industry))
            recommended.Industry = templatePriority.Template?.Industry;
        if (string.IsNullOrWhiteSpace(recommended.MatchReason))
            recommended.MatchReason = templatePriority.MatchReason;
        if (string.IsNullOrWhiteSpace(recommended.MatchMode))
            recommended.MatchMode = templatePriority.MatchMode;
        if (recommended.Confidence <= 0 && templatePriority.Confidence > 0)
            recommended.Confidence = templatePriority.Confidence;

        if (recommended.MatchedFields.Count == 0 && templatePriority.MatchedFields.Count > 0)
        {
            recommended.MatchedFields = templatePriority.MatchedFields
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Select(field => field.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (recommended.MissingSignals.Count == 0 && templatePriority.MissingSignals.Count > 0)
        {
            recommended.MissingSignals = templatePriority.MissingSignals
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Select(field => field.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static void ApplyModelEmbeddedNmsDefaults(AiGeneratedFlowJson generatedFlow)
    {
        if (generatedFlow.Operators.Count == 0)
            return;

        foreach (var deepLearning in generatedFlow.Operators.Where(op => IsOperatorType(op, "DeepLearning")))
        {
            deepLearning.Parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var outputFormat = ReadParameter(deepLearning, "OutputFormat");
            if (string.IsNullOrWhiteSpace(outputFormat) ||
                outputFormat.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                SetParameter(deepLearning, "OutputFormat", "EndToEndNms");
                outputFormat = "EndToEndNms";
            }

            if (IsEndToEndNmsFormat(outputFormat))
                SetParameter(deepLearning, "EnableInternalNms", "true");
        }

        CollapseRedundantBoxNmsAfterModelEmbeddedNms(generatedFlow);
    }

    private static void CollapseRedundantBoxNmsAfterModelEmbeddedNms(AiGeneratedFlowJson generatedFlow)
    {
        var operatorsById = generatedFlow.Operators
            .Where(op => !string.IsNullOrWhiteSpace(op.TempId))
            .ToDictionary(op => op.TempId, StringComparer.OrdinalIgnoreCase);

        var boxNmsOperators = generatedFlow.Operators
            .Where(op => IsOperatorType(op, "BoxNms"))
            .ToList();

        foreach (var boxNms in boxNmsOperators)
        {
            if (!TryResolveModelEmbeddedNmsSource(generatedFlow, operatorsById, boxNms.TempId, out var source))
                continue;

            var sourceConnections = generatedFlow.Connections
                .Where(conn => string.Equals(conn.SourceTempId, boxNms.TempId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (sourceConnections.Any(conn =>
                    IsPort(conn.SourcePortName, "SuppressedCount") ||
                    IsPort(conn.SourcePortName, "SuppressedDetections")))
            {
                continue;
            }

            var replacementConnections = new List<AiGeneratedConnection>();
            foreach (var connection in sourceConnections)
            {
                var replacement = BuildBoxNmsReplacementConnection(connection, source);
                if (replacement != null)
                    replacementConnections.Add(replacement);
            }

            generatedFlow.Operators.Remove(boxNms);
            generatedFlow.Connections = generatedFlow.Connections
                .Where(conn =>
                    !string.Equals(conn.SourceTempId, boxNms.TempId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(conn.TargetTempId, boxNms.TempId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var replacement in replacementConnections)
                AddConnectionIfUseful(generatedFlow.Connections, replacement);

            generatedFlow.ParametersNeedingReview.Remove(boxNms.TempId);
            generatedFlow.PendingParameters.RemoveAll(item =>
                string.Equals(item.OperatorId, boxNms.TempId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool TryResolveModelEmbeddedNmsSource(
        AiGeneratedFlowJson generatedFlow,
        IReadOnlyDictionary<string, AiGeneratedOperator> operatorsById,
        string boxNmsTempId,
        out NmsBypassSource source)
    {
        source = default;
        var detectionsInput = generatedFlow.Connections.FirstOrDefault(conn =>
            string.Equals(conn.TargetTempId, boxNmsTempId, StringComparison.OrdinalIgnoreCase) &&
            IsPort(conn.TargetPortName, "Detections"));

        if (detectionsInput == null)
            return false;

        return TryTraceModelEmbeddedNmsSource(
            generatedFlow,
            operatorsById,
            detectionsInput.SourceTempId,
            detectionsInput.SourcePortName,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            out source);
    }

    private static bool TryTraceModelEmbeddedNmsSource(
        AiGeneratedFlowJson generatedFlow,
        IReadOnlyDictionary<string, AiGeneratedOperator> operatorsById,
        string sourceTempId,
        string sourcePortName,
        ISet<string> visited,
        out NmsBypassSource source)
    {
        source = default;
        if (!visited.Add(sourceTempId) || !operatorsById.TryGetValue(sourceTempId, out var sourceOperator))
            return false;

        if (IsOperatorType(sourceOperator, "DeepLearning"))
        {
            if (!UsesModelEmbeddedNms(sourceOperator))
                return false;

            source = new NmsBypassSource(
                PassthroughTempId: sourceTempId,
                DetectionsPortName: sourcePortName,
                CountPortName: ResolveDeepLearningCountPort(sourceOperator, sourcePortName),
                ImagePortName: "Image",
                DiagnosticsTempId: sourceTempId,
                DiagnosticsPortName: "PostprocessDiagnostics");
            return true;
        }

        if (!IsOperatorType(sourceOperator, "BoxFilter"))
            return false;

        var filterInput = generatedFlow.Connections.FirstOrDefault(conn =>
            string.Equals(conn.TargetTempId, sourceTempId, StringComparison.OrdinalIgnoreCase) &&
            IsPort(conn.TargetPortName, "Detections"));
        if (filterInput == null)
            return false;

        if (!TryTraceModelEmbeddedNmsSource(
                generatedFlow,
                operatorsById,
                filterInput.SourceTempId,
                filterInput.SourcePortName,
                visited,
                out var upstream))
        {
            return false;
        }

        source = upstream with
        {
            PassthroughTempId = sourceTempId,
            DetectionsPortName = sourcePortName,
            CountPortName = "Count",
            ImagePortName = "Image"
        };
        return true;
    }

    private static AiGeneratedConnection? BuildBoxNmsReplacementConnection(
        AiGeneratedConnection oldConnection,
        NmsBypassSource source)
    {
        if (IsPort(oldConnection.SourcePortName, "Detections"))
        {
            return new AiGeneratedConnection
            {
                SourceTempId = source.PassthroughTempId,
                SourcePortName = source.DetectionsPortName,
                TargetTempId = oldConnection.TargetTempId,
                TargetPortName = oldConnection.TargetPortName
            };
        }

        if (IsPort(oldConnection.SourcePortName, "Count") ||
            IsPort(oldConnection.SourcePortName, "InputCount"))
        {
            return new AiGeneratedConnection
            {
                SourceTempId = source.PassthroughTempId,
                SourcePortName = source.CountPortName,
                TargetTempId = oldConnection.TargetTempId,
                TargetPortName = oldConnection.TargetPortName
            };
        }

        if (IsPort(oldConnection.SourcePortName, "Image"))
        {
            return new AiGeneratedConnection
            {
                SourceTempId = source.PassthroughTempId,
                SourcePortName = source.ImagePortName,
                TargetTempId = oldConnection.TargetTempId,
                TargetPortName = oldConnection.TargetPortName
            };
        }

        if (IsPort(oldConnection.SourcePortName, "Diagnostics"))
        {
            return new AiGeneratedConnection
            {
                SourceTempId = source.DiagnosticsTempId,
                SourcePortName = source.DiagnosticsPortName,
                TargetTempId = oldConnection.TargetTempId,
                TargetPortName = oldConnection.TargetPortName
            };
        }

        return null;
    }

    private static void AddConnectionIfUseful(List<AiGeneratedConnection> connections, AiGeneratedConnection connection)
    {
        var targetAlreadyConnected = connections.Any(existing =>
            string.Equals(existing.TargetTempId, connection.TargetTempId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.TargetPortName, connection.TargetPortName, StringComparison.OrdinalIgnoreCase));
        if (targetAlreadyConnected)
            return;

        var duplicate = connections.Any(existing =>
            string.Equals(existing.SourceTempId, connection.SourceTempId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.SourcePortName, connection.SourcePortName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.TargetTempId, connection.TargetTempId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.TargetPortName, connection.TargetPortName, StringComparison.OrdinalIgnoreCase));
        if (!duplicate)
            connections.Add(connection);
    }

    private static bool UsesModelEmbeddedNms(AiGeneratedOperator op)
    {
        var outputFormat = ReadParameter(op, "OutputFormat");
        return string.IsNullOrWhiteSpace(outputFormat) ||
            outputFormat.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
            IsEndToEndNmsFormat(outputFormat);
    }

    private static string ResolveDeepLearningCountPort(AiGeneratedOperator op, string detectionPortName)
    {
        if (IsPort(detectionPortName, "Objects"))
            return "ObjectCount";
        if (IsPort(detectionPortName, "Defects"))
            return "DefectCount";

        var detectionMode = ReadParameter(op, "DetectionMode");
        return string.Equals(detectionMode, "Object", StringComparison.OrdinalIgnoreCase)
            ? "ObjectCount"
            : "DefectCount";
    }

    private static bool IsOperatorType(AiGeneratedOperator op, string operatorType) =>
        string.Equals(op.OperatorType, operatorType, StringComparison.OrdinalIgnoreCase);

    private static bool IsPort(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static string? ReadParameter(AiGeneratedOperator op, string name) =>
        op.Parameters != null && op.Parameters.TryGetValue(name, out var value)
            ? value
            : null;

    private static void SetParameter(AiGeneratedOperator op, string name, string value)
    {
        op.Parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        op.Parameters[name] = value;
    }

    private static bool IsEndToEndNmsFormat(string? outputFormat) =>
        outputFormat is not null &&
        (outputFormat.Equals("EndToEndNms", StringComparison.OrdinalIgnoreCase) ||
         outputFormat.Equals("EndToEnd", StringComparison.OrdinalIgnoreCase) ||
         outputFormat.Equals("OnnxNms", StringComparison.OrdinalIgnoreCase) ||
         outputFormat.Equals("Nms", StringComparison.OrdinalIgnoreCase));

    private readonly record struct NmsBypassSource(
        string PassthroughTempId,
        string DetectionsPortName,
        string CountPortName,
        string ImagePortName,
        string DiagnosticsTempId,
        string DiagnosticsPortName);

    private static void MergeValidationResult(AiValidationResult target, AiValidationResult source)
    {
        foreach (var error in source.Errors)
        {
            if (!target.Errors.Contains(error, StringComparer.Ordinal))
                target.Errors.Add(error);
        }

        foreach (var warning in source.Warnings)
        {
            if (!target.Warnings.Contains(warning, StringComparer.Ordinal))
                target.Warnings.Add(warning);
        }

        foreach (var diagnostic in source.Diagnostics)
        {
            var exists = target.Diagnostics.Any(existing =>
                string.Equals(existing.Severity, diagnostic.Severity, StringComparison.Ordinal) &&
                string.Equals(existing.Category, diagnostic.Category, StringComparison.Ordinal) &&
                string.Equals(existing.Code, diagnostic.Code, StringComparison.Ordinal) &&
                string.Equals(existing.Message, diagnostic.Message, StringComparison.Ordinal) &&
                string.Equals(existing.OperatorId, diagnostic.OperatorId, StringComparison.Ordinal) &&
                string.Equals(existing.ParameterName, diagnostic.ParameterName, StringComparison.Ordinal) &&
                string.Equals(existing.SourceTempId, diagnostic.SourceTempId, StringComparison.Ordinal) &&
                string.Equals(existing.SourcePortName, diagnostic.SourcePortName, StringComparison.Ordinal) &&
                string.Equals(existing.TargetTempId, diagnostic.TargetTempId, StringComparison.Ordinal) &&
                string.Equals(existing.TargetPortName, diagnostic.TargetPortName, StringComparison.Ordinal));
            if (!exists)
                target.Diagnostics.Add(CloneDiagnostic(diagnostic));
        }
    }

    private static List<AiMissingResourceInfo> BuildMissingResources(
        AiGeneratedFlowJson? generatedFlow,
        TemplatePriorityContext templatePriority)
    {
        var resources = new Dictionary<string, AiMissingResourceInfo>(StringComparer.OrdinalIgnoreCase);
        void AddResource(string type, string key, string description)
        {
            var resourceKey = $"{type}|{key}";
            if (resources.ContainsKey(resourceKey))
                return;

            resources[resourceKey] = new AiMissingResourceInfo
            {
                ResourceType = type,
                ResourceKey = key,
                Description = description
            };
        }

        foreach (var item in generatedFlow?.MissingResources ?? new List<AiMissingResourceInfo>())
        {
            if (string.IsNullOrWhiteSpace(item.ResourceType) || string.IsNullOrWhiteSpace(item.ResourceKey))
                continue;

            AddResource(
                item.ResourceType.Trim(),
                item.ResourceKey.Trim(),
                string.IsNullOrWhiteSpace(item.Description) ? "缺少必要资源" : item.Description.Trim());
        }

        foreach (var op in generatedFlow?.Operators ?? new List<AiGeneratedOperator>())
        {
            var parameters = op.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (op.OperatorType.Equals("DeepLearning", StringComparison.OrdinalIgnoreCase))
            {
                if (IsMissingParameter(parameters, "ModelPath", "ModelId"))
                {
                    AddResource("Model", "DeepLearning.ModelPath", "缺少可用模型文件路径或模型标识");
                }
            }

            if (op.OperatorType.Contains("Communication", StringComparison.OrdinalIgnoreCase))
            {
                if (IsMissingParameter(parameters, "IpAddress"))
                {
                    AddResource("PLC", $"{op.OperatorType}.IpAddress", "缺少 PLC 通信地址");
                }

                if (IsMissingParameter(parameters, "Port"))
                {
                    AddResource("PLC", $"{op.OperatorType}.Port", "缺少 PLC 通信端口");
                }
            }
        }

        if (templatePriority.IsTemplateFirst && templatePriority.Template == null)
        {
            AddResource("Template", "WireSequence.Template", "当前未找到可直接复用的线序模板，请先保存模板资产");
        }

        return resources.Values.ToList();
    }

    private static bool IsMissingParameter(IReadOnlyDictionary<string, string> parameters, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!parameters.TryGetValue(key, out var value))
                continue;

            if (string.IsNullOrWhiteSpace(value))
                continue;

            var normalized = value.Trim();
            if (!normalized.Equals("todo", StringComparison.OrdinalIgnoreCase)
                && !normalized.Equals("tbd", StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains("your_", StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains("to_be_filled", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record TemplatePriorityContext(
        bool IsTemplateFirst,
        FlowTemplate? Template,
        string MatchReason,
        string MatchMode,
        double Confidence,
        IReadOnlyList<string> MatchedKeywords,
        string ScenarioKey,
        string ScenarioName,
        string GenerationMode,
        string TemplateLockLevel,
        IReadOnlyList<string> MatchedFields,
        IReadOnlyList<string> MissingSignals,
        ScenarioMatchResult? PrimaryMatch,
        IReadOnlyList<ScenarioMatchResult> Candidates)
    {
        public static TemplatePriorityContext None { get; } =
            new(
                false,
                null,
                string.Empty,
                string.Empty,
                0,
                Array.Empty<string>(),
                string.Empty,
                string.Empty,
                "free_generate",
                "none",
                Array.Empty<string>(),
                Array.Empty<string>(),
                null,
                Array.Empty<ScenarioMatchResult>());
    }

    private sealed record AgentFinalRepairResult(
        bool Success,
        VisionAgentLoopResult? AgentResult,
        AiGeneratedFlowJson? GeneratedFlow,
        AiValidationResult? Validation,
        List<AiAttemptDiagnostic> Diagnostics,
        List<VisionAgentToolTrace> ToolTrace,
        List<VisionAgentPendingAction> PendingActions,
        int RepairAttempts)
    {
        public static AgentFinalRepairResult Repaired(
            VisionAgentLoopResult agentResult,
            AiGeneratedFlowJson generatedFlow,
            AiValidationResult validation,
            List<AiAttemptDiagnostic> diagnostics,
            List<VisionAgentToolTrace> toolTrace,
            List<VisionAgentPendingAction> pendingActions,
            int repairAttempts) =>
            new(true, agentResult, generatedFlow, validation, diagnostics, toolTrace, pendingActions, repairAttempts);

        public static AgentFinalRepairResult Failed(
            VisionAgentLoopResult agentResult,
            AiValidationResult validation,
            List<AiAttemptDiagnostic> diagnostics,
            List<VisionAgentToolTrace> toolTrace,
            List<VisionAgentPendingAction> pendingActions,
            int repairAttempts) =>
            new(false, agentResult, null, validation, diagnostics, toolTrace, pendingActions, repairAttempts);
    }

    private sealed record ClarificationHistoryContext(
        int ClarificationRounds,
        IReadOnlySet<string> AnsweredFields,
        IReadOnlyList<AiClarificationQuestion> PendingQuestions,
        string PendingTurnIntent)
    {
        public static ClarificationHistoryContext Empty { get; } =
            new(0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), Array.Empty<AiClarificationQuestion>(), string.Empty);
    }

    private sealed record ManualRetryHistoryContext(
        bool IsRepairRequest,
        bool HasPendingManualRetry);

    private string BuildRetryMessage(string originalMessage, AiValidationResult failedValidation, string? lastRawResponse)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Regenerate the workflow JSON using the same user request, but repair the previous attempt precisely instead of starting blindly.");
        sb.AppendLine();
        sb.AppendLine("Original request:");
        sb.AppendLine(originalMessage);
        sb.AppendLine();

        var repairTargets = BuildRepairTargets(failedValidation);
        if (repairTargets.Count > 0)
        {
            sb.AppendLine("Repair priorities:");
            foreach (var target in repairTargets.Take(4))
            {
                sb.AppendLine($"- {target}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Structured issues found in the previous attempt:");
        foreach (var issue in failedValidation.Diagnostics.Take(10))
        {
            var fieldText = issue.RelatedFields.Count > 0
                ? $" | fields: {string.Join(", ", issue.RelatedFields)}"
                : string.Empty;
            var repairHint = string.IsNullOrWhiteSpace(issue.RepairHint)
                ? string.Empty
                : $" | action: {issue.RepairHint}";
            sb.AppendLine($"- [{issue.Severity}/{issue.Category}/{issue.Code}] {issue.Message}{fieldText}{repairHint}");
        }

        if (failedValidation.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Warnings to improve if possible:");
            foreach (var warning in failedValidation.Warnings)
                sb.AppendLine($"- {warning}");
        }

        if (!string.IsNullOrWhiteSpace(lastRawResponse))
        {
            sb.AppendLine();
            sb.AppendLine("Previous assistant output summary:");
            sb.AppendLine(_responseParser.Summarize(lastRawResponse));
            sb.AppendLine();
            sb.AppendLine("Previous assistant output to fix:");
            sb.AppendLine("```json");
            sb.AppendLine(TrimRetryOutput(lastRawResponse));
            sb.AppendLine("```");
        }

        sb.AppendLine();
        sb.AppendLine("Keep already valid operators, connections, and parameters unchanged where possible.");
        sb.AppendLine("Return a complete corrected JSON object only. Do not add explanations or markdown outside the JSON.");
        sb.AppendLine("Output contract:");
        sb.AppendLine("- The first character must be { and the last character must be }.");
        sb.AppendLine("- Top-level shape: explanation string, operators array, connections array, optional parametersNeedingReview object.");
        sb.AppendLine("- Do not wrap the workflow in workflow/flow/result/data/answer.");
        sb.AppendLine("- Do not return the JSON as an escaped string.");
        sb.AppendLine("- Do not use nodes/edges/steps/modules as final top-level aliases.");

        return sb.ToString();
    }

    private string BuildAssistantReply(
        AiGeneratedFlowJson generatedFlow,
        OperatorFlowDto flowDto,
        AiRecommendedTemplateInfo? recommendedTemplate)
    {
        if (!string.IsNullOrWhiteSpace(generatedFlow.Explanation))
        {
            return generatedFlow.Explanation.Trim();
        }

        var operatorCount = flowDto.Operators?.Count ?? 0;
        var connectionCount = flowDto.Connections?.Count ?? 0;
        if (!string.IsNullOrWhiteSpace(recommendedTemplate?.TemplateName))
        {
            return $"工程方案已生成，包含 {operatorCount} 个算子、{connectionCount} 条连线，并优先沿用了模板「{recommendedTemplate.TemplateName}」。";
        }

        return $"工程方案已生成，包含 {operatorCount} 个算子、{connectionCount} 条连线。";
    }

    private AiFlowGenerationResult CreateClarificationResult(
        string sessionId,
        AiRequirementBrief requirementBrief,
        TemplatePriorityContext templatePriority,
        string detectedIntent,
        AiTurnRoute turnRoute,
        ClarificationHistoryContext clarificationHistory,
        IReadOnlyList<string> progressMessages,
        object? promptTrace,
        IReadOnlyList<AiGenerationStageDiagnostic> stageTimeline)
    {
        var summary = BuildClarificationSummary(requirementBrief);
        var recommendedTemplate = ResolveRecommendedTemplate(null, templatePriority);
        var missingResources = BuildMissingResources(null, templatePriority);
        var assistantPayload = new ConversationTurnPayload
        {
            Kind = "assistant_clarification",
            Status = AiFlowGenerationResult.CompletionStatusClarificationRequired,
            InteractionState = AiInteractionStates.Clarifying,
            TurnIntent = ResolveClarificationPayloadIntent(turnRoute, clarificationHistory),
            RouterConfidence = turnRoute.Confidence,
            BlockingClarificationFields = requirementBrief.BlockingClarificationFields.ToList(),
            NonBlockingMissingFields = requirementBrief.NonBlockingMissingFields.ToList(),
            ClarificationRound = clarificationHistory.ClarificationRounds + 1,
            AskedQuestionFingerprints = requirementBrief.ClarificationQuestions
                .Select(BuildQuestionFingerprint)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            AnsweredClarificationFields = clarificationHistory.AnsweredFields.ToList(),
            Reply = summary,
            Progress = progressMessages.Where(item => !string.IsNullOrWhiteSpace(item)).ToList(),
            ClarificationRequired = true,
            RequirementBrief = requirementBrief
        };

        _conversationalFlowService.RecordAssistantResponse(
            sessionId,
            summary,
            null,
            payload: assistantPayload);

        return new AiFlowGenerationResult
        {
            Success = false,
            ErrorMessage = summary,
            AiExplanation = summary,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusClarificationRequired,
            FailureType = AiFlowGenerationResult.FailureTypeClarificationRequired,
            ClarificationRequired = true,
            RequirementBrief = requirementBrief,
            SessionId = sessionId,
            DetectedIntent = detectedIntent,
            RecommendedTemplate = recommendedTemplate,
            GenerationMode = templatePriority.GenerationMode,
            TemplateLockLevel = templatePriority.TemplateLockLevel,
            PendingParameters = [],
            MissingResources = missingResources,
            PromptTrace = promptTrace,
            TemplateCandidates = BuildTemplateCandidates(templatePriority),
            StageTimeline = stageTimeline.ToList(),
            TurnIntent = turnRoute.TurnIntent,
            InteractionState = AiInteractionStates.Clarifying,
            RouterConfidence = turnRoute.Confidence,
            BlockingClarificationFields = requirementBrief.BlockingClarificationFields.ToList(),
            NonBlockingMissingFields = requirementBrief.NonBlockingMissingFields.ToList()
        };
    }

    private static string BuildClarificationSummary(AiRequirementBrief requirementBrief)
    {
        var questionCount = requirementBrief.ClarificationQuestions.Count > 0
            ? requirementBrief.ClarificationQuestions.Count
            : Math.Max(requirementBrief.MissingFacts.Count, 1);
        var sb = new StringBuilder();
        sb.Append($"当前需求还需要澄清 {questionCount} 项关键信息。");

        if (!string.IsNullOrWhiteSpace(requirementBrief.ScenarioName))
        {
            sb.Append($" 已识别场景：{requirementBrief.ScenarioName}。");
        }

        if (requirementBrief.MissingFacts.Count > 0)
        {
            sb.Append($" 主要缺口：{string.Join("；", requirementBrief.MissingFacts.Take(3))}。");
        }

        if (requirementBrief.CanGenerateDraftNow)
        {
            sb.Append(" 若希望先看草稿，可切换到草稿模式。");
        }
        else
        {
            sb.Append(" 请先补齐关键字段后再继续。");
        }

        return sb.ToString();
    }

    private static string ResolveClarificationPayloadIntent(
        AiTurnRoute turnRoute,
        ClarificationHistoryContext clarificationHistory)
    {
        return string.Equals(turnRoute.TurnIntent, AiTurnIntents.ClarificationAnswer, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(clarificationHistory.PendingTurnIntent)
            ? clarificationHistory.PendingTurnIntent
            : turnRoute.TurnIntent;
    }

    private static string BuildQuestionFingerprint(AiClarificationQuestion question)
    {
        if (string.IsNullOrWhiteSpace(question.Field))
            return string.Empty;

        var questionText = string.IsNullOrWhiteSpace(question.Question)
            ? string.Empty
            : question.Question.Trim();
        return $"{question.Field.Trim().ToLowerInvariant()}:{questionText}";
    }

    private AiFlowGenerationResult CreateManualRetryResult(
        string stage,
        string sessionId,
        string originalMessage,
        AiValidationResult validation,
        List<AiAttemptDiagnostic> diagnostics,
        int retryCount,
        string? lastRawResponse,
        object? promptTrace,
        IReadOnlyList<string> progressMessages,
        AiRequirementBrief? requirementBrief,
        AiTurnRoute turnRoute,
        string generationMode,
        string templateLockLevel,
        List<AiTemplateCandidateInfo> templateCandidates,
        IReadOnlyList<AiGenerationStageDiagnostic> stageTimeline)
    {
        var failureSummary = BuildFailureSummary(
            validation,
            retryCount: retryCount,
            message: BuildManualRetrySummary(stage, validation),
            lastRawResponse: lastRawResponse,
            fallbackCode: stage == "parse" ? "invalid_json" : "validation_failed",
            fallbackCategory: stage);
        var manualRetry = new AiManualRetryInfo
        {
            Required = true,
            Stage = stage,
            Draft = BuildManualRetryDraft(originalMessage, validation, lastRawResponse, requirementBrief),
            Summary = diagnostics.FirstOrDefault()?.Summary ?? BuildAttemptSummary(validation),
            RepairTarget = failureSummary.RepairTarget,
            LastOutputSummary = failureSummary.LastOutputSummary,
            Diagnostics = diagnostics.Select(CloneAttemptDiagnostic).ToList()
        };
        var persistedMessage = $"本轮生成未通过{(stage == "parse" ? "JSON 解析" : "结构校验")}，已生成纠错草稿，请确认后手动发送。";
        RecordFailureResponse(
            sessionId,
            persistedMessage,
            lastRawResponse,
            BuildFailureTurnPayload(
                status: AiFlowGenerationResult.FailureTypeManualRetryRequired,
                summaryText: failureSummary.Message,
                failureSummary: failureSummary,
                diagnostics: diagnostics,
                progressMessages: progressMessages,
                manualRetry: manualRetry,
                requirementBrief: requirementBrief,
                turnRoute: turnRoute));

        return new AiFlowGenerationResult
        {
            Success = false,
            ErrorMessage = failureSummary.Message,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
            FailureType = AiFlowGenerationResult.FailureTypeManualRetryRequired,
            FailureSummary = failureSummary,
            LastAttemptDiagnostics = diagnostics,
            ManualRetry = manualRetry,
            RetryCount = retryCount,
            SessionId = sessionId,
            PromptTrace = promptTrace,
            RequirementBrief = requirementBrief,
            GenerationMode = generationMode,
            TemplateLockLevel = templateLockLevel,
            TemplateCandidates = templateCandidates,
            StageTimeline = stageTimeline.ToList(),
            TurnIntent = AiTurnIntents.ManualRetryRepair,
            InteractionState = AiInteractionStates.ManualRetry,
            RouterConfidence = turnRoute.Confidence,
            BlockingClarificationFields = requirementBrief?.BlockingClarificationFields.ToList() ?? new List<string>(),
            NonBlockingMissingFields = requirementBrief?.NonBlockingMissingFields.ToList() ?? new List<string>()
        };
    }

    private static string BuildManualRetrySummary(string stage, AiValidationResult validation)
    {
        var label = stage == "parse" ? "JSON 解析" : "结构校验";
        if (validation.PrimaryError != null)
        {
            return $"AI 输出未通过{label}：[{validation.PrimaryError.Category}/{validation.PrimaryError.Code}] {validation.PrimaryError.Message}";
        }

        return $"AI 输出未通过{label}，请根据诊断信息修正后重试。";
    }

    private string BuildManualRetryDraft(
        string originalMessage,
        AiValidationResult validation,
        string? lastRawResponse,
        AiRequirementBrief? requirementBrief)
    {
        var sb = new StringBuilder();
        sb.AppendLine("请基于上一轮需求继续修正工作流 JSON，不要重建无关结构。");
        sb.AppendLine("请只返回一个完整且可解析的 JSON 对象，不要附加 markdown、解释文本或代码块标记。");
        sb.AppendLine();
        sb.AppendLine("本轮需求原话：");
        sb.AppendLine(originalMessage.Trim());

        var requirementContext = BuildRequirementRepairContext(requirementBrief);
        if (!string.IsNullOrWhiteSpace(requirementContext))
        {
            sb.AppendLine();
            sb.AppendLine("上一轮已确认的需求上下文：");
            sb.AppendLine(requirementContext);
        }

        var repairTargets = BuildRepairTargets(validation);
        if (repairTargets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("优先修复：");
            foreach (var target in repairTargets.Take(4))
            {
                sb.AppendLine($"- {target}");
            }
        }

        if (validation.Diagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("诊断信息：");
            foreach (var issue in validation.Diagnostics.Take(8))
            {
                var fieldText = issue.RelatedFields.Count > 0
                    ? $"（字段：{string.Join("、", issue.RelatedFields)}）"
                    : string.Empty;
                var repairHint = string.IsNullOrWhiteSpace(issue.RepairHint)
                    ? string.Empty
                    : $"；修复建议：{issue.RepairHint}";
                sb.AppendLine($"- [{issue.Category}/{issue.Code}] {issue.Message}{fieldText}{repairHint}");
            }
        }

        if (!string.IsNullOrWhiteSpace(lastRawResponse))
        {
            sb.AppendLine();
            sb.AppendLine("上一轮输出摘要：");
            sb.AppendLine(_responseParser.Summarize(lastRawResponse));
            sb.AppendLine();
            sb.AppendLine("上一轮模型原始输出（可能不是合法 JSON，请在此基础上修复）：");
            sb.AppendLine("---BEGIN PREVIOUS OUTPUT---");
            sb.AppendLine(TrimRetryOutput(lastRawResponse));
            sb.AppendLine("---END PREVIOUS OUTPUT---");
        }

        sb.AppendLine("Output contract: return only the exact workflow JSON object; first char {, last char }; top-level operators/connections must be arrays; no workflow/flow/result/data wrapper; no escaped JSON string.");
        sb.AppendLine();
        sb.AppendLine("请尽量保留已经正确的算子、连线和参数，仅修正本轮报错涉及的部分。");
        return sb.ToString().Trim();
    }

    private static string BuildRequirementRepairContext(AiRequirementBrief? requirementBrief)
    {
        if (requirementBrief == null)
            return string.Empty;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(requirementBrief.ScenarioName))
            lines.Add($"场景：{requirementBrief.ScenarioName}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.ScenarioKey))
            lines.Add($"场景Key：{requirementBrief.ScenarioKey}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.IntentType))
            lines.Add($"意图：{requirementBrief.IntentType}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.ObjectName))
            lines.Add($"检测对象：{requirementBrief.ObjectName}");
        if (requirementBrief.ObjectTypes.Count > 0)
            lines.Add($"对象类型：{string.Join("、", requirementBrief.ObjectTypes.Take(6))}");
        if (requirementBrief.MeasurementTargets.Count > 0)
            lines.Add($"测量目标：{string.Join("、", requirementBrief.MeasurementTargets.Take(6))}");
        if (requirementBrief.DefectTypes.Count > 0)
            lines.Add($"缺陷类别：{string.Join("、", requirementBrief.DefectTypes.Take(6))}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.OutputTarget))
            lines.Add($"输出目标：{requirementBrief.OutputTarget}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.DecisionRule))
            lines.Add($"判定逻辑：{requirementBrief.DecisionRule}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.RoiRequirement))
            lines.Add($"ROI：{requirementBrief.RoiRequirement}");
        if (!string.IsNullOrWhiteSpace(requirementBrief.CalibrationRequirement))
            lines.Add($"标定：{requirementBrief.CalibrationRequirement}");
        foreach (var fact in requirementBrief.KnownFacts.Take(8))
        {
            if (!string.IsNullOrWhiteSpace(fact))
                lines.Add(fact);
        }

        return string.Join(
            Environment.NewLine,
            lines
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildFinalValidationErrorMessage(AiValidationResult? validation, int retryCount)
    {
        if (validation?.PrimaryError != null)
        {
            return $"AI generated workflow did not pass validation (retried {retryCount} times): " +
                   $"[{validation.PrimaryError.Category}/{validation.PrimaryError.Code}] {validation.PrimaryError.Message}";
        }

        return $"AI generated workflow did not pass validation (retried {retryCount} times): " +
               string.Join("; ", validation?.Errors ?? new List<string>());
    }

    private static AiValidationResult BuildParseValidationResult(AiFlowParseResult parseResult)
    {
        var validation = new AiValidationResult();
        validation.AddError(
            string.IsNullOrWhiteSpace(parseResult.Message)
                ? "AI 返回的内容无法解析为工作流 JSON"
                : parseResult.Message,
            code: string.IsNullOrWhiteSpace(parseResult.Code) ? "invalid_json" : parseResult.Code,
            category: string.IsNullOrWhiteSpace(parseResult.Category) ? "format" : parseResult.Category,
            relatedFields: ["response.content"],
            repairHint: string.IsNullOrWhiteSpace(parseResult.RepairHint)
                ? "请只返回一个完整 JSON 对象，不要附加 markdown、解释文本或多余前后缀。"
                : parseResult.RepairHint);
        return validation;
    }

    private List<AiAttemptDiagnostic> BuildAttemptDiagnostics(
        int attemptNumber,
        string stage,
        AiValidationResult validation,
        string? lastRawResponse)
    {
        return
        [
            new AiAttemptDiagnostic
            {
                AttemptNumber = attemptNumber,
                Stage = stage,
                Summary = BuildAttemptSummary(validation),
                OutputSummary = _responseParser.Summarize(lastRawResponse),
                Issues = validation.Diagnostics.Select(CloneDiagnostic).ToList()
            }
        ];
    }

    private static List<AiValidationDiagnostic>? ExtractKnowledgeDiagnostics(AiValidationResult? validation)
    {
        if (validation == null)
            return null;
        var knowledge = validation.Diagnostics
            .Where(d => string.Equals(d.Category, "knowledge", StringComparison.OrdinalIgnoreCase))
            .Select(CloneDiagnostic)
            .ToList();
        return knowledge.Count > 0 ? knowledge : null;
    }

    private static string BuildAttemptSummary(AiValidationResult validation)
    {
        var errorCount = validation.Diagnostics.Count(item => item.Severity == AiValidationSeverity.Error);
        var warningCount = validation.Diagnostics.Count(item => item.Severity == AiValidationSeverity.Warning);
        if (validation.PrimaryError != null)
        {
            return $"主失败点：[{validation.PrimaryError.Category}/{validation.PrimaryError.Code}] " +
                   $"{validation.PrimaryError.Message}（errors={errorCount}, warnings={warningCount}）";
        }

        if (warningCount > 0)
            return $"本轮无阻断性错误，但有 {warningCount} 条警告需要关注。";

        return "本轮未记录结构化诊断。";
    }

    private AiFailureSummary BuildFailureSummary(
        AiValidationResult? validation,
        int retryCount,
        string message,
        string? lastRawResponse,
        string fallbackCode,
        string fallbackCategory)
    {
        var primary = validation?.PrimaryError;
        return new AiFailureSummary
        {
            Category = primary?.Category ?? fallbackCategory,
            Code = primary?.Code ?? fallbackCode,
            Message = message,
            RepairTarget = BuildRepairTargets(validation).FirstOrDefault()
                ?? "根据最近一次诊断修复工作流 JSON 后重试。",
            RetryCount = retryCount,
            LastOutputSummary = _responseParser.Summarize(lastRawResponse)
        };
    }

    private static List<string> BuildRepairTargets(AiValidationResult? validation)
    {
        if (validation == null)
            return new List<string>();

        return validation.Diagnostics
            .Where(item => item.Severity == AiValidationSeverity.Error)
            .Select(item => string.IsNullOrWhiteSpace(item.RepairHint) ? item.Message : item.RepairHint!)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string TrimRetryOutput(string rawResponse)
    {
        const int maxLength = 6000;
        if (string.IsNullOrWhiteSpace(rawResponse))
            return string.Empty;

        var trimmed = rawResponse.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength] + "\n...<truncated>";
    }

    private static string TrimTemplateFlowJson(string flowJson)
    {
        const int maxLength = 8000;
        if (string.IsNullOrWhiteSpace(flowJson))
            return string.Empty;

        var trimmed = flowJson.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength] + "\n...<truncated>";
    }

    private static ConversationTurnPayload BuildFailureTurnPayload(
        string status,
        string summaryText,
        AiFailureSummary failureSummary,
        IReadOnlyCollection<AiAttemptDiagnostic> diagnostics,
        IReadOnlyList<string> progressMessages,
        AiManualRetryInfo? manualRetry = null,
        AiRequirementBrief? requirementBrief = null,
        AiTurnRoute? turnRoute = null)
    {
        return new ConversationTurnPayload
        {
            Kind = "assistant_failure",
            Status = status,
            InteractionState = manualRetry?.Required == true
                ? AiInteractionStates.ManualRetry
                : AiInteractionStates.Failed,
            TurnIntent = manualRetry?.Required == true
                ? AiTurnIntents.ManualRetryRepair
                : turnRoute?.TurnIntent ?? AiTurnIntents.Unknown,
            RouterConfidence = turnRoute?.Confidence ?? string.Empty,
            BlockingClarificationFields = requirementBrief?.BlockingClarificationFields.ToList() ?? new List<string>(),
            NonBlockingMissingFields = requirementBrief?.NonBlockingMissingFields.ToList() ?? new List<string>(),
            Progress = progressMessages.ToList(),
            ClarificationRequired = false,
            RequirementBrief = requirementBrief,
            Failure = new ConversationTurnFailurePayload
            {
                Summary = summaryText,
                FailureSummary = new AiFailureSummary
                {
                    Category = failureSummary.Category,
                    Code = failureSummary.Code,
                    Message = failureSummary.Message,
                    RepairTarget = failureSummary.RepairTarget,
                    RetryCount = failureSummary.RetryCount,
                    LastOutputSummary = failureSummary.LastOutputSummary
                },
                Diagnostics = diagnostics.Select(CloneAttemptDiagnostic).ToList()
            },
            ManualRetry = manualRetry == null
                ? null
                : new AiManualRetryInfo
                {
                    Required = manualRetry.Required,
                    Stage = manualRetry.Stage,
                    Draft = manualRetry.Draft,
                    Summary = manualRetry.Summary,
                    RepairTarget = manualRetry.RepairTarget,
                    LastOutputSummary = manualRetry.LastOutputSummary,
                    Diagnostics = manualRetry.Diagnostics.Select(CloneAttemptDiagnostic).ToList()
                }
        };
    }

    private void RecordFailureResponse(
        string sessionId,
        string errorMessage,
        string? lastRawResponse,
        ConversationTurnPayload? payload = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(errorMessage))
            return;

        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"上次生成失败：{errorMessage}");

        if (!string.IsNullOrWhiteSpace(lastRawResponse))
        {
            summary.AppendLine();
            summary.AppendLine("最近一次模型输出片段：");
            summary.AppendLine(TrimRetryOutput(lastRawResponse));
        }

        _conversationalFlowService.RecordAssistantResponse(
            sessionId,
            summary.ToString().Trim(),
            null,
            payload: payload);
    }

    private static AiValidationDiagnostic CloneDiagnostic(AiValidationDiagnostic source)
    {
        return new AiValidationDiagnostic
        {
            Severity = source.Severity,
            Code = source.Code,
            Category = source.Category,
            Message = source.Message,
            RelatedFields = source.RelatedFields.ToList(),
            OperatorId = source.OperatorId,
            ParameterName = source.ParameterName,
            SourceTempId = source.SourceTempId,
            SourcePortName = source.SourcePortName,
            TargetTempId = source.TargetTempId,
            TargetPortName = source.TargetPortName,
            RepairHint = source.RepairHint
        };
    }

    private static AiAttemptDiagnostic CloneAttemptDiagnostic(AiAttemptDiagnostic source)
    {
        return new AiAttemptDiagnostic
        {
            AttemptNumber = source.AttemptNumber,
            Stage = source.Stage,
            Summary = source.Summary,
            OutputSummary = source.OutputSummary,
            Issues = source.Issues?.Select(CloneDiagnostic).ToList() ?? new List<AiValidationDiagnostic>()
        };
    }

    private (OperatorFlowDto Flow, Dictionary<string, string> ActualOperatorIdMap) ConvertToFlowDto(
        AiGeneratedFlowJson generated,
        string userDescription)
    {
        // Map tempId to generated operator ID and metadata.
        var opInfoMapping = new Dictionary<string, (Guid Id, OperatorMetadata Meta)>();

        // tempId 鈫?(InputPorts: Name->Guid, OutputPorts: Name->Guid)
        var portMapping = new Dictionary<string, (Dictionary<string, Guid> Inputs, Dictionary<string, Guid> Outputs)>();

        foreach (var op in generated.Operators)
        {
            var type = Enum.Parse<OperatorType>(op.OperatorType);
            var metadata = _operatorFactory.GetMetadata(type) ?? throw new InvalidOperationException($"Operator {type} is not registered.");
            var operatorId = Guid.NewGuid();
            opInfoMapping[op.TempId] = (operatorId, metadata);

            var inputPorts = new Dictionary<string, Guid>();
            foreach (var p in metadata.InputPorts)
                inputPorts[p.Name] = Guid.NewGuid();

            var outputPorts = new Dictionary<string, Guid>();
            foreach (var p in metadata.OutputPorts)
                outputPorts[p.Name] = Guid.NewGuid();

            portMapping[op.TempId] = (inputPorts, outputPorts);
        }

        var operators = generated.Operators.Select(op =>
        {
            var (operatorId, metadata) = opInfoMapping[op.TempId];
            var (inputs, outputs) = portMapping[op.TempId];

            return new OperatorDto
            {
                Id = operatorId,
                Name = op.DisplayName,
                Type = metadata.Type,
                X = 0, // 由 AutoLayoutService 填充
                Y = 0,
                IsEnabled = true,
                InputPorts = metadata.InputPorts.Select(p => new PortDto
                {
                    Id = inputs[p.Name],
                    Name = p.Name,
                    Direction = PortDirection.Input,
                    DataType = p.DataType,
                    IsRequired = p.IsRequired
                }).ToList(),
                OutputPorts = metadata.OutputPorts.Select(p => new PortDto
                {
                    Id = outputs[p.Name],
                    Name = p.Name,
                    Direction = PortDirection.Output,
                    DataType = p.DataType
                }).ToList(),
                Parameters = metadata.Parameters.Select(p => new ParameterDto
                {
                    Id = Guid.NewGuid(),
                    Name = p.Name,
                    DisplayName = p.DisplayName,
                    Description = p.Description,
                    DataType = p.DataType,
                    DefaultValue = p.DefaultValue,
                    IsRequired = p.IsRequired,
                    Options = p.Options?.Select(opt => new ClearVision.Product.Core.ValueObjects.ParameterOption
                    {
                        Label = opt.Label,
                        Value = opt.Value
                    }).ToList(),
                    Value = op.Parameters.TryGetValue(p.Name, out var val) ? val : null
                }).ToList()
            };
        }).ToList();

        var connections = generated.Connections?.Select(conn =>
        {
            // 源端口必须从 OutputPorts 查找
            var outputs = portMapping[conn.SourceTempId].Outputs;
            if (!outputs.TryGetValue(conn.SourcePortName, out var srcPortId))
            {
                throw new InvalidOperationException(
                   $"源算子 {conn.SourceTempId} 不存在输出端口 '{conn.SourcePortName}'");
            }

            // 目标端口必须从 InputPorts 查找
            var inputs = portMapping[conn.TargetTempId].Inputs;
            if (!inputs.TryGetValue(conn.TargetPortName, out var tgtPortId))
            {
                throw new InvalidOperationException(
                    $"目标算子 {conn.TargetTempId} 不存在输入端口 '{conn.TargetPortName}'");
            }

            return new OperatorConnectionDto
            {
                Id = Guid.NewGuid(),
                SourceOperatorId = opInfoMapping[conn.SourceTempId].Id,
                SourcePortId = srcPortId,
                TargetOperatorId = opInfoMapping[conn.TargetTempId].Id,
                TargetPortId = tgtPortId
            };
        }).ToList() ?? new List<OperatorConnectionDto>();

        return (
            new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = $"AI生成 - {userDescription}",
                Operators = operators,
                Connections = connections
            },
            opInfoMapping.ToDictionary(
                item => item.Key,
                item => item.Value.Id.ToString(),
                StringComparer.OrdinalIgnoreCase));
    }

    private OperatorFlow ConvertDtoToEntity(OperatorFlowDto dto)
    {
        // 简单转换为用于测试跑分的内部结构
        var flow = new OperatorFlow(dto.Name);
        typeof(OperatorFlow).GetProperty("Id")?.SetValue(flow, dto.Id);

        flow.Operators = dto.Operators.Select(o =>
        {
            var op = _operatorFactory.CreateOperator(o.Type, o.Name, o.X, o.Y);
            typeof(Operator).GetProperty("Id")?.SetValue(op, o.Id);

            // 简单复制核心参数
            foreach (var pDto in o.Parameters)
            {
                var targetParam = op.Parameters.FirstOrDefault(p => p.Name == pDto.Name);
                if (targetParam != null && pDto.Value != null)
                    targetParam.SetValue(pDto.Value);
            }
            return op;
        }).ToList();

        flow.Connections = dto.Connections.Select(c =>
        {
            var conn = new ClearVision.Product.Core.ValueObjects.OperatorConnection(c.SourceOperatorId, c.SourcePortId, c.TargetOperatorId, c.TargetPortId);
            typeof(ClearVision.Product.Core.ValueObjects.OperatorConnection).GetProperty("Id")?.SetValue(conn, c.Id);
            return conn;
        }).ToList();

        return flow;
    }

    private static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var cjkCount = 0;
        foreach (var ch in text)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF ||   // CJK Unified Ideographs
                ch >= 0x3400 && ch <= 0x4DBF ||   // CJK Extension A
                ch >= 0x3000 && ch <= 0x303F ||   // CJK Symbols and Punctuation
                ch >= 0xFF00 && ch <= 0xFFEF)     // Fullwidth Forms
            {
                cjkCount++;
            }
        }
        var nonCjkCount = text.Length - cjkCount;
        // CJK chars ~1.5 tokens each; non-CJK ~0.25 tokens each
        return (int)(cjkCount * 1.5 + nonCjkCount * 0.25);
    }
}


