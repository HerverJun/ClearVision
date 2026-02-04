using System.Collections.Concurrent;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.ImageProcessing;

/// <summary>
/// Mat 对象池 - 用于重用 OpenCV Mat 对象，减少 GC 压力
/// Sprint 5: S5-004 实现
/// </summary>
public class MatPool : IDisposable
{
    private readonly ConcurrentDictionary<MatKey, ConcurrentBag<OpenCvSharp.Mat>> _pools = new();
    private readonly int _maxSizePerKey;
    private readonly object _lock = new();
    private long _rentCount;
    private long _returnCount;
    private long _createCount;

    /// <summary>
    /// 创建 MatPool 实例
    /// </summary>
    /// <param name="maxSizePerKey">每种尺寸类型的最大池大小</param>
    public MatPool(int maxSizePerKey = 10)
    {
        _maxSizePerKey = maxSizePerKey;
    }

    /// <summary>
    /// 从池中获取 Mat 对象
    /// </summary>
    public OpenCvSharp.Mat Rent(int rows, int cols, OpenCvSharp.MatType type)
    {
        var key = new MatKey(rows, cols, type);
        
        if (_pools.TryGetValue(key, out var pool) && pool.TryTake(out var mat))
        {
            Interlocked.Increment(ref _rentCount);
            // 清空数据但保留内存
            mat.SetTo(OpenCvSharp.Scalar.All(0));
            return mat;
        }

        // 池中没有可用对象，创建新的
        Interlocked.Increment(ref _createCount);
        return new OpenCvSharp.Mat(rows, cols, type);
    }

    /// <summary>
    /// 将 Mat 对象归还到池中
    /// </summary>
    public void Return(OpenCvSharp.Mat mat)
    {
        if (mat == null || mat.IsDisposed)
            return;

        var key = new MatKey(mat.Rows, mat.Cols, mat.Type());
        
        var pool = _pools.GetOrAdd(key, _ => new ConcurrentBag<OpenCvSharp.Mat>());
        
        if (pool.Count < _maxSizePerKey)
        {
            pool.Add(mat);
            Interlocked.Increment(ref _returnCount);
        }
        else
        {
            // 池已满，释放对象
            mat.Dispose();
        }
    }

    /// <summary>
    /// 获取池统计信息
    /// </summary>
    public MatPoolStatistics GetStatistics()
    {
        var totalPooled = _pools.Values.Sum(p => p.Count);
        var totalRent = Interlocked.Read(ref _rentCount);
        var totalReturn = Interlocked.Read(ref _returnCount);
        var totalCreate = Interlocked.Read(ref _createCount);
        
        return new MatPoolStatistics
        {
            TotalPooled = totalPooled,
            TotalRented = totalRent,
            TotalReturned = totalReturn,
            TotalCreated = totalCreate,
            PoolHitRate = totalRent > 0 ? (double)totalReturn / totalRent : 0,
            PoolCount = _pools.Count
        };
    }

    /// <summary>
    /// 清空所有池
    /// </summary>
    public void Clear()
    {
        foreach (var pool in _pools.Values)
        {
            while (pool.TryTake(out var mat))
            {
                mat.Dispose();
            }
        }
        _pools.Clear();
    }

    public void Dispose()
    {
        Clear();
    }

    /// <summary>
    /// Mat 尺寸和类型键
    /// </summary>
    private readonly record struct MatKey(int Rows, int Cols, OpenCvSharp.MatType Type);
}

/// <summary>
/// MatPool 统计信息
/// </summary>
public class MatPoolStatistics
{
    public int TotalPooled { get; set; }
    public long TotalRented { get; set; }
    public long TotalReturned { get; set; }
    public long TotalCreated { get; set; }
    public double PoolHitRate { get; set; }
    public int PoolCount { get; set; }
}
