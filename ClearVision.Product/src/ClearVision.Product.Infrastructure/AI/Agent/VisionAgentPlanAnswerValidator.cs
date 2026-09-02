using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed record VisionAgentTaskTypeNormalizationAudit(
    string Source,
    string RawValue,
    string CanonicalValue,
    bool ExplicitUserChoice);

public sealed record VisionAgentPlanAnswerValidationResult(
    List<VisionAgentPlanAnswer> AcceptedAnswers,
    Dictionary<string, string> RequirementAnswers,
    Dictionary<string, string> BuildDecisions,
    Dictionary<string, string> ParameterSelections,
    List<string> ResolvedFields,
    List<string> InvalidQuestionIds,
    List<string> InvalidValues,
    List<string> ConflictedFields,
    string AnswerSetFingerprint,
    List<string> Warnings)
{
    public List<string> DeferredFields { get; init; } = [];
    public string CanonicalTaskType { get; init; } = string.Empty;
    public List<VisionAgentTaskTypeNormalizationAudit> TaskTypeNormalizationAudit { get; init; } = [];
}

public sealed class VisionAgentPlanAnswerValidator
{
    private static readonly JsonSerializerOptions FingerprintJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly HashSet<string> ParameterSelectionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ResultJudgment.ExpectValue",
        "ExpectValue",
        "classification_ok_label",
        "ok_label",
        "targetAttribute",
        "ResultJudgment.ExpectValueMin",
        "ResultJudgment.ExpectValueMax",
        "ResultJudgment.MinConfidence",
        "ExpectValueMin",
        "ExpectValueMax",
        "MinConfidence",
        "measurement_min",
        "measurement_max",
        "lower_bound",
        "upper_bound",
        "min_value",
        "max_value",
        "max_defect_area",
        "max_defect_count",
        "defect_upper_bound",
        "expected_presence",
        "presence_expected",
        "presence_state",
        "expected_state",
        "expected_code",
        "code_value",
        "expected_text",
        "expected_object_count",
        "object_count",
        "min_object_count",
        "max_object_count",
        "classification_min_confidence",
        "template_min_confidence"
    };

    public VisionAgentPlanAnswerValidationResult Validate(
        VisionAgentPlanModeResult? plan,
        IEnumerable<VisionAgentPlanAnswer>? confirmedAnswers,
        IReadOnlyDictionary<string, string>? legacySelections,
        bool acceptedRecommendedDefaults)
    {
        var questions = (plan?.ClarificationQuestions ?? [])
            .Where(question => !string.IsNullOrWhiteSpace(question.Id))
            .GroupBy(question => Clean(question.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var blockingReasons = plan?.BlockingReasons ?? [];
        var candidates = new List<CandidateAnswer>();
        var invalidQuestionIds = new List<string>();
        var invalidValues = new List<string>();
        var warnings = new List<string>();
        var parameterSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var deferred = ResolveDeferredSelections(questions.Values, blockingReasons, legacySelections);
        var answerList = (confirmedAnswers ?? []).ToList();

        foreach (var answer in answerList)
        {
            var answerField = VisionAgentPlanFieldPolicy.NormalizeField(answer.Field);
            if (deferred.QuestionIds.Contains(Clean(answer.QuestionId)) ||
                (!string.IsNullOrWhiteSpace(answerField) && deferred.Fields.Contains(answerField)))
            {
                warnings.Add($"confirmed_answer_suppressed_by_defer:{answerField}");
                continue;
            }
            if (TryValidateAnswer(answer, questions, blockingReasons, false, candidates, invalidQuestionIds, invalidValues, warnings))
            {
                continue;
            }
        }

        if (acceptedRecommendedDefaults)
        {
            foreach (var question in questions.Values)
            {
                var field = VisionAgentPlanFieldPolicy.ResolveQuestionField(question, blockingReasons);
                if (string.IsNullOrWhiteSpace(field))
                {
                    field = FallbackQuestionField(question);
                }

                var recommended = RecommendedValue(question);
                if (string.IsNullOrWhiteSpace(recommended))
                {
                    continue;
                }

                if ((legacySelections?.TryGetValue(question.Id, out var selected) == true ||
                     legacySelections?.TryGetValue(field, out selected) == true) &&
                    !string.Equals(Clean(selected), recommended, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TryValidateAnswer(
                    new VisionAgentPlanAnswer
                    {
                        QuestionId = question.Id,
                        Field = field,
                        Value = recommended,
                        Origin = VisionAgentPlanAnswerOrigins.AcceptedRecommendedDefault
                    },
                    questions,
                    blockingReasons,
                    true,
                    candidates,
                    invalidQuestionIds,
                    invalidValues,
                    warnings);
            }
        }

        foreach (var item in legacySelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            var key = Clean(item.Key);
            var value = CleanValue(item.Value);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            if (VisionAgentPlanFieldPolicy.IsPlaceholderValue(value))
            {
                warnings.Add($"placeholder_selection_ignored:{key}");
                continue;
            }

            if (questions.ContainsKey(key) ||
                VisionAgentPlanFieldPolicy.TryGet(key, out _))
            {
                warnings.Add($"legacy_plan_selection_ignored:{key}");
                continue;
            }

            if (IsParameterSelectionKey(key))
            {
                parameterSelections[key] = value;
            }
            else
            {
                warnings.Add($"unknown_selection_ignored:{key}");
            }
        }

        var accepted = ResolveConflicts(candidates, out var conflictedFields);
        var taskResolution = VisionAgentTaskTypeResolver.Resolve(
            plan,
            (plan?.ConfirmedPlanAnswers ?? []).Concat(answerList));
        warnings.AddRange(taskResolution.Warnings);
        if (taskResolution.BlockingConflict)
        {
            accepted.RemoveAll(answer =>
                string.Equals(answer.Field, VisionAgentPlanAnswerFields.TaskType, StringComparison.OrdinalIgnoreCase));
            conflictedFields.Add(VisionAgentPlanAnswerFields.TaskType);
        }
        else if (!string.IsNullOrWhiteSpace(taskResolution.CanonicalValue))
        {
            accepted = accepted.Select(answer =>
                    string.Equals(answer.Field, VisionAgentPlanAnswerFields.TaskType, StringComparison.OrdinalIgnoreCase)
                        ? answer with { Value = taskResolution.CanonicalValue }
                        : answer)
                .ToList();
        }
        var requirementAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var buildDecisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var answer in accepted)
        {
            if (!VisionAgentPlanFieldPolicy.TryGet(answer.Field, out var rule))
            {
                continue;
            }

            if (rule.Category == VisionAgentPlanFieldCategories.Requirement)
            {
                requirementAnswers[rule.Field] = answer.Value;
            }
            else if (rule.Category == VisionAgentPlanFieldCategories.BuildDecision)
            {
                buildDecisions[rule.Field] = answer.Value;
            }
        }

        return new VisionAgentPlanAnswerValidationResult(
            accepted,
            requirementAnswers,
            buildDecisions,
            parameterSelections,
            accepted.Select(answer => VisionAgentPlanFieldPolicy.NormalizeField(answer.Field))
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            invalidQuestionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            invalidValues.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            conflictedFields.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ComputeFingerprint(accepted),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            DeferredFields = deferred.Fields
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            CanonicalTaskType = taskResolution.CanonicalValue,
            TaskTypeNormalizationAudit = taskResolution.Evidence
                .Select(item => new VisionAgentTaskTypeNormalizationAudit(
                    item.Source,
                    item.RawValue,
                    item.CanonicalValue,
                    item.ExplicitUserChoice))
                .ToList()
        };
    }

    private static bool TryValidateAnswer(
        VisionAgentPlanAnswer answer,
        IReadOnlyDictionary<string, VisionAgentClarificationQuestion> questions,
        IReadOnlyList<string> blockingReasons,
        bool generatedRecommended,
        List<CandidateAnswer> candidates,
        List<string> invalidQuestionIds,
        List<string> invalidValues,
        List<string> warnings)
    {
        var origin = NormalizeOrigin(answer.Origin);
        if (string.IsNullOrWhiteSpace(origin))
        {
            invalidValues.Add($"{Clean(answer.QuestionId)}:invalid_origin");
            return false;
        }
        if (!VisionAgentPlanFieldPolicy.IsAuthoritativeConfirmationOrigin(origin))
        {
            warnings.Add($"non_authoritative_answer_ignored:{Clean(answer.Field)}:{origin}");
            return false;
        }

        var questionId = Clean(answer.QuestionId);
        var knownQuestion = !string.IsNullOrWhiteSpace(questionId) &&
                            questions.TryGetValue(questionId, out var questionForField)
            ? questionForField
            : null;
        var field = VisionAgentPlanFieldPolicy.NormalizeField(answer.Field);
        if (string.IsNullOrWhiteSpace(field) && knownQuestion != null)
        {
            field = VisionAgentPlanFieldPolicy.ResolveQuestionField(knownQuestion, blockingReasons);
            if (string.IsNullOrWhiteSpace(field))
            {
                field = FallbackQuestionField(knownQuestion);
            }
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            invalidValues.Add($"{Clean(answer.QuestionId)}:invalid_field");
            return false;
        }

        var value = CleanValue(answer.Value);
        if (string.IsNullOrWhiteSpace(value))
        {
            invalidValues.Add($"{questionId}:{field}:empty_value");
            return false;
        }
        if (VisionAgentPlanFieldPolicy.IsPlaceholderValue(value))
        {
            invalidValues.Add($"{questionId}:{field}:placeholder_value");
            return false;
        }
        var acceptedValue = value;
        if (string.Equals(field, VisionAgentPlanAnswerFields.TaskType, StringComparison.OrdinalIgnoreCase))
        {
            if (!AiVisionTaskCatalog.TryNormalizePrimary(value, out acceptedValue))
            {
                invalidValues.Add($"{questionId}:{field}:unsupported_task_type");
                return false;
            }
            if (origin == VisionAgentPlanAnswerOrigins.ExplicitUserText &&
                string.IsNullOrWhiteSpace(answer.EvidenceText))
            {
                invalidValues.Add($"{questionId}:{field}:explicit_text_evidence_missing");
                warnings.Add("explicit_user_text_missing_evidence:task_type");
                return false;
            }
        }
        else if (string.Equals(field, VisionAgentPlanAnswerFields.ImageSource, StringComparison.OrdinalIgnoreCase))
        {
            var imageSource = VisionAgentImageSourceResolver.Resolve(value);
            if (!imageSource.Supported)
            {
                invalidValues.Add($"{questionId}:{field}:{imageSource.DiagnosticCode}");
                return false;
            }
        }

        if (origin == VisionAgentPlanAnswerOrigins.ExplicitUserText)
        {
            return TryValidateTextAnswer(
                questionId,
                field,
                acceptedValue,
                answer.EvidenceText,
                questions,
                blockingReasons,
                candidates,
                invalidQuestionIds,
                invalidValues);
        }

        if (string.IsNullOrWhiteSpace(questionId) ||
            !questions.TryGetValue(questionId, out var question))
        {
            if (VisionAgentPlanFieldPolicy.TryGet(field, out var rule))
            {
                candidates.Add(new CandidateAnswer(
                    new VisionAgentPlanAnswer
                    {
                        QuestionId = questionId,
                        Field = field,
                        Value = acceptedValue,
                        Origin = origin,
                        EvidenceText = CleanValue(answer.EvidenceText),
                        Confidence = answer.Confidence,
                        Resolved = answer.Resolved
                    },
                    VisionAgentPlanFieldPolicy.AnswerOriginPriority(origin),
                    generatedRecommended));
                return true;
            }
            invalidQuestionIds.Add(string.IsNullOrWhiteSpace(questionId) ? "<empty>" : questionId);
            return false;
        }

        var questionField = VisionAgentPlanFieldPolicy.ResolveQuestionField(question, blockingReasons);
        if (!string.IsNullOrWhiteSpace(questionField) &&
            !string.Equals(field, questionField, StringComparison.OrdinalIgnoreCase))
        {
            invalidValues.Add($"{questionId}:field_mismatch");
            return false;
        }

        var optionValues = question.Options
            .Select(option => Clean(option.Value))
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!optionValues.Contains(value))
        {
            invalidValues.Add($"{questionId}:invalid_option");
            return false;
        }

        var selectedOption = question.Options.FirstOrDefault(option =>
                Clean(option.Value).Equals(value, StringComparison.OrdinalIgnoreCase));
        if (!VisionAgentPlanFieldPolicy.IsResolveFieldOption(selectedOption))
        {
            invalidValues.Add($"{questionId}:answer_effect_not_resolve_field");
            return false;
        }

        if (origin == VisionAgentPlanAnswerOrigins.AcceptedRecommendedDefault)
        {
            if (VisionAgentPlanFieldPolicy.TryGet(field, out var rule) &&
                !rule.AllowRecommendedConfirmation)
            {
                invalidValues.Add($"{questionId}:recommended_not_allowed");
                return false;
            }

            var recommended = RecommendedValue(question);
            if (!string.Equals(value, recommended, StringComparison.OrdinalIgnoreCase))
            {
                invalidValues.Add($"{questionId}:not_recommended_value");
                return false;
            }
        }

        candidates.Add(new CandidateAnswer(
            new VisionAgentPlanAnswer
            {
                QuestionId = questionId,
                Field = field,
                Value = acceptedValue,
                Origin = origin,
                EvidenceText = CleanValue(answer.EvidenceText),
                Confidence = answer.Confidence,
                Resolved = answer.Resolved
            },
            VisionAgentPlanFieldPolicy.AnswerOriginPriority(origin),
            generatedRecommended));
        return true;
    }

    private static bool TryValidateTextAnswer(
        string questionId,
        string field,
        string value,
        string evidenceText,
        IReadOnlyDictionary<string, VisionAgentClarificationQuestion> questions,
        IReadOnlyList<string> blockingReasons,
        List<CandidateAnswer> candidates,
        List<string> invalidQuestionIds,
        List<string> invalidValues)
    {
        if (!VisionAgentPlanFieldPolicy.TryGet(field, out var rule) ||
            rule.Category != VisionAgentPlanFieldCategories.Requirement ||
            !rule.AllowFreeText)
        {
            invalidValues.Add($"{questionId}:{field}:text_not_allowed");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(questionId))
        {
            if (!questions.TryGetValue(questionId, out var question))
            {
                question = null;
            }

            if (question == null)
            {
                candidates.Add(new CandidateAnswer(
                    new VisionAgentPlanAnswer
                    {
                        QuestionId = questionId,
                        Field = field,
                        Value = value,
                        Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText,
                        EvidenceText = CleanValue(evidenceText)
                    },
                    VisionAgentPlanFieldPolicy.AnswerOriginPriority(VisionAgentPlanAnswerOrigins.ExplicitUserText),
                    GeneratedRecommended: false));
                return true;
            }

            var questionField = VisionAgentPlanFieldPolicy.ResolveQuestionField(question, blockingReasons);
            if (!string.IsNullOrWhiteSpace(questionField) &&
                !string.Equals(field, questionField, StringComparison.OrdinalIgnoreCase))
            {
                invalidValues.Add($"{questionId}:field_mismatch");
                return false;
            }
        }

        candidates.Add(new CandidateAnswer(
            new VisionAgentPlanAnswer
            {
                QuestionId = questionId,
                Field = field,
                Value = value,
                Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText,
                EvidenceText = CleanValue(evidenceText)
            },
            VisionAgentPlanFieldPolicy.AnswerOriginPriority(VisionAgentPlanAnswerOrigins.ExplicitUserText),
            GeneratedRecommended: false));
        return true;
    }

    private static List<VisionAgentPlanAnswer> ResolveConflicts(
        List<CandidateAnswer> candidates,
        out List<string> conflictedFields)
    {
        conflictedFields = [];
        var accepted = new List<VisionAgentPlanAnswer>();
        foreach (var group in candidates.GroupBy(item => item.Answer.Field, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.Answer.QuestionId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var top = ordered[0];
            var samePriorityDifferent = ordered
                .Where(item => item.Priority == top.Priority)
                .Select(item => item.Answer.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1;
            if (samePriorityDifferent)
            {
                conflictedFields.Add(group.Key);
                continue;
            }

            accepted.Add(top.Answer);
        }

        return accepted
            .OrderBy(answer => answer.Field, StringComparer.OrdinalIgnoreCase)
            .ThenBy(answer => answer.QuestionId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ComputeFingerprint(IEnumerable<VisionAgentPlanAnswer> answers)
    {
        var payload = answers
            .OrderBy(answer => answer.Field, StringComparer.OrdinalIgnoreCase)
            .ThenBy(answer => answer.QuestionId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(answer => answer.Origin, StringComparer.OrdinalIgnoreCase)
            .ThenBy(answer => answer.Value, StringComparer.OrdinalIgnoreCase)
            .Select(answer => new
            {
                field = Clean(answer.Field),
                questionId = Clean(answer.QuestionId),
                origin = NormalizeOrigin(answer.Origin),
                evidenceText = CleanValue(answer.EvidenceText),
                value = CleanValue(answer.Value)
            })
            .ToList();
        var json = JsonSerializer.Serialize(payload, FingerprintJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string RecommendedValue(VisionAgentClarificationQuestion question)
    {
        var value = Clean(question.Options.FirstOrDefault(option =>
                option.Recommended &&
                VisionAgentPlanFieldPolicy.IsResolveFieldOption(option))?.Value);
        return VisionAgentPlanFieldPolicy.IsPlaceholderValue(value) ? string.Empty : value;
    }

    private static DeferredSelections ResolveDeferredSelections(
        IEnumerable<VisionAgentClarificationQuestion> questions,
        IReadOnlyList<string> blockingReasons,
        IReadOnlyDictionary<string, string>? selections)
    {
        var questionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (selections == null || selections.Count == 0)
        {
            return new DeferredSelections(questionIds, fields);
        }

        foreach (var question in questions)
        {
            var questionId = Clean(question.Id);
            var field = VisionAgentPlanFieldPolicy.ResolveQuestionField(question, blockingReasons);
            if (string.IsNullOrWhiteSpace(field))
            {
                field = FallbackQuestionField(question);
            }
            if (!selections.TryGetValue(questionId, out var selected) &&
                (string.IsNullOrWhiteSpace(field) || !selections.TryGetValue(field, out selected)))
            {
                continue;
            }

            var option = question.Options.FirstOrDefault(candidate =>
                Clean(candidate.Value).Equals(CleanValue(selected), StringComparison.OrdinalIgnoreCase));
            if (option == null ||
                !string.Equals(
                    VisionAgentPlanFieldPolicy.NormalizeAnswerEffect(option),
                    VisionAgentClarificationAnswerEffects.Defer,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            questionIds.Add(questionId);
            if (!string.IsNullOrWhiteSpace(field)) fields.Add(field);
        }

        return new DeferredSelections(questionIds, fields);
    }

    private static bool IsParameterSelectionKey(string key)
    {
        return ParameterSelectionKeys.Contains(key) ||
               key.Contains('.', StringComparison.Ordinal) ||
               (!string.IsNullOrWhiteSpace(key) && char.IsUpper(key[0]));
    }

    private static string FallbackQuestionField(VisionAgentClarificationQuestion question)
    {
        return Clean(question.Field) is { Length: > 0 } field
            ? field
            : Clean(question.Id);
    }

    private static string NormalizeOrigin(string? origin)
    {
        return Clean(origin).ToLowerInvariant() switch
        {
            VisionAgentPlanAnswerOrigins.ExplicitUserSelection => VisionAgentPlanAnswerOrigins.ExplicitUserSelection,
            VisionAgentPlanAnswerOrigins.AcceptedRecommendedDefault => VisionAgentPlanAnswerOrigins.AcceptedRecommendedDefault,
            VisionAgentPlanAnswerOrigins.ExplicitUserText => VisionAgentPlanAnswerOrigins.ExplicitUserText,
            VisionAgentPlanAnswerOrigins.RuleInferred => VisionAgentPlanAnswerOrigins.RuleInferred,
            VisionAgentPlanAnswerOrigins.LegacyInferred => VisionAgentPlanAnswerOrigins.LegacyInferred,
            VisionAgentPlanAnswerOrigins.ResourceBound => VisionAgentPlanAnswerOrigins.ResourceBound,
            VisionAgentPlanAnswerOrigins.ModelInferred => VisionAgentPlanAnswerOrigins.ModelInferred,
            VisionAgentPlanAnswerOrigins.DefaultAssumption => VisionAgentPlanAnswerOrigins.DefaultAssumption,
            _ => string.Empty
        };
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string CleanValue(string? value)
    {
        var text = Clean(value);
        if (text.Length > 256)
        {
            text = text[..256];
        }

        return text.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private sealed record CandidateAnswer(
        VisionAgentPlanAnswer Answer,
        int Priority,
        bool GeneratedRecommended);

    private sealed record DeferredSelections(
        HashSet<string> QuestionIds,
        HashSet<string> Fields);
}
