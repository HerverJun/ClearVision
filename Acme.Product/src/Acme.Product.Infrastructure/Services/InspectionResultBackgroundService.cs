// InspectionResultBackgroundService.cs
// 异步处理检测结果保存的后台服务
// 作者：蘅芜君

using Acme.Product.Core.Entities;
using Acme.Product.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;

namespace Acme.Product.Infrastructure.Services;

/// <summary>
/// 检测结果异步通道写入器接口
/// </summary>
public interface IInspectionResultChannelWriter
{
    /// <summary>
    /// 尝试将检测结果写入后台队列
    /// </summary>
    bool TryWrite(InspectionResult result);

    ValueTask WriteAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        TryWrite(result);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 基于 Channel 的检测结果缓冲服务与后台保存任务
/// 解决了检测结果实时保存阻塞前端核心检测线程的问题
/// </summary>
public class InspectionResultBackgroundService : BackgroundService, IInspectionResultChannelWriter
{
    private readonly Channel<InspectionResult> _channel;
    private readonly ILogger<InspectionResultBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _batchSize;
    private readonly int _queueCapacity;

    public InspectionResultBackgroundService(
        ILogger<InspectionResultBackgroundService> logger, 
        IServiceProvider serviceProvider,
        IConfiguration? configuration = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _batchSize = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:BatchSize",
            "Performance__Persistence__BatchSize",
            "CV_PERSISTENCE_BATCH_SIZE",
            fallback: 50,
            min: 1,
            max: 1000);
        _queueCapacity = ResolveConfiguredInt(
            configuration,
            "Performance:Persistence:QueueCapacity",
            "Performance__Persistence__QueueCapacity",
            "CV_PERSISTENCE_QUEUE_CAPACITY",
            fallback: 1000,
            min: 1,
            max: 100_000);
        
        // Keep the persistence queue bounded; wait when full instead of silently dropping local critical results.
        _channel = Channel.CreateBounded<InspectionResult>(new BoundedChannelOptions(_queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true, // 本类唯一消耗
            SingleWriter = false // 多个检测流可能同时写入
        });
    }

    public bool TryWrite(InspectionResult result)
    {
        var written = _channel.Writer.TryWrite(result);
        if (!written)
        {
            _logger.LogWarning("检测结果后台保存队列已满，可能有记录被丢弃或未能成功入队: {Id}", result.Id);
        }
        return written;
    }

    public async ValueTask WriteAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        if (_channel.Writer.TryWrite(result))
        {
            return;
        }

        await _channel.Writer.WriteAsync(result, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("检测结果异步落盘后台服务已启动。");

        // 批处理缓冲
        var batch = new List<InspectionResult>(_batchSize);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 等待有数据可用
                if (await _channel.Reader.WaitToReadAsync(stoppingToken))
                {
                    // 尽最大努力读取出配置的批量
                    while (batch.Count < _batchSize && _channel.Reader.TryRead(out var result))
                    {
                        batch.Add(result);
                    }

                    if (batch.Count > 0)
                    {
                        // 落盘
                        await SaveBatchAsync(batch, stoppingToken);
                        batch.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 停止请求
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理检测异步保存时发生异常。");
            }
        }
        
        // 优雅退出前清空队列
        while (_channel.Reader.TryRead(out var lastResult))
        {
            batch.Clear();
            batch.Add(lastResult);
             while (batch.Count < _batchSize && _channel.Reader.TryRead(out var result))
             {
                 batch.Add(result);
             }
             await SaveBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task SaveBatchAsync(List<InspectionResult> results, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IInspectionResultRepository>();

            await repo.AddRangeAsync(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "异步落盘时出现错误，批次大小: {Count}", results.Count);
        }
    }

    private static int ResolveConfiguredInt(
        IConfiguration? configuration,
        string configurationKey,
        string environmentKey,
        string fallbackEnvironmentKey,
        int fallback,
        int min,
        int max)
    {
        var configured = configuration?[configurationKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(environmentKey);
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(fallbackEnvironmentKey);
        }

        return int.TryParse(configured, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }
}
