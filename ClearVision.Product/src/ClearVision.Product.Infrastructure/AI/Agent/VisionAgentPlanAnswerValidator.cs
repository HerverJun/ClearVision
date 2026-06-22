using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

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
    List<string> Warnings);

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
        "targetAttribute"
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

        foreach (var answer in confirmedAnswers ?? [])
        {
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
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
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

        if (origin == VisionAgentPlanAnswerOrigins.ExplicitUserText)
        {
            return TryValidateTextAnswer(
                questionId,
                field,
                value,
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
                        Value = value,
                        Origin = origin
                    },
                    Priority(origin),
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
                Value = value,
                Origin = origin
            },
            Priority(origin),
            generatedRecommended));
        return true;
    }

    private static bool TryValidateTextAnswer(
        string questionId,
        string field,
        string value,
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
                        Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                    },
                    Priority(VisionAgentPlanAnswerOrigins.ExplicitUserText),
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
                Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
            },
            Priority(VisionAgentPlanAnswerOrigins.ExplicitUserText),
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
                value = CleanValue(answer.Value)
            })
            .ToList();
        var json = JsonSerializer.Serialize(payload, FingerprintJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string RecommendedValue(VisionAgentClarificationQuestion question)
    {
        var value = Clean(question.Options.FirstOrDefault(option => option.Recommended)?.Value) is { Length: > 0 } recommended
            ? recommended
            : Clean(question.DefaultValue);
        return VisionAgentPlanFieldPolicy.IsPlaceholderValue(value) ? string.Empty : value;
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
            VisionAgentPlanAnswerOrigins.LegacyInferred => VisionAgentPlanAnswerOrigins.LegacyInferred,
            VisionAgentPlanAnswerOrigins.ResourceBound => VisionAgentPlanAnswerOrigins.ResourceBound,
            VisionAgentPlanAnswerOrigins.ModelInferred => VisionAgentPlanAnswerOrigins.ModelInferred,
            VisionAgentPlanAnswerOrigins.DefaultAssumption => VisionAgentPlanAnswerOrigins.DefaultAssumption,
            _ => string.Empty
        };
    }

    private static int Priority(string origin)
    {
        return origin switch
        {
            VisionAgentPlanAnswerOrigins.ExplicitUserText => 6,
            VisionAgentPlanAnswerOrigins.ExplicitUserSelection => 6,
            VisionAgentPlanAnswerOrigins.ResourceBound => 5,
            VisionAgentPlanAnswerOrigins.ModelInferred => 4,
            VisionAgentPlanAnswerOrigins.AcceptedRecommendedDefault => 3,
            VisionAgentPlanAnswerOrigins.LegacyInferred => 2,
            VisionAgentPlanAnswerOrigins.DefaultAssumption => 1,
            _ => 0
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
}
