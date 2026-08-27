using System.Security.Cryptography;

namespace ClearVision.Product.Desktop.PreviewArtifacts;

public sealed class PreviewArtifactStore : IDisposable
{
    private const int ArtifactIdBytes = 32;
    private const int ArtifactIdLength = 43;

    private readonly object _gate = new();
    private readonly Dictionary<string, PreviewArtifactEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<PreviewArtifactOwnerScope, HashSet<string>> _ownerIndex = new();
    private readonly PreviewArtifactStoreOptions _options;
    private readonly IPreviewArtifactClock _clock;
    private readonly System.Threading.Timer? _cleanupTimer;
    private long _totalBytes;
    private bool _disposed;

    public PreviewArtifactStore(
        PreviewArtifactStoreOptions? options = null,
        IPreviewArtifactClock? clock = null)
    {
        var configured = options ?? new PreviewArtifactStoreOptions();
        _options = new PreviewArtifactStoreOptions
        {
            Ttl = configured.Ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : configured.Ttl,
            MaxEntries = Math.Max(0, configured.MaxEntries),
            MaxTotalBytes = Math.Max(0, configured.MaxTotalBytes),
            MaxEntryBytes = Math.Max(0, Math.Min(configured.MaxEntryBytes, configured.MaxTotalBytes)),
            CleanupInterval = configured.CleanupInterval <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : configured.CleanupInterval
        };
        _clock = clock ?? new SystemPreviewArtifactClock();
        _cleanupTimer = new System.Threading.Timer(
            _ => CleanupExpired(),
            null,
            _options.CleanupInterval,
            _options.CleanupInterval);
    }

    public PreviewArtifactStoreOptions Options => _options;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return _totalBytes;
            }
        }
    }

    public PreviewArtifactBatch CreateBatch(PreviewArtifactOwnerScope owner)
    {
        ThrowIfDisposed();
        return new PreviewArtifactBatch(this, owner);
    }

    public bool TryRead(string? artifactId, string? userId, out PreviewArtifactReadResult? result)
    {
        result = null;
        if (!IsValidArtifactId(artifactId))
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (!_entries.TryGetValue(artifactId!, out var entry))
            {
                return false;
            }

            if (!string.Equals(entry.Owner.UserId, userId, StringComparison.Ordinal))
            {
                return false;
            }

            if (IsExpired(entry))
            {
                RemoveEntryUnderLock(artifactId!);
                return false;
            }

            result = new PreviewArtifactReadResult(entry.ToReference(), entry.Bytes.ToArray());
            return true;
        }
    }

    public bool Delete(string? artifactId, string? userId)
    {
        if (!IsValidArtifactId(artifactId))
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (!_entries.TryGetValue(artifactId!, out var entry) ||
                !string.Equals(entry.Owner.UserId, userId, StringComparison.Ordinal))
            {
                return false;
            }

            return RemoveEntryUnderLock(artifactId!);
        }
    }

    public int RevokeOwner(PreviewArtifactOwnerScope owner)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return 0;
            }

            return RemoveOwnerUnderLock(owner, keepIds: null);
        }
    }

    public int CleanupExpired()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return 0;
            }

            var expiredIds = _entries
                .Where(pair => IsExpired(pair.Value))
                .Select(pair => pair.Key)
                .ToList();

            foreach (var artifactId in expiredIds)
            {
                RemoveEntryUnderLock(artifactId);
            }

            return expiredIds.Count;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _entries.Clear();
            _ownerIndex.Clear();
            _totalBytes = 0;
        }

        _cleanupTimer?.Dispose();
    }

    public static bool IsValidArtifactId(string? artifactId)
    {
        if (artifactId == null || artifactId.Length != ArtifactIdLength)
        {
            return false;
        }

        foreach (var character in artifactId)
        {
            if ((character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') ||
                character == '-' ||
                character == '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    internal void ValidatePendingBatchCanContain(
        int pendingCount,
        long pendingBytes,
        string pathHint)
    {
        ThrowIfDisposed();
        if (_options.MaxEntries <= 0 || _options.MaxTotalBytes <= 0 || _options.MaxEntryBytes <= 0)
        {
            throw new PreviewArtifactStoreRejectedException("PreviewArtifactStore capacity is disabled.");
        }

        if (pendingCount > _options.MaxEntries)
        {
            throw new PreviewArtifactStoreRejectedException(
                $"Preview artifact batch at {pathHint} has {pendingCount} entries, exceeding max entries {_options.MaxEntries}.");
        }

        if (pendingBytes > _options.MaxTotalBytes)
        {
            throw new PreviewArtifactStoreRejectedException(
                $"Preview artifact batch at {pathHint} has {pendingBytes} bytes, exceeding max total {_options.MaxTotalBytes}.");
        }
    }

    internal PreviewArtifactPendingEntry CreatePendingEntry(
        PreviewArtifactOwnerScope owner,
        string kind,
        string role,
        string pathHint,
        string contentType,
        byte[] bytes,
        int? width,
        int? height,
        int? channels)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(bytes);
        if (_options.MaxEntries <= 0 || _options.MaxTotalBytes <= 0 || _options.MaxEntryBytes <= 0)
        {
            throw new PreviewArtifactStoreRejectedException("PreviewArtifactStore capacity is disabled.");
        }

        if (bytes.LongLength > _options.MaxEntryBytes)
        {
            throw new PreviewArtifactStoreRejectedException(
                $"Preview artifact {pathHint} has {bytes.LongLength} bytes, exceeding max entry {_options.MaxEntryBytes}.");
        }

        var createdAt = _clock.UtcNow;
        var expiresAt = createdAt.Add(_options.Ttl);
        var clonedBytes = bytes.ToArray();
        var sha256 = ComputeSha256(clonedBytes);
        var artifactId = GenerateArtifactId();
        var entry = new PreviewArtifactPendingEntry(
            artifactId,
            owner,
            kind,
            role,
            pathHint,
            contentType,
            clonedBytes,
            sha256,
            createdAt,
            expiresAt,
            width,
            height,
            channels);

        return entry;
    }

    internal void CommitBatch(
        PreviewArtifactOwnerScope owner,
        IReadOnlyList<PreviewArtifactPendingEntry> pendingEntries)
    {
        ArgumentNullException.ThrowIfNull(pendingEntries);
        lock (_gate)
        {
            ThrowIfDisposedUnderLock();
            CleanupExpiredUnderLock();

            var pending = pendingEntries.ToList();
            var pendingIds = new HashSet<string>(StringComparer.Ordinal);
            long pendingBytes = 0;
            foreach (var entry in pending)
            {
                if (entry.Owner != owner)
                {
                    throw new PreviewArtifactStoreRejectedException("Preview artifact batch contains a mismatched owner.");
                }

                if (!pendingIds.Add(entry.ArtifactId))
                {
                    throw new PreviewArtifactStoreRejectedException("Preview artifact batch contains a duplicate artifact id.");
                }

                if (entry.Length > _options.MaxEntryBytes)
                {
                    throw new PreviewArtifactStoreRejectedException(
                        $"Preview artifact {entry.PathHint} has {entry.Length} bytes, exceeding max entry {_options.MaxEntryBytes}.");
                }

                pendingBytes += entry.Length;
            }

            if (pending.Count > 0)
            {
                if (_options.MaxEntries <= 0 || _options.MaxTotalBytes <= 0 || _options.MaxEntryBytes <= 0)
                {
                    throw new PreviewArtifactStoreRejectedException("PreviewArtifactStore capacity is disabled.");
                }

                if (pending.Count > _options.MaxEntries)
                {
                    throw new PreviewArtifactStoreRejectedException(
                        $"Preview artifact batch has {pending.Count} entries, exceeding max entries {_options.MaxEntries}.");
                }

                if (pendingBytes > _options.MaxTotalBytes)
                {
                    throw new PreviewArtifactStoreRejectedException(
                        $"Preview artifact batch has {pendingBytes} bytes, exceeding max total {_options.MaxTotalBytes}.");
                }
            }

            var ownerIdsToReplace = _ownerIndex.TryGetValue(owner, out var ownerIds)
                ? ownerIds.ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (var artifactId in pendingIds)
            {
                if (_entries.ContainsKey(artifactId) && !ownerIdsToReplace.Contains(artifactId))
                {
                    throw new PreviewArtifactStoreRejectedException("Preview artifact id collision detected.");
                }
            }

            var survivorCandidates = _entries.Values
                .Where(entry => !ownerIdsToReplace.Contains(entry.ArtifactId))
                .OrderBy(entry => entry.CreatedAtUtc)
                .ThenBy(entry => entry.ArtifactId, StringComparer.Ordinal)
                .ToList();

            var plannedEvictions = new HashSet<string>(StringComparer.Ordinal);
            var survivorCount = survivorCandidates.Count;
            var survivorBytes = survivorCandidates.Sum(entry => entry.Bytes.LongLength);
            var evictionIndex = 0;
            while (survivorCount + pending.Count > _options.MaxEntries ||
                   survivorBytes + pendingBytes > _options.MaxTotalBytes)
            {
                if (evictionIndex >= survivorCandidates.Count)
                {
                    throw new PreviewArtifactStoreRejectedException("Preview artifact batch cannot fit into store capacity.");
                }

                var candidate = survivorCandidates[evictionIndex++];
                if (!plannedEvictions.Add(candidate.ArtifactId))
                {
                    continue;
                }

                survivorCount--;
                survivorBytes -= candidate.Bytes.LongLength;
            }

            foreach (var artifactId in ownerIdsToReplace)
            {
                RemoveEntryUnderLock(artifactId);
            }

            foreach (var artifactId in plannedEvictions)
            {
                RemoveEntryUnderLock(artifactId);
            }

            foreach (var pendingEntry in pending)
            {
                var entry = pendingEntry.ToCommittedEntry();
                _entries[entry.ArtifactId] = entry;
                if (!_ownerIndex.TryGetValue(owner, out var newOwnerIds))
                {
                    newOwnerIds = new HashSet<string>(StringComparer.Ordinal);
                    _ownerIndex[owner] = newOwnerIds;
                }

                newOwnerIds.Add(entry.ArtifactId);
                _totalBytes += entry.Bytes.LongLength;
            }
        }
    }

    private static string GenerateArtifactId()
    {
        var bytes = RandomNumberGenerator.GetBytes(ArtifactIdBytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private bool IsExpired(PreviewArtifactEntry entry) =>
        entry.ExpiresAtUtc <= _clock.UtcNow;

    private void CleanupExpiredUnderLock()
    {
        var expiredIds = _entries
            .Where(pair => IsExpired(pair.Value))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var artifactId in expiredIds)
        {
            RemoveEntryUnderLock(artifactId);
        }
    }

    private int RemoveOwnerUnderLock(PreviewArtifactOwnerScope owner, HashSet<string>? keepIds)
    {
        if (!_ownerIndex.TryGetValue(owner, out var ownerIds))
        {
            return 0;
        }

        var removed = 0;
        foreach (var artifactId in ownerIds.ToList())
        {
            if (keepIds != null && keepIds.Contains(artifactId))
            {
                continue;
            }

            if (RemoveEntryUnderLock(artifactId))
            {
                removed++;
            }
        }

        if (ownerIds.Count == 0)
        {
            _ownerIndex.Remove(owner);
        }

        return removed;
    }

    private bool RemoveEntryUnderLock(string artifactId)
    {
        if (!_entries.Remove(artifactId, out var entry))
        {
            return false;
        }

        _totalBytes = Math.Max(0, _totalBytes - entry.Bytes.LongLength);
        if (_ownerIndex.TryGetValue(entry.Owner, out var ownerIds))
        {
            ownerIds.Remove(artifactId);
            if (ownerIds.Count == 0)
            {
                _ownerIndex.Remove(entry.Owner);
            }
        }

        return true;
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
        {
            ThrowIfDisposedUnderLock();
        }
    }

    private void ThrowIfDisposedUnderLock()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

public sealed class PreviewArtifactBatch : IDisposable
{
    private readonly PreviewArtifactStore _store;
    private readonly PreviewArtifactOwnerScope _owner;
    private readonly List<PreviewArtifactPendingEntry> _pendingEntries = new();
    private bool _completed;

    internal PreviewArtifactBatch(PreviewArtifactStore store, PreviewArtifactOwnerScope owner)
    {
        _store = store;
        _owner = owner;
    }

    private long PendingBytes => _pendingEntries.Sum(entry => entry.Length);

    public PreviewArtifactReferenceV1 Add(
        string kind,
        string role,
        string pathHint,
        string contentType,
        byte[] bytes,
        int? width = null,
        int? height = null,
        int? channels = null)
    {
        if (_completed)
        {
            throw new InvalidOperationException("Preview artifact batch is already completed.");
        }

        ArgumentNullException.ThrowIfNull(bytes);
        _store.ValidatePendingBatchCanContain(
            _pendingEntries.Count + 1,
            PendingBytes + bytes.LongLength,
            pathHint);

        var pending = _store.CreatePendingEntry(
            _owner,
            kind,
            role,
            pathHint,
            contentType,
            bytes,
            width,
            height,
            channels);
        _pendingEntries.Add(pending);
        return pending.ToReference();
    }

    public void Commit()
    {
        if (_completed)
        {
            return;
        }

        _store.CommitBatch(_owner, _pendingEntries);
        _completed = true;
    }

    public void Rollback()
    {
        if (_completed)
        {
            return;
        }

        _pendingEntries.Clear();
        _completed = true;
    }

    public void Dispose()
    {
        Rollback();
    }
}

public sealed class PreviewArtifactStoreRejectedException : Exception
{
    public PreviewArtifactStoreRejectedException(string message)
        : base(message)
    {
    }
}

internal sealed class PreviewArtifactPendingEntry
{
    private readonly byte[] _bytes;

    public PreviewArtifactPendingEntry(
        string artifactId,
        PreviewArtifactOwnerScope owner,
        string kind,
        string role,
        string pathHint,
        string contentType,
        byte[] bytes,
        string sha256,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        int? width,
        int? height,
        int? channels)
    {
        ArtifactId = artifactId;
        Owner = owner;
        Kind = kind;
        Role = role;
        PathHint = pathHint;
        ContentType = contentType;
        _bytes = bytes.ToArray();
        Sha256 = sha256;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Width = width;
        Height = height;
        Channels = channels;
    }

    public string ArtifactId { get; }
    public PreviewArtifactOwnerScope Owner { get; }
    public string Kind { get; }
    public string Role { get; }
    public string PathHint { get; }
    public string ContentType { get; }
    public string Sha256 { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public int? Width { get; }
    public int? Height { get; }
    public int? Channels { get; }
    public long Length => _bytes.LongLength;

    public PreviewArtifactReferenceV1 ToReference() => new()
    {
        ArtifactId = ArtifactId,
        Kind = Kind,
        Role = Role,
        PathHint = PathHint,
        ContentType = ContentType,
        Length = _bytes.LongLength,
        Sha256 = Sha256,
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = ExpiresAtUtc,
        Width = Width,
        Height = Height,
        Channels = Channels
    };

    public PreviewArtifactEntry ToCommittedEntry()
    {
        var committedBytes = _bytes.ToArray();
        return new PreviewArtifactEntry(
            ArtifactId,
            Owner,
            Kind,
            Role,
            PathHint,
            ContentType,
            committedBytes,
            PreviewArtifactStore.ComputeSha256(committedBytes),
            CreatedAtUtc,
            ExpiresAtUtc,
            Width,
            Height,
            Channels);
    }
}

internal sealed record PreviewArtifactEntry(
    string ArtifactId,
    PreviewArtifactOwnerScope Owner,
    string Kind,
    string Role,
    string PathHint,
    string ContentType,
    byte[] Bytes,
    string Sha256,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int? Width,
    int? Height,
    int? Channels)
{
    public PreviewArtifactReferenceV1 ToReference() => new()
    {
        ArtifactId = ArtifactId,
        Kind = Kind,
        Role = Role,
        PathHint = PathHint,
        ContentType = ContentType,
        Length = Bytes.LongLength,
        Sha256 = Sha256,
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = ExpiresAtUtc,
        Width = Width,
        Height = Height,
        Channels = Channels
    };
}
