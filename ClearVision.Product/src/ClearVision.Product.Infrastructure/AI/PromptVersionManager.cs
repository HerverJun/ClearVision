// PromptVersionManager.cs
// 提示词版本管理器
// 管理提示词模板版本、指标与兼容策略
// 作者：蘅芜君
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClearVision.Product.Infrastructure.AI;

// 提示词使用指标
public class PromptMetrics
{
    public int TotalCalls { get; set; }
    public int SuccessCalls { get; set; }
    public int TotalTokenUsage { get; set; }
    public long TotalLatencyMs { get; set; }

    public double SuccessRate => TotalCalls == 0 ? 0 : (double)SuccessCalls / TotalCalls;
    public double AvgTokens => TotalCalls == 0 ? 0 : (double)TotalTokenUsage / TotalCalls;
    public double AvgLatencyMs => TotalCalls == 0 ? 0 : (double)TotalLatencyMs / TotalCalls;
}

// 提示词版本
public class PromptVersion
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public PromptMetrics Metrics { get; set; } = new();
}

public class PromptVersionList
{
    public Guid? ActiveVersionId { get; set; }
    public List<PromptVersion> Versions { get; set; } = new();
}

public interface IPromptVersionManager
{
    Task<PromptVersion> CreateVersionAsync(string name, string content, string description, string createdBy = "System");
    Task<PromptVersion?> GetVersionAsync(Guid id);
    Task<List<PromptVersion>> ListVersionsAsync();
    Task ActivateVersionAsync(Guid id);
    Task DeleteVersionAsync(Guid id);
    Task<PromptVersion> GetActiveVersionAsync();
    Task RecordMetricsAsync(Guid versionId, bool success, int tokenUsage, long latencyMs);
}

// 提示词版本管理器
public class PromptVersionManager : IPromptVersionManager
{
    private const string AuthorityName = "prompt_versions";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly AiJsonMutationAuthority<PromptVersionList> _authority;
    private readonly object _mutationGate;
    private readonly AiAuxiliaryPersistenceHealth _persistenceHealth;

    public PromptVersionManager(string? baseDirectory = null)
        : this(baseDirectory, null, null)
    {
    }

    internal PromptVersionManager(
        string? baseDirectory,
        IAiPersistenceFaultInjector? faultInjector,
        AiAuxiliaryPersistenceHealth? persistenceHealth)
    {
        var appData = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClearVision")
            : baseDirectory;

        if (!Directory.Exists(appData))
            Directory.CreateDirectory(appData);

        _persistenceHealth = persistenceHealth ?? new AiAuxiliaryPersistenceHealth();
        var filePath = Path.Combine(appData, "prompt_versions.json");
        _mutationGate = AiPersistenceFileOperations.GetMutationGate(filePath);
        _authority = new AiJsonMutationAuthority<PromptVersionList>(
            AuthorityName,
            filePath,
            static () => new PromptVersionList(),
            JsonOptions,
            faultInjector);
    }

    public Task<PromptVersion> CreateVersionAsync(string name, string content, string description, string createdBy = "System")
    {
        var version = _authority.Mutate("create_version", data =>
        {
            var candidate = new PromptVersion
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                Content = content,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy,
                Metrics = new PromptMetrics()
            };

            data.Versions.Add(candidate);
            if (data.Versions.Count == 1)
            {
                data.ActiveVersionId = candidate.Id;
            }

            return AiJsonMutation<PromptVersion>.Persist(candidate);
        });
        return Task.FromResult(version);
    }

    public Task<PromptVersion?> GetVersionAsync(Guid id)
    {
        var version = _authority.Read(data => data.Versions.FirstOrDefault(v => v.Id == id));
        return Task.FromResult(version);
    }

    public Task<List<PromptVersion>> ListVersionsAsync()
    {
        return Task.FromResult(_authority.Read(data => data.Versions.ToList()));
    }

    public Task ActivateVersionAsync(Guid id)
    {
        _authority.Mutate("activate_version", data =>
        {
            if (!data.Versions.Any(v => v.Id == id))
            {
                throw new ArgumentException("指定的提示词版本不存在。");
            }

            if (data.ActiveVersionId == id)
            {
                return AiJsonMutation<bool>.NoChange(true);
            }

            data.ActiveVersionId = id;
            return AiJsonMutation<bool>.Persist(true);
        });
        return Task.CompletedTask;
    }

    public Task DeleteVersionAsync(Guid id)
    {
        _authority.Mutate("delete_version", data =>
        {
            var version = data.Versions.FirstOrDefault(v => v.Id == id);
            if (version == null)
            {
                return AiJsonMutation<bool>.NoChange(false);
            }

            data.Versions.Remove(version);
            if (data.ActiveVersionId == id)
            {
                data.ActiveVersionId = data.Versions.OrderByDescending(v => v.CreatedAt).FirstOrDefault()?.Id;
            }

            return AiJsonMutation<bool>.Persist(true);
        });
        return Task.CompletedTask;
    }

    public Task<PromptVersion> GetActiveVersionAsync()
    {
        var active = _authority.Mutate("initialize_active_version", data =>
        {
            if (data.ActiveVersionId.HasValue)
            {
                var existing = data.Versions.FirstOrDefault(v => v.Id == data.ActiveVersionId.Value);
                if (existing != null)
                {
                    return AiJsonMutation<PromptVersion>.NoChange(existing);
                }
            }

            var defaultPrompt = @"You are a professional industrial vision inspection flow generation assistant for ClearVision platform.
Your task is to convert natural language requirements into structured JSON flow definitions.
Always respond with valid JSON that matches the ClearVision flow schema.";
            var newVersion = new PromptVersion
            {
                Id = Guid.NewGuid(),
                Name = "V1.0 - Baseline",
                Description = "系统初始默认提示词",
                Content = defaultPrompt,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                Metrics = new PromptMetrics()
            };
            data.Versions.Add(newVersion);
            data.ActiveVersionId = newVersion.Id;
            return AiJsonMutation<PromptVersion>.Persist(newVersion);
        });

        return Task.FromResult(active);
    }

    public Task RecordMetricsAsync(Guid versionId, bool success, int tokenUsage, long latencyMs)
    {
        // Keep the health transition ordered with the durable mutation. The authority uses
        // this same path-keyed monitor, so its nested lock is reentrant rather than a second
        // lock in the ordering graph. This prevents an older operation from publishing a
        // late recovery/degradation after a newer metrics commit has already completed.
        lock (_mutationGate)
        {
            try
            {
                var persisted = _authority.Mutate("record_metrics", data =>
                {
                    var version = data.Versions.FirstOrDefault(v => v.Id == versionId);
                    if (version == null)
                    {
                        return AiJsonMutation<bool>.NoChange(false);
                    }

                    version.Metrics ??= new PromptMetrics();
                    version.Metrics.TotalCalls++;
                    if (success)
                        version.Metrics.SuccessCalls++;
                    version.Metrics.TotalTokenUsage += tokenUsage;
                    version.Metrics.TotalLatencyMs += latencyMs;
                    return AiJsonMutation<bool>.Persist(true);
                });
                if (persisted)
                {
                    _persistenceHealth.ReportRecovered(AuthorityName);
                }
            }
            catch (AiAuxiliaryPersistenceException ex)
            {
                _persistenceHealth.ReportDegraded(AuthorityName, "record_metrics", ex.ErrorCode);
            }
        }

        return Task.CompletedTask;
    }
}
