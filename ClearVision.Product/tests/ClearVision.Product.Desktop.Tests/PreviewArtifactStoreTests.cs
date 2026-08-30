using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Desktop.PreviewArtifacts;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class PreviewArtifactStoreTests
{
    private const string TestUserId = "preview-artifact-test-user";

    [Fact]
    public void Store_CommitsOpaqueImmutableArtifactWithChecksum()
    {
        var clock = new FakePreviewArtifactClock(new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));
        using var store = new PreviewArtifactStore(clock: clock);
        var owner = CreateOwner();
        var source = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };

        PreviewArtifactReferenceV1 reference;
        using (var batch = store.CreateBatch(owner))
        {
            reference = batch.Add("image", "outputImage", "$.Image", "image/png", source);
            reference.ArtifactId.Should().HaveLength(43);
            reference.ArtifactId.Should().MatchRegex("^[A-Za-z0-9_-]+$");
            reference.ArtifactId.Should().NotContain(owner.ProjectId.ToString("N"));
            batch.Commit();
        }

        source[4] = 0xFF;

        store.TryRead(reference.ArtifactId, TestUserId, out var read).Should().BeTrue();
        read!.Bytes.Should().Equal(0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4);
        read.ContentType.Should().Be("image/png");
        read.Length.Should().Be(8);
        read.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(read.Bytes)).ToLowerInvariant());

        read.Bytes[4] = 0xEE;
        store.TryRead(reference.ArtifactId, TestUserId, out var secondRead).Should().BeTrue();
        secondRead!.Bytes.Should().Equal(0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4);
        secondRead.Length.Should().Be(8);
        secondRead.Sha256.Should().Be(read.Sha256);
    }

    [Fact]
    public void Store_ReadAndDeleteRequireMatchingOwnerUser()
    {
        using var store = new PreviewArtifactStore();
        var reference = AddArtifact(store, CreateOwner(), [1, 2, 3]);

        store.TryRead(reference.ArtifactId, "other-user", out _).Should().BeFalse();
        store.Delete(reference.ArtifactId, "other-user").Should().BeFalse();
        store.TryRead(reference.ArtifactId, TestUserId, out var ownerRead).Should().BeTrue();
        ownerRead!.Bytes.Should().Equal(1, 2, 3);
        store.Delete(reference.ArtifactId, TestUserId).Should().BeTrue();
    }

    [Fact]
    public void Store_PublicApiDoesNotExposePendingByteArrays()
    {
        typeof(PreviewArtifactBatch)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Should()
            .NotContain(property => property.Name.Contains("Pending", StringComparison.Ordinal));

        var pendingType = typeof(PreviewArtifactStore).Assembly.GetType(
            "ClearVision.Product.Desktop.PreviewArtifacts.PreviewArtifactPendingEntry");

        pendingType.Should().NotBeNull();
        pendingType!.IsPublic.Should().BeFalse();
        pendingType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Should()
            .NotContain(property => property.PropertyType == typeof(byte[]));
    }

    [Fact]
    public void Store_ExpiresDeletesRevokesAndDisposesArtifacts()
    {
        var clock = new FakePreviewArtifactClock(new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));
        using var store = new PreviewArtifactStore(new PreviewArtifactStoreOptions
        {
            Ttl = TimeSpan.FromMinutes(10),
            MaxEntries = 16,
            MaxTotalBytes = 1024,
            MaxEntryBytes = 512
        }, clock);
        var owner = CreateOwner();
        var first = AddArtifact(store, owner, [1, 2, 3]);
        store.TryRead(first.ArtifactId, TestUserId, out _).Should().BeTrue();

        clock.Advance(TimeSpan.FromMinutes(11));
        store.TryRead(first.ArtifactId, TestUserId, out _).Should().BeFalse();
        store.Count.Should().Be(0);

        var second = AddArtifact(store, owner, [4, 5, 6]);
        store.RevokeOwner(owner).Should().Be(1);
        store.TryRead(second.ArtifactId, TestUserId, out _).Should().BeFalse();

        var third = AddArtifact(store, owner, [7, 8, 9]);
        store.Delete(third.ArtifactId, TestUserId).Should().BeTrue();
        store.TryRead(third.ArtifactId, TestUserId, out _).Should().BeFalse();

        var fourth = AddArtifact(store, owner, [10, 11, 12]);
        store.Dispose();
        store.TryRead(fourth.ArtifactId, TestUserId, out _).Should().BeFalse();
    }

    [Fact]
    public void Store_UsesDeterministicCapacityEvictionAndBatchReplacement()
    {
        var clock = new FakePreviewArtifactClock(new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));
        using var store = new PreviewArtifactStore(new PreviewArtifactStoreOptions
        {
            MaxEntries = 2,
            MaxTotalBytes = 6,
            MaxEntryBytes = 4,
            Ttl = TimeSpan.FromMinutes(10)
        }, clock);
        var owner = CreateOwner();
        var first = AddArtifact(store, owner with { ClientRequestSequence = 1 }, [1, 1, 1]);
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = AddArtifact(store, owner with { ClientRequestSequence = 2 }, [2, 2, 2]);
        clock.Advance(TimeSpan.FromSeconds(1));
        var third = AddArtifact(store, owner with { ClientRequestSequence = 3 }, [3, 3, 3]);

        store.TryRead(first.ArtifactId, TestUserId, out _).Should().BeFalse();
        store.TryRead(second.ArtifactId, TestUserId, out _).Should().BeTrue();
        store.TryRead(third.ArtifactId, TestUserId, out _).Should().BeTrue();

        var replaceOwner = owner with { ClientRequestSequence = 4 };
        var oldForOwner = AddArtifact(store, replaceOwner, [4]);
        var replacement = AddArtifact(store, replaceOwner, [5]);
        store.TryRead(oldForOwner.ArtifactId, TestUserId, out _).Should().BeFalse("a successful new preview replaces old artifacts for the same owner.");
        store.TryRead(replacement.ArtifactId, TestUserId, out _).Should().BeTrue();
    }

    [Fact]
    public void Store_CommitsMultiArtifactBatchAtomicallyWhenCapacityAllows()
    {
        using var store = new PreviewArtifactStore(new PreviewArtifactStoreOptions
        {
            MaxEntries = 2,
            MaxTotalBytes = 16,
            MaxEntryBytes = 8
        });
        var owner = CreateOwner();

        PreviewArtifactReferenceV1 first;
        PreviewArtifactReferenceV1 second;
        using (var batch = store.CreateBatch(owner))
        {
            first = batch.Add("binary", "input", "$.Input", "application/octet-stream", [1, 2, 3]);
            second = batch.Add("binary", "output", "$.Output", "application/octet-stream", [4, 5, 6]);
            batch.Commit();
        }

        store.Count.Should().Be(2);
        store.TryRead(first.ArtifactId, TestUserId, out var firstRead).Should().BeTrue();
        store.TryRead(second.ArtifactId, TestUserId, out var secondRead).Should().BeTrue();
        firstRead!.Bytes.Should().Equal(1, 2, 3);
        secondRead!.Bytes.Should().Equal(4, 5, 6);
    }

    [Fact]
    public void Store_RejectedBatchDoesNotCreateDanglingReferenceOrReplaceOldOwner()
    {
        using var store = new PreviewArtifactStore(new PreviewArtifactStoreOptions
        {
            MaxEntries = 1,
            MaxTotalBytes = 8,
            MaxEntryBytes = 8
        });
        var owner = CreateOwner();
        var existing = AddArtifact(store, owner, [9, 9]);

        PreviewArtifactReferenceV1 pending;
        using (var batch = store.CreateBatch(owner))
        {
            pending = batch.Add("binary", "first", "$.First", "application/octet-stream", [1, 1]);
            batch.Invoking(item => item.Add("binary", "second", "$.Second", "application/octet-stream", [2, 2]))
                .Should()
                .Throw<PreviewArtifactStoreRejectedException>();
        }

        store.Count.Should().Be(1);
        store.TryRead(existing.ArtifactId, TestUserId, out var oldRead).Should().BeTrue();
        oldRead!.Bytes.Should().Equal(9, 9);
        store.TryRead(pending.ArtifactId, TestUserId, out _).Should().BeFalse();
    }

    [Fact]
    public void Store_EvictsOldEntriesForWholeNewBatchWithoutEvictingCurrentBatch()
    {
        var clock = new FakePreviewArtifactClock(new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));
        using var store = new PreviewArtifactStore(new PreviewArtifactStoreOptions
        {
            MaxEntries = 2,
            MaxTotalBytes = 8,
            MaxEntryBytes = 4,
            Ttl = TimeSpan.FromMinutes(10)
        }, clock);
        var owner = CreateOwner();
        var oldFirst = AddArtifact(store, owner with { ClientRequestSequence = 1 }, [1, 1]);
        clock.Advance(TimeSpan.FromSeconds(1));
        var oldSecond = AddArtifact(store, owner with { ClientRequestSequence = 2 }, [2, 2]);
        clock.Advance(TimeSpan.FromSeconds(1));

        PreviewArtifactReferenceV1 newFirst;
        PreviewArtifactReferenceV1 newSecond;
        using (var batch = store.CreateBatch(owner with { ClientRequestSequence = 3 }))
        {
            newFirst = batch.Add("binary", "first", "$.First", "application/octet-stream", [3, 3]);
            newSecond = batch.Add("binary", "second", "$.Second", "application/octet-stream", [4, 4]);
            batch.Commit();
        }

        store.Count.Should().Be(2);
        store.TryRead(oldFirst.ArtifactId, TestUserId, out _).Should().BeFalse();
        store.TryRead(oldSecond.ArtifactId, TestUserId, out _).Should().BeFalse();
        store.TryRead(newFirst.ArtifactId, TestUserId, out _).Should().BeTrue();
        store.TryRead(newSecond.ArtifactId, TestUserId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Store_AllowsConcurrentReadDeleteWithoutPathTokens()
    {
        using var store = new PreviewArtifactStore(new PreviewArtifactStoreOptions
        {
            MaxEntries = 16,
            MaxTotalBytes = 1024,
            MaxEntryBytes = 512
        });
        var reference = AddArtifact(store, CreateOwner(), [1, 2, 3, 4]);

        var tasks = Enumerable.Range(0, 32)
            .Select(index => Task.Run(() =>
            {
                _ = index;
                _ = store.TryRead(reference.ArtifactId, TestUserId, out var ignoredRead);
                _ = ignoredRead;
                _ = store.Delete(reference.ArtifactId, TestUserId);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        PreviewArtifactStore.IsValidArtifactId("../secret").Should().BeFalse();
        PreviewArtifactStore.IsValidArtifactId("C:\\temp\\file").Should().BeFalse();
        store.TryRead(reference.ArtifactId, TestUserId, out _).Should().BeFalse();
    }

    [Fact]
    public void Materializer_CopiesImageWrapperBytesBeforeOriginalRelease()
    {
        using var mat = new OpenCvSharp.Mat(2, 2, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.White);
        var pngBytes = mat.ToBytes(".png");
        var wrapper = ImageWrapper.FromBytes(pngBytes);
        using var store = new PreviewArtifactStore();
        var materializer = new PreviewArtifactMaterializer(store, NullLogger<PreviewArtifactMaterializer>.Instance);

        using var result = materializer.MaterializePreview(
            CreateOwner(),
            new Dictionary<string, object> { ["Image"] = wrapper },
            inputImageBytes: null,
            outputImageBytes: null,
            CancellationToken.None);
        result.Commit();
        wrapper.Release();

        var imageArtifact = result.Artifacts.Should().ContainSingle().Subject;
        store.TryRead(imageArtifact.ArtifactId, TestUserId, out var read).Should().BeTrue();
        read!.Bytes.Should().NotBeEmpty();
        read.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void Materializer_RollsBackCanceledBatchAndDowngradesOversizeArtifacts()
    {
        using var store = new PreviewArtifactStore(new PreviewArtifactStoreOptions
        {
            MaxEntries = 16,
            MaxTotalBytes = 16,
            MaxEntryBytes = 4
        });
        var materializer = new PreviewArtifactMaterializer(store, NullLogger<PreviewArtifactMaterializer>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => materializer.MaterializePreview(
            CreateOwner(),
            new Dictionary<string, object> { ["Image"] = new byte[] { 1, 2, 3 } },
            inputImageBytes: null,
            outputImageBytes: null,
            cts.Token);
        act.Should().Throw<OperationCanceledException>();
        store.Count.Should().Be(0);

        using var result = materializer.MaterializePreview(
            CreateOwner() with { ClientRequestSequence = 2 },
            new Dictionary<string, object> { ["Mask"] = new byte[] { 1, 2, 3, 4, 5 } },
            inputImageBytes: null,
            outputImageBytes: null,
            CancellationToken.None);
        result.Artifacts.Should().BeEmpty();
        result.Diagnostics.Should().Contain(item => item.Contains("PreviewArtifactRejected", StringComparison.Ordinal));
        store.Count.Should().Be(0);
    }

    [Fact]
    public void Materializer_MaterializesPositionPointListsAsBoundedPointSetArtifacts()
    {
        using var store = new PreviewArtifactStore();
        var materializer = new PreviewArtifactMaterializer(store, NullLogger<PreviewArtifactMaterializer>.Instance);
        var points = Enumerable.Range(0, 80)
            .Select(index => new Position(10.5 + index, 20.25 + index))
            .ToList();

        using var result = materializer.MaterializePreview(
            CreateOwner(),
            new Dictionary<string, object> { ["EdgePoints"] = points },
            inputImageBytes: null,
            outputImageBytes: null,
            CancellationToken.None);
        result.Commit();

        var artifact = result.Artifacts.Should().ContainSingle().Subject;
        artifact.Kind.Should().Be("pointSet");
        artifact.Role.Should().Be("pointSet");
        artifact.PathHint.Should().Be("$.\"EdgePoints\"");
        artifact.ContentType.Should().Be("application/json");

        var value = result.OutputData["EdgePoints"].Should().BeOfType<PreviewArtifactValue>().Subject;
        value.Kind.Should().Be("pointSet");
        value.Artifact.Should().BeSameAs(artifact);
        value.Metadata.Should().Contain("count", "80");
        value.Metadata.Should().Contain("itemKind", "point");

        store.TryRead(artifact.ArtifactId, TestUserId, out var read).Should().BeTrue();
        var json = Encoding.UTF8.GetString(read!.Bytes);
        json.Should().Contain("\"x\":10.5");
        json.Should().Contain("\"y\":20.25");
        json.Should().Contain("\"x\":89.5");
        json.Should().Contain("\"y\":99.25");
    }

    [Fact]
    public void Materializer_MaterializesCaliperProfileEvidenceAsBoundedProfileArtifact()
    {
        using var store = new PreviewArtifactStore();
        var materializer = new PreviewArtifactMaterializer(store, NullLogger<PreviewArtifactMaterializer>.Instance);
        var profiles = Enumerable.Range(0, CircleCaliperFitV2Request.MaxProfileEvidenceCount)
            .Select(index => new CircleCaliperFitV2ProfileEvidence(
                CircleCaliperFitV2ProfileEvidence.ContractVersionValue,
                index * 4,
                index * 15.0,
                10,
                20,
                30,
                40,
                257,
                5,
                4.5,
                32.0,
                19.0,
                "LightToDark",
                Enumerable.Range(0, CircleCaliperFitV2Request.MaxProfileEvidenceSamplesPerProfile).Select(sample => (double)sample).ToArray()))
            .ToArray();

        using var result = materializer.MaterializePreview(
            CreateOwner(),
            new Dictionary<string, object> { ["CaliperProfileEvidence"] = profiles },
            inputImageBytes: null,
            outputImageBytes: null,
            CancellationToken.None);
        result.Commit();

        var artifact = result.Artifacts.Should().ContainSingle().Subject;
        artifact.Kind.Should().Be("profile");
        artifact.Role.Should().Be("profile");
        artifact.ContentType.Should().Be("application/json");
        artifact.Length.Should().BeLessThan(CircleCaliperFitV2Request.MaxProfileEvidenceArtifactBytes);

        var value = result.OutputData["CaliperProfileEvidence"].Should().BeOfType<PreviewArtifactValue>().Subject;
        value.Kind.Should().Be("profile");
        value.Artifact.Should().BeSameAs(artifact);
        value.Metadata.Should().Contain("count", CircleCaliperFitV2Request.MaxProfileEvidenceCount.ToString());
        value.Metadata.Should().Contain("itemKind", "profile");

        store.TryRead(artifact.ArtifactId, TestUserId, out var read).Should().BeTrue();
        var json = Encoding.UTF8.GetString(read!.Bytes);
        json.Should().Contain(CircleCaliperFitV2ProfileEvidence.ContractVersionValue);
        json.Should().Contain("\"caliperIndex\":0");
        json.Should().Contain("\"caliperIndex\":92");
    }

    [Fact]
    public void Materializer_CancelDoesNotReplaceExistingOwnerArtifacts()
    {
        using var store = new PreviewArtifactStore(new PreviewArtifactStoreOptions
        {
            MaxEntries = 16,
            MaxTotalBytes = 1024,
            MaxEntryBytes = 512
        });
        var owner = CreateOwner();
        var existing = AddArtifact(store, owner, [7, 7, 7]);
        var materializer = new PreviewArtifactMaterializer(store, NullLogger<PreviewArtifactMaterializer>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => materializer.MaterializePreview(
            owner,
            new Dictionary<string, object> { ["Profile"] = Enumerable.Range(0, 128).ToList() },
            inputImageBytes: null,
            outputImageBytes: null,
            cts.Token);

        act.Should().Throw<OperationCanceledException>();
        store.Count.Should().Be(1);
        store.TryRead(existing.ArtifactId, TestUserId, out var read).Should().BeTrue();
        read!.Bytes.Should().Equal(7, 7, 7);
    }

    [Fact]
    public void Store_ScopedReadRequiresUserProjectKindAndRevalidatesContentHash()
    {
        using var store = new PreviewArtifactStore();
        var owner = CreateOwner();
        var reference = AddArtifact(store, owner, [1, 2, 3]);

        store.TryReadScoped(
                reference.ArtifactId,
                owner.UserId,
                owner.ProjectId,
                "binary",
                out var valid)
            .Should()
            .BeTrue();
        valid!.Bytes.Should().Equal(1, 2, 3);
        store.TryReadScoped(reference.ArtifactId, "wrong-user", owner.ProjectId, "binary", out _)
            .Should()
            .BeFalse();
        store.TryReadScoped(reference.ArtifactId, owner.UserId, Guid.NewGuid(), "binary", out _)
            .Should()
            .BeFalse();
        store.TryReadScoped(reference.ArtifactId, owner.UserId, owner.ProjectId, "wrong-kind", out _)
            .Should()
            .BeFalse();

        var entriesField = typeof(PreviewArtifactStore).GetField(
            "_entries",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var entries = (Dictionary<string, PreviewArtifactEntry>)entriesField.GetValue(store)!;
        entries[reference.ArtifactId].Bytes[0] = 0xFF;

        store.TryReadScoped(
                reference.ArtifactId,
                owner.UserId,
                owner.ProjectId,
                "binary",
                out _)
            .Should()
            .BeFalse();
        store.Count.Should().Be(0);
    }

    private static PreviewArtifactReferenceV1 AddArtifact(
        PreviewArtifactStore store,
        PreviewArtifactOwnerScope owner,
        byte[] bytes)
    {
        using var batch = store.CreateBatch(owner);
        var reference = batch.Add("binary", "resource", "$.Data", "application/octet-stream", bytes);
        batch.Commit();
        return reference;
    }

    private static PreviewArtifactOwnerScope CreateOwner() =>
        new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            1,
            2,
            TestUserId);

    private sealed class FakePreviewArtifactClock : IPreviewArtifactClock
    {
        public FakePreviewArtifactClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan value)
        {
            UtcNow = UtcNow.Add(value);
        }
    }
}
