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

    public bool TryRead(string? artifactId, out PreviewArtifactReadResult? result)
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

            if (IsExpired(entry))
            {
                RemoveEntryUnderLock(artifactId!);
                return false;
            }

            result = new PreviewArtifactReadResult(entry.ToReference(), entry.Bytes.ToArray());
            return true;
        }
    }

    public bool Delete(string? artifactId)
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
        var sha256 = Convert.ToHexString(SHA256.HashData(clonedBytes)).ToLowerInvariant();
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
        lock (_gate)
        {
            ThrowIfDisposedUnderLock();
            CleanupExpiredUnderLock();

            var keepIds = pendingEntries.Select(entry => entry.ArtifactId).ToHashSet(StringComparer.Ordinal);
            RemoveOwnerUnderLock(owner, keepIds);

            foreach (var pending in pendingEntries)
            {
                var entry = new PreviewArtifactEntry(
                    pending.ArtifactId,
                    pending.Owner,
                    pending.Kind,
                    pending.Role,
                    pending.PathHint,
                    pending.ContentType,
                    pending.Bytes,
                    pending.Sha256,
                    pending.CreatedAtUtc,
                    pending.ExpiresAtUtc,
                    pending.Width,
                    pending.Height,
                    pending.Channels);
                _entries[entry.ArtifactId] = entry;
                if (!_ownerIndex.TryGetValue(owner, out var ownerIds))
                {
                    ownerIds = new HashSet<string>(StringComparer.Ordinal);
                    _ownerIndex[owner] = ownerIds;
                }

                ownerIds.Add(entry.ArtifactId);
                _totalBytes += entry.Bytes.LongLength;
            }

            TrimCapacityUnderLock();
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

    private void TrimCapacityUnderLock()
    {
        while (_entries.Count > _options.MaxEntries || _totalBytes > _options.MaxTotalBytes)
        {
            var candidate = _entries.Values
                .OrderBy(entry => entry.CreatedAtUtc)
                .ThenBy(entry => entry.ArtifactId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate == null)
            {
                return;
            }

            RemoveEntryUnderLock(candidate.ArtifactId);
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

    public IReadOnlyList<PreviewArtifactPendingEntry> PendingEntries => _pendingEntries;

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

public sealed record PreviewArtifactPendingEntry(
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
