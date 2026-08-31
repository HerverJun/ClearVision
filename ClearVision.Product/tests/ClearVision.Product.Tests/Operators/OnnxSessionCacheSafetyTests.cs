using System.Reflection;
using System.Text;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Collection(RuntimeConcurrencyCollection.Name)]
public sealed class OnnxSessionCacheSafetyTests
{
    [Fact]
    public async Task DeepLearningCache_ReplacementAfterInitialObservation_ShouldLoadReplacementSnapshot()
    {
        var sut = new DeepLearningOperator(Substitute.For<ILogger<DeepLearningOperator>>());
        await VerifyReplacementAfterInitialObservationAsync(
            path => AcquireDeepLearningLeaseAsync(sut, path),
            DeepLearningOperator.UnloadModel,
            hook => DeepLearningOperator.InitialModelIdentityObservedForTests = hook,
            () => GetStaticCacheKeys(typeof(DeepLearningOperator), "_modelCache"));
    }

    [Fact]
    public async Task SemanticSegmentationCache_ReplacementAfterInitialObservation_ShouldLoadReplacementSnapshot()
    {
        SemanticSegmentationOperator.ClearSessionCacheForTests();
        var sut = new SemanticSegmentationOperator(Substitute.For<ILogger<SemanticSegmentationOperator>>());

        try
        {
            await VerifyReplacementAfterInitialObservationAsync(
                path => AcquireSemanticSegmentationLeaseAsync(sut, path),
                _ => SemanticSegmentationOperator.ClearSessionCacheForTests(),
                hook => SemanticSegmentationOperator.InitialModelIdentityObservedForTests = hook,
                () => GetStaticCacheKeys(typeof(SemanticSegmentationOperator), "SessionCache"));
        }
        finally
        {
            SemanticSegmentationOperator.InitialModelIdentityObservedForTests = null;
            SemanticSegmentationOperator.ClearSessionCacheForTests();
        }
    }

    [Fact]
    public async Task DeepLearningCache_SameLengthHotReplacement_ShouldUseNewSessionAndRetireOldAfterLastLease()
    {
        var sut = new DeepLearningOperator(Substitute.For<ILogger<DeepLearningOperator>>());
        await VerifyHotReplacementAsync(
            path => AcquireDeepLearningLeaseAsync(sut, path),
            DeepLearningOperator.UnloadModel);
    }

    [Fact]
    public async Task SemanticSegmentationCache_SameLengthHotReplacement_ShouldUseNewSessionAndRetireOldAfterLastLease()
    {
        SemanticSegmentationOperator.ClearSessionCacheForTests();
        var sut = new SemanticSegmentationOperator(Substitute.For<ILogger<SemanticSegmentationOperator>>());

        try
        {
            await VerifyHotReplacementAsync(
                path => AcquireSemanticSegmentationLeaseAsync(sut, path),
                _ => SemanticSegmentationOperator.ClearSessionCacheForTests());
        }
        finally
        {
            SemanticSegmentationOperator.ClearSessionCacheForTests();
        }
    }

    private static async Task VerifyHotReplacementAsync(
        Func<string, Task<ReflectedSessionLease>> acquire,
        Action<string> clearCache)
    {
        var tempDirectory = Directory.CreateTempSubdirectory("onnx-hot-replacement-");
        var canonicalPath = Path.Combine(tempDirectory.FullName, "identity-copy.onnx");
        var aliasPath = Path.Combine(tempDirectory.FullName, "unused-segment", "..", "identity-copy.onnx");
        File.Copy(ResolveIdentityModelPath(), canonicalPath);
        clearCache(canonicalPath);

        ReflectedSessionLease? firstLease = null;
        ReflectedSessionLease? canonicalAliasLease = null;
        ReflectedSessionLease? replacementLease = null;
        try
        {
            var originalLength = new FileInfo(canonicalPath).Length;
            var originalIdentity = OnnxModelFileIdentity.Capture(canonicalPath);

            firstLease = await acquire(aliasPath);
            canonicalAliasLease = await acquire(canonicalPath);

            canonicalAliasLease.Session.Should().BeSameAs(
                firstLease.Session,
                "canonical aliases must resolve to one cache identity");

            ReplaceProducerNameWithoutChangingLength(canonicalPath);
            new FileInfo(canonicalPath).Length.Should().Be(originalLength);
            var replacementIdentity = OnnxModelFileIdentity.Capture(canonicalPath);
            replacementIdentity.ContentSha256.Should().NotBe(originalIdentity.ContentSha256);

            replacementLease = await acquire(canonicalPath);

            replacementLease.Session.Should().NotBeSameAs(
                firstLease.Session,
                "a content hash change at the same canonical path must create a new session");
            firstLease.IsRetired.Should().BeTrue();
            firstLease.IsDisposed.Should().BeFalse("an outstanding old-version lease is still active");

            firstLease.Dispose();
            firstLease.IsDisposed.Should().BeFalse("the canonical-alias lease still owns the old session");

            canonicalAliasLease.Dispose();
            firstLease.IsDisposed.Should().BeTrue("the retired session is disposed after its final lease releases");
            replacementLease.IsRetired.Should().BeFalse();
            replacementLease.IsDisposed.Should().BeFalse();
        }
        finally
        {
            firstLease?.Dispose();
            canonicalAliasLease?.Dispose();
            replacementLease?.Dispose();
            clearCache(canonicalPath);
            tempDirectory.Delete(recursive: true);
        }
    }

    private static async Task VerifyReplacementAfterInitialObservationAsync(
        Func<string, Task<ReflectedSessionLease>> acquire,
        Action<string> clearCache,
        Action<Action<OnnxModelFileIdentity>?> setObservationHook,
        Func<IReadOnlyCollection<string>> snapshotCacheKeys)
    {
        var tempDirectory = Directory.CreateTempSubdirectory("onnx-observation-race-");
        var canonicalPath = Path.Combine(tempDirectory.FullName, "identity-copy.onnx");
        File.Copy(ResolveIdentityModelPath(), canonicalPath);
        clearCache(canonicalPath);

        using var observationReached = new ManualResetEventSlim();
        using var releaseObservation = new ManualResetEventSlim();
        var originalLength = new FileInfo(canonicalPath).Length;
        var originalIdentity = OnnxModelFileIdentity.Capture(canonicalPath);
        OnnxModelFileIdentity? observedIdentity = null;
        ReflectedSessionLease? originalLease = null;
        ReflectedSessionLease? lease = null;
        Task<ReflectedSessionLease>? acquireTask = null;

        // Populate the original cache first. The replacement barrier must prove that a hot-cache
        // lookup cannot return this old session after the path contents change.
        originalLease = await acquire(canonicalPath);

        setObservationHook(identity =>
        {
            observedIdentity = identity;
            observationReached.Set();
            if (!releaseObservation.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Model observation barrier was not released.");
            }
        });

        try
        {
            acquireTask = Task.Run(() => acquire(canonicalPath));
            observationReached.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            observedIdentity.Should().Be(originalIdentity);

            ReplaceProducerNameWithoutChangingLength(canonicalPath);
            new FileInfo(canonicalPath).Length.Should().Be(originalLength);
            var replacementIdentity = OnnxModelFileIdentity.Capture(canonicalPath);
            replacementIdentity.ContentSha256.Should().NotBe(originalIdentity.ContentSha256);

            releaseObservation.Set();
            lease = await acquireTask.WaitAsync(TimeSpan.FromSeconds(10));

            lease.Session.Should().NotBeSameAs(
                originalLease.Session,
                "a new lease must not use the hot cached session after same-path replacement");
            originalLease.IsRetired.Should().BeTrue();
            originalLease.IsDisposed.Should().BeFalse("the original lease is still active");
            lease.Session.ModelMetadata.ProducerName.Should().Be(
                "Clearvision-test-fixture",
                "the session must be constructed from the replacement bytes, not the initially observed file");
            var modelKeys = snapshotCacheKeys()
                .Where(key => key.StartsWith(replacementIdentity.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            modelKeys.Should().ContainSingle();
            modelKeys[0].Should().EndWith(
                $"sha256:{replacementIdentity.ContentSha256}",
                "the cache identity must be derived from the replacement snapshot hash");

            originalLease.Dispose();
            originalLease.IsDisposed.Should().BeTrue(
                "the retired original session is disposed after its final lease releases");
        }
        finally
        {
            releaseObservation.Set();
            setObservationHook(null);
            if (lease == null && acquireTask != null)
            {
                try
                {
                    lease = await acquireTask.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch
                {
                    // Preserve the primary assertion while still releasing the test seam.
                }
            }

            originalLease?.Dispose();
            lease?.Dispose();
            clearCache(canonicalPath);
            tempDirectory.Delete(recursive: true);
        }
    }

    private static IReadOnlyCollection<string> GetStaticCacheKeys(Type ownerType, string fieldName)
    {
        var cache = ownerType
            .GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        return ((IEnumerable<string>)cache.GetType().GetProperty("Keys")!.GetValue(cache)!)
            .ToArray();
    }

    private static async Task<ReflectedSessionLease> AcquireDeepLearningLeaseAsync(
        DeepLearningOperator sut,
        string modelPath)
    {
        var method = typeof(DeepLearningOperator).GetMethod(
            "AcquireModelSessionWithVerifiedExecutionProviderAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return await InvokeLeaseFactoryAsync(
            method!,
            sut,
            [modelPath, false, 0, CancellationToken.None]);
    }

    private static async Task<ReflectedSessionLease> AcquireSemanticSegmentationLeaseAsync(
        SemanticSegmentationOperator sut,
        string modelPath)
    {
        var method = typeof(SemanticSegmentationOperator).GetMethod(
            "GetOrCreateSessionLeaseAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return await InvokeLeaseFactoryAsync(
            method!,
            sut,
            [modelPath, "cpu", CancellationToken.None]);
    }

    private static async Task<ReflectedSessionLease> InvokeLeaseFactoryAsync(
        MethodInfo method,
        object target,
        object?[] arguments)
    {
        var task = method.Invoke(target, arguments).Should().BeAssignableTo<Task>().Subject;
        await task.WaitAsync(TimeSpan.FromSeconds(10));
        var lease = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(task);
        lease.Should().NotBeNull();
        return new ReflectedSessionLease(lease!);
    }

    private static void ReplaceProducerNameWithoutChangingLength(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var marker = Encoding.ASCII.GetBytes("clearvision-test-fixture");
        var markerIndex = bytes.AsSpan().IndexOf(marker);
        markerIndex.Should().BeGreaterThanOrEqualTo(0);
        bytes[markerIndex] = (byte)'C';
        File.WriteAllBytes(path, bytes);
    }

    private static string ResolveIdentityModelPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               (!File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) ||
                !Directory.Exists(Path.Combine(directory.FullName, "ClearVision.Product"))))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new DirectoryNotFoundException("Failed to resolve repository root.");
        }

        return Path.Combine(
            directory.FullName,
            "ClearVision.Product",
            "tests",
            "TestData",
            "model_test_suite",
            "identity_2x2",
            "identity_2x2.onnx");
    }

    private sealed class ReflectedSessionLease : IDisposable
    {
        private readonly object _lease;
        private readonly object _owner;

        public ReflectedSessionLease(object lease)
        {
            _lease = lease;
            var leaseType = lease.GetType();
            Session = leaseType
                .GetProperty("Session", BindingFlags.Instance | BindingFlags.Public)!
                .GetValue(lease)
                .Should()
                .BeOfType<InferenceSession>()
                .Subject;
            _owner = leaseType
                .GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(lease)!;
        }

        public InferenceSession Session { get; }

        public bool IsRetired => ReadOwnerFlag("_disposeRequested");

        public bool IsDisposed => ReadOwnerFlag("_disposed");

        public void Dispose() => ((IDisposable)_lease).Dispose();

        private bool ReadOwnerFlag(string fieldName) =>
            (int)_owner.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(_owner)! != 0;
    }
}
