using System.Collections.Concurrent;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.AI.Anomaly;

internal static class OnnxPatchEmbeddingExtractor
{
    private const int MaxCachedSessions = 4;

    private static readonly ConcurrentDictionary<string, CachedSession> SessionCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SessionLocks = new(StringComparer.OrdinalIgnoreCase);

    private sealed class CachedSession : IDisposable
    {
        private int _leaseCount;
        private int _disposeRequested;
        private int _disposed;
        private long _lastAccessTicks;

        public CachedSession(InferenceSession session, long modelLength, DateTime modelLastWriteUtc)
        {
            Session = session;
            ModelLength = modelLength;
            ModelLastWriteUtc = modelLastWriteUtc;
            _lastAccessTicks = DateTime.UtcNow.Ticks;
        }

        public InferenceSession Session { get; }
        private long ModelLength { get; }
        private DateTime ModelLastWriteUtc { get; }
        public DateTime LastAccessUtc => new(Interlocked.Read(ref _lastAccessTicks), DateTimeKind.Utc);

        public bool Matches(FileInfo modelFile)
        {
            return modelFile.Length == ModelLength &&
                   modelFile.LastWriteTimeUtc == ModelLastWriteUtc;
        }

        public bool TryAcquireLease(out SessionLease lease)
        {
            lease = null!;

            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                return false;
            }

            Interlocked.Increment(ref _leaseCount);
            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                ReleaseLease();
                return false;
            }

            Touch();
            lease = new SessionLease(this);
            return true;
        }

        public void Touch()
        {
            Interlocked.Exchange(ref _lastAccessTicks, DateTime.UtcNow.Ticks);
        }

        public void Dispose()
        {
            Retire();
        }

        public void Retire()
        {
            if (Interlocked.Exchange(ref _disposeRequested, 1) == 0 &&
                Volatile.Read(ref _leaseCount) == 0)
            {
                DisposeSession();
            }
        }

        private void ReleaseLease()
        {
            if (Interlocked.Decrement(ref _leaseCount) == 0 &&
                Volatile.Read(ref _disposeRequested) != 0)
            {
                DisposeSession();
            }
        }

        private void DisposeSession()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Session.Dispose();
            }
        }

        public sealed class SessionLease : IDisposable
        {
            private CachedSession? _owner;

            public SessionLease(CachedSession owner)
            {
                _owner = owner;
            }

            public InferenceSession Session => _owner?.Session
                ?? throw new ObjectDisposedException(nameof(SessionLease));

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.ReleaseLease();
            }
        }
    }

    public static float[] ExtractEmbedding(Mat patch, SimplePatchCoreOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EmbeddingModelPath))
        {
            throw new InvalidOperationException("EmbeddingModelPath is required when FeatureExtractorId is 'onnx_embedding'.");
        }

        using var lease = GetOrCreateSessionLease(options.EmbeddingModelPath);
        var session = lease.Session;
        var input = session.InputMetadata.First();
        var dimensions = input.Value.Dimensions;
        var inputHeight = ResolveImageDimension(dimensions, 2, options.PatchSize);
        var inputWidth = ResolveImageDimension(dimensions, 3, options.PatchSize);
        var tensor = PreprocessPatch(patch, inputWidth, inputHeight);

        using var results = session.Run([NamedOnnxValue.CreateFromTensor(input.Key, tensor)]);
        var output = results.First().AsTensor<float>();
        var flattened = output.ToArray();
        if (flattened.Length == 0)
        {
            throw new InvalidOperationException("Embedding model returned an empty tensor.");
        }

        NormalizeInPlace(flattened);
        return flattened;
    }

    public static IReadOnlyList<float[]> ExtractEmbeddings(IReadOnlyList<Mat> patches, SimplePatchCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(patches);

        if (patches.Count == 0)
        {
            return [];
        }

        if (patches.Any(patch => patch == null || patch.Empty()))
        {
            throw new InvalidOperationException("Embedding extraction received an empty patch.");
        }

        if (string.IsNullOrWhiteSpace(options.EmbeddingModelPath))
        {
            throw new InvalidOperationException("EmbeddingModelPath is required when FeatureExtractorId is 'onnx_embedding'.");
        }

        using var lease = GetOrCreateSessionLease(options.EmbeddingModelPath);
        var session = lease.Session;
        var input = session.InputMetadata.First();
        var dimensions = input.Value.Dimensions;
        var inputHeight = ResolveImageDimension(dimensions, 2, options.PatchSize);
        var inputWidth = ResolveImageDimension(dimensions, 3, options.PatchSize);
        var maxBatchSize = ResolveBatchSize(dimensions, patches.Count);

        if (maxBatchSize <= 1)
        {
            return patches.Select(patch => ExtractEmbedding(patch, options)).ToArray();
        }

        var embeddings = new List<float[]>(patches.Count);
        for (var start = 0; start < patches.Count; start += maxBatchSize)
        {
            var batchSize = Math.Min(maxBatchSize, patches.Count - start);
            var tensor = PreprocessPatches(patches, start, batchSize, inputWidth, inputHeight);
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(input.Key, tensor)]);
            var output = results.First().AsTensor<float>();
            foreach (var embedding in SplitAndNormalizeEmbeddings(output, batchSize))
            {
                embeddings.Add(embedding);
            }
        }

        return embeddings;
    }

    private static CachedSession.SessionLease GetOrCreateSessionLease(string modelPath)
    {
        var resolvedModelPath = Path.GetFullPath(modelPath);
        var modelFile = new FileInfo(resolvedModelPath);
        if (!modelFile.Exists)
        {
            throw new FileNotFoundException("Embedding ONNX model was not found.", resolvedModelPath);
        }

        if (TryGetCurrentCachedSession(resolvedModelPath, modelFile, out var lease))
        {
            return lease;
        }

        var gate = SessionLocks.GetOrAdd(resolvedModelPath, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            modelFile.Refresh();
            if (!modelFile.Exists)
            {
                throw new FileNotFoundException("Embedding ONNX model was not found.", resolvedModelPath);
            }

            if (TryGetCurrentCachedSession(resolvedModelPath, modelFile, out lease))
            {
                return lease;
            }

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            var session = new InferenceSession(resolvedModelPath, options);
            var cachedSession = new CachedSession(session, modelFile.Length, modelFile.LastWriteTimeUtc);
            SessionCache[resolvedModelPath] = cachedSession;
            EnforceSessionCacheLimit(resolvedModelPath);

            if (cachedSession.TryAcquireLease(out lease))
            {
                return lease;
            }

            throw new ObjectDisposedException(nameof(InferenceSession), "Embedding ONNX session was evicted before it could be leased.");
        }
        finally
        {
            gate.Release();
        }
    }

    internal static void ClearSessionCache()
    {
        foreach (var key in SessionCache.Keys)
        {
            if (SessionCache.TryRemove(key, out var cached))
            {
                cached.Retire();
            }
        }
    }

    private static bool TryGetCurrentCachedSession(
        string resolvedModelPath,
        FileInfo modelFile,
        out CachedSession.SessionLease lease)
    {
        lease = null!;

        if (!SessionCache.TryGetValue(resolvedModelPath, out var cached))
        {
            return false;
        }

        if (cached.Matches(modelFile))
        {
            return cached.TryAcquireLease(out lease);
        }

        if (SessionCache.TryRemove(new KeyValuePair<string, CachedSession>(resolvedModelPath, cached)))
        {
            cached.Retire();
        }

        return false;
    }

    private static void EnforceSessionCacheLimit(string protectedKey)
    {
        if (SessionCache.Count <= MaxCachedSessions)
        {
            return;
        }

        foreach (var candidate in SessionCache
                     .Where(entry => !entry.Key.Equals(protectedKey, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.Value.LastAccessUtc))
        {
            if (SessionCache.Count <= MaxCachedSessions)
            {
                break;
            }

            if (SessionCache.TryRemove(new KeyValuePair<string, CachedSession>(candidate.Key, candidate.Value)))
            {
                candidate.Value.Retire();
            }
        }
    }

    private static DenseTensor<float> PreprocessPatch(Mat patch, int inputWidth, int inputHeight)
    {
        using var prepared = EnsureThreeChannel(patch);
        using var resized = new Mat();
        Cv2.Resize(prepared, resized, new Size(inputWidth, inputHeight), 0, 0, InterpolationFlags.Linear);

        using var floatImage = new Mat();
        resized.ConvertTo(floatImage, MatType.CV_32FC3, 1.0 / 255.0);

        var tensor = new DenseTensor<float>([1, 3, inputHeight, inputWidth]);
        var indexer = floatImage.GetGenericIndexer<Vec3f>();
        for (var y = 0; y < inputHeight; y++)
        {
            for (var x = 0; x < inputWidth; x++)
            {
                var pixel = indexer[y, x];
                tensor[0, 0, y, x] = pixel.Item2;
                tensor[0, 1, y, x] = pixel.Item1;
                tensor[0, 2, y, x] = pixel.Item0;
            }
        }

        return tensor;
    }

    private static DenseTensor<float> PreprocessPatches(
        IReadOnlyList<Mat> patches,
        int start,
        int batchSize,
        int inputWidth,
        int inputHeight)
    {
        var tensor = new DenseTensor<float>([batchSize, 3, inputHeight, inputWidth]);

        for (var batchIndex = 0; batchIndex < batchSize; batchIndex++)
        {
            using var prepared = EnsureThreeChannel(patches[start + batchIndex]);
            using var resized = new Mat();
            Cv2.Resize(prepared, resized, new Size(inputWidth, inputHeight), 0, 0, InterpolationFlags.Linear);

            using var floatImage = new Mat();
            resized.ConvertTo(floatImage, MatType.CV_32FC3, 1.0 / 255.0);

            var indexer = floatImage.GetGenericIndexer<Vec3f>();
            for (var y = 0; y < inputHeight; y++)
            {
                for (var x = 0; x < inputWidth; x++)
                {
                    var pixel = indexer[y, x];
                    tensor[batchIndex, 0, y, x] = pixel.Item2;
                    tensor[batchIndex, 1, y, x] = pixel.Item1;
                    tensor[batchIndex, 2, y, x] = pixel.Item0;
                }
            }
        }

        return tensor;
    }

    private static Mat EnsureThreeChannel(Mat source)
    {
        if (source.Channels() == 3)
        {
            return source.Clone();
        }

        var converted = new Mat();
        if (source.Channels() == 1)
        {
            Cv2.CvtColor(source, converted, ColorConversionCodes.GRAY2BGR);
        }
        else
        {
            Cv2.CvtColor(source, converted, ColorConversionCodes.BGRA2BGR);
        }

        return converted;
    }

    private static int ResolveImageDimension(IReadOnlyList<int> dimensions, int index, int fallback)
    {
        if (dimensions.Count <= index)
        {
            return Math.Max(1, fallback);
        }

        var value = dimensions[index];
        return value > 0 ? value : Math.Max(1, fallback);
    }

    private static int ResolveBatchSize(IReadOnlyList<int> dimensions, int requestedBatchSize)
    {
        if (dimensions.Count == 0)
        {
            return 1;
        }

        var batchDimension = dimensions[0];
        if (batchDimension <= 0)
        {
            return requestedBatchSize;
        }

        return Math.Max(1, batchDimension);
    }

    private static IEnumerable<float[]> SplitAndNormalizeEmbeddings(Tensor<float> output, int batchSize)
    {
        var flattened = output.ToArray();
        if (flattened.Length == 0)
        {
            throw new InvalidOperationException("Embedding model returned an empty tensor.");
        }

        if (batchSize == 1)
        {
            NormalizeInPlace(flattened);
            yield return flattened;
            yield break;
        }

        if (flattened.Length % batchSize != 0)
        {
            throw new InvalidOperationException(
                $"Embedding tensor length {flattened.Length} is not divisible by batch size {batchSize}.");
        }

        var embeddingLength = flattened.Length / batchSize;
        for (var batchIndex = 0; batchIndex < batchSize; batchIndex++)
        {
            var embedding = new float[embeddingLength];
            Array.Copy(flattened, batchIndex * embeddingLength, embedding, 0, embeddingLength);
            NormalizeInPlace(embedding);
            yield return embedding;
        }
    }

    private static void NormalizeInPlace(float[] values)
    {
        double sum = 0;
        for (var i = 0; i < values.Length; i++)
        {
            sum += values[i] * values[i];
        }

        var norm = Math.Sqrt(sum);
        if (norm <= 1e-12)
        {
            return;
        }

        var inv = (float)(1.0 / norm);
        for (var i = 0; i < values.Length; i++)
        {
            values[i] *= inv;
        }
    }
}
