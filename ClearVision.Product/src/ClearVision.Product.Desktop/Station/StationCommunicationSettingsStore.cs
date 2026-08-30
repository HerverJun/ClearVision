using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Runtime.Abstractions;

namespace ClearVision.Product.Desktop.Station;

public enum StationCommunicationMode
{
    Disabled = 0,
    LocalLoopback = 1,
    LanController = 2
}

internal enum StationCommunicationPersistenceStage
{
    OperationEntered,
    AuthoritativeRead,
    AuthoritativeSnapshotRead,
    CandidateStarted,
    StudioCandidateWrite,
    StationCandidateWrite,
    CandidateRead,
    PreviousGenerationPrepared,
    CommitIntentWrite,
    CommitIntended,
    StudioPublish,
    StudioPublished,
    StationPublish,
    StationPublished,
    CommitCompleteWrite,
    CommitCompleted,
    RecoveryStarted,
    RecoveryPublish,
    RecoveryRollback,
    RecoveryCommit,
    RecoveryCompleted
}

internal interface IStationCommunicationPersistenceFaultInjector
{
    void OnStage(StationCommunicationPersistenceStage stage, string generationId);
}

internal sealed class NoOpStationCommunicationPersistenceFaultInjector :
    IStationCommunicationPersistenceFaultInjector
{
    public static NoOpStationCommunicationPersistenceFaultInjector Instance { get; } = new();

    private NoOpStationCommunicationPersistenceFaultInjector()
    {
    }

    public void OnStage(StationCommunicationPersistenceStage stage, string generationId)
    {
    }
}

/// <summary>
/// Test-only signal for an abrupt process stop. It deliberately leaves a durable intent marker
/// and generation candidates for a new store instance to recover.
/// </summary>
internal sealed class StationCommunicationPersistenceInterruptionException : IOException
{
    public StationCommunicationPersistenceInterruptionException(string stage)
        : base($"Simulated Station communication persistence interruption at {stage}.")
    {
    }
}

public sealed class StationCommunicationSettingsStore
{
    private const int GeneratedTokenUpperBound = 1_000_000;
    private const int CommitMarkerSchemaVersion = 1;
    private const string StudioCandidateFileName = "studio.candidate.json";
    private const string StationCandidateFileName = "station.candidate.json";
    private const string PreviousStudioFileName = "studio.previous.json";
    private const string PreviousStationFileName = "station.previous.json";
    private const string PreviousMarkerFileName = "marker.previous.json";

    private static readonly ConcurrentDictionary<string, object> OperationGates =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    static StationCommunicationSettingsStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private readonly IStationCommunicationPersistenceFaultInjector _faultInjector;
    private readonly object _operationGate;

    public StationCommunicationSettingsStore()
        : this(null)
    {
    }

    public StationCommunicationSettingsStore(string? localAppDataRoot)
        : this(
            StationSettingsPaths.GetStudioCommunicationSettingsPath(localAppDataRoot),
            StationSettingsPaths.GetStationSyncSettingsPath(localAppDataRoot))
    {
    }

    public StationCommunicationSettingsStore(string studioSettingsPath, string stationSyncSettingsPath)
        : this(studioSettingsPath, stationSyncSettingsPath, NoOpStationCommunicationPersistenceFaultInjector.Instance)
    {
    }

    internal StationCommunicationSettingsStore(
        string studioSettingsPath,
        string stationSyncSettingsPath,
        IStationCommunicationPersistenceFaultInjector faultInjector)
    {
        if (string.IsNullOrWhiteSpace(studioSettingsPath))
        {
            throw new ArgumentException("Studio settings path is required.", nameof(studioSettingsPath));
        }

        if (string.IsNullOrWhiteSpace(stationSyncSettingsPath))
        {
            throw new ArgumentException("Station Sync settings path is required.", nameof(stationSyncSettingsPath));
        }

        ArgumentNullException.ThrowIfNull(faultInjector);

        StudioSettingsPath = Path.GetFullPath(studioSettingsPath);
        StationSyncSettingsPath = Path.GetFullPath(stationSyncSettingsPath);
        StationSyncAppliedMarkerPath = Path.Combine(
            Path.GetDirectoryName(StationSyncSettingsPath) ?? string.Empty,
            StationSettingsPaths.StationSyncSettingsAppliedMarkerFileName);
        CommitMarkerPath = StudioSettingsPath + ".station-communication.commit.json";
        TransactionRootPath = StudioSettingsPath + ".station-communication.generations";
        _operationGate = GetOperationGate(StudioSettingsPath, StationSyncSettingsPath);
        _faultInjector = faultInjector;
    }

    public string StudioSettingsPath { get; }

    public string StationSyncSettingsPath { get; }

    public string StationSyncAppliedMarkerPath { get; }

    public string CommitMarkerPath { get; }

    public string TransactionRootPath { get; }

    public StationCommunicationSettingsView GetSettings(StationIngressOptions runningIngress)
    {
        lock (_operationGate)
        {
            try
            {
                Checkpoint(StationCommunicationPersistenceStage.OperationEntered, string.Empty);
                RecoverIfNeededNoLock();
                var snapshot = ReadSnapshotNoLock(runningIngress);
                return BuildView(snapshot, runningIngress, null, null, "Station communication settings loaded.");
            }
            catch (Exception ex)
            {
                throw ToPersistenceException(StationCommunicationPersistenceStage.AuthoritativeRead, ex);
            }
        }
    }

    public StationCommunicationSaveResult SaveSettings(
        StationCommunicationSettingsUpdateRequest request,
        StationIngressOptions runningIngress)
    {
        lock (_operationGate)
        {
            try
            {
                Checkpoint(StationCommunicationPersistenceStage.OperationEntered, string.Empty);
                RecoverIfNeededNoLock();
                return SaveSettingsNoLock(request, runningIngress);
            }
            catch (Exception ex)
            {
                return BuildSaveFailure(RecoverAfterMutationFailureNoLock(ToPersistenceException(
                    StationCommunicationPersistenceStage.AuthoritativeRead,
                    ex)));
            }
        }
    }

    private StationCommunicationSaveResult SaveSettingsNoLock(
        StationCommunicationSettingsUpdateRequest request,
        StationIngressOptions runningIngress)
    {
        var snapshot = ReadSnapshotNoLock(runningIngress);
        if (!TryBuildTarget(request, snapshot, out var target, out var errors))
        {
            return StationCommunicationSaveResult.Failed("Station communication settings are invalid.", errors);
        }

        var generationId = Guid.NewGuid().ToString("N");
        target.StudioDocument.GenerationId = generationId;
        target.StationSyncDocument.GenerationId = generationId;

        var requiresStudioRestart = !AreIngressOptionsEquivalent(target.Ingress, runningIngress);
        var requiresLocalStationRestart = !AreStationSyncOptionsEquivalent(
            target.StationSync,
            snapshot.StationSync ?? new LocalStationSyncOptions());

        CommitGenerationNoLock(generationId, target, snapshot);

        var savedSnapshot = ReadSnapshotNoLock(runningIngress);
        if (!string.Equals(savedSnapshot.GenerationId, generationId, StringComparison.Ordinal))
        {
            throw CreatePersistenceException(
                StationCommunicationPersistenceStage.CommitCompleteWrite,
                new InvalidDataException("The published Station communication generation could not be verified."));
        }

        var view = BuildView(
            savedSnapshot,
            runningIngress,
            requiresStudioRestart,
            requiresLocalStationRestart,
            "Station communication settings saved. Restart Studio or local Station where indicated.");
        return StationCommunicationSaveResult.Succeeded(view);
    }

    public StationCommunicationTokenResult RevealToken(StationIngressOptions runningIngress)
    {
        lock (_operationGate)
        {
            try
            {
                Checkpoint(StationCommunicationPersistenceStage.OperationEntered, string.Empty);
                RecoverIfNeededNoLock();
                var snapshot = ReadSnapshotNoLock(runningIngress);
                var token = ResolveToken(snapshot);
                return new StationCommunicationTokenResult
                {
                    Success = true,
                    Operation = "reveal",
                    Token = token,
                    TokenInfo = BuildTokenInfo(token),
                    Settings = BuildView(snapshot, runningIngress, null, null, "Station token revealed.")
                };
            }
            catch (Exception ex)
            {
                return BuildTokenFailure(
                    "reveal",
                    ToPersistenceException(StationCommunicationPersistenceStage.AuthoritativeRead, ex));
            }
        }
    }

    public StationCommunicationTokenResult RegenerateToken(StationIngressOptions runningIngress)
    {
        lock (_operationGate)
        {
            try
            {
                Checkpoint(StationCommunicationPersistenceStage.OperationEntered, string.Empty);
                RecoverIfNeededNoLock();
                var snapshot = ReadSnapshotNoLock(runningIngress);
                var generatedToken = GenerateToken(ResolveToken(snapshot));
                var mode = InferMode(snapshot.Ingress, snapshot.Metadata);
                var request = new StationCommunicationSettingsUpdateRequest
                {
                    Mode = mode.ToString(),
                    Port = snapshot.Ingress.Port,
                    LanHost = snapshot.Metadata.LanHost,
                    LocalStationSyncEnabled = snapshot.StationSync?.Enabled ?? mode != StationCommunicationMode.Disabled,
                    SharedToken = generatedToken
                };

                var saveResult = SaveSettingsNoLock(request, runningIngress);
                if (!saveResult.Success)
                {
                    return new StationCommunicationTokenResult
                    {
                        Success = false,
                        Operation = "regenerate",
                        Message = saveResult.Message,
                        PublicMessage = saveResult.PublicMessage,
                        Errors = saveResult.Errors,
                        ErrorCode = saveResult.ErrorCode,
                        Stage = saveResult.Stage,
                        Retryable = saveResult.Retryable
                    };
                }

                return new StationCommunicationTokenResult
                {
                    Success = true,
                    Operation = "regenerate",
                    Token = generatedToken,
                    TokenInfo = BuildTokenInfo(generatedToken),
                    Settings = saveResult.Settings,
                    Message = saveResult.Message
                };
            }
            catch (Exception ex)
            {
                var failure = RecoverAfterMutationFailureNoLock(ToPersistenceException(
                    StationCommunicationPersistenceStage.AuthoritativeRead,
                    ex));
                return BuildTokenFailure("regenerate", failure);
            }
        }
    }

    private PersistedStationCommunicationSnapshot ReadSnapshotNoLock(StationIngressOptions runningIngress)
    {
        var studioDocument = ReadStudioDocumentNoLock();
        var stationSyncDocument = ReadStationSyncDocumentNoLock();
        var generationId = ValidatePublishedGeneration(studioDocument, stationSyncDocument);
        var ingress = CloneIngress(studioDocument.StationIngress ?? runningIngress);
        var metadata = studioDocument.StationCommunication ?? BuildMetadata(InferMode(ingress, null), null, null);
        var stationSync = stationSyncDocument.StationSync == null
            ? null
            : CloneStationSync(stationSyncDocument.StationSync);

        Checkpoint(StationCommunicationPersistenceStage.AuthoritativeSnapshotRead, generationId ?? string.Empty);
        return new PersistedStationCommunicationSnapshot(
            studioDocument,
            stationSyncDocument,
            metadata,
            ingress,
            stationSync,
            generationId);
    }

    private bool TryBuildTarget(
        StationCommunicationSettingsUpdateRequest request,
        PersistedStationCommunicationSnapshot snapshot,
        out StationCommunicationTarget target,
        out IReadOnlyList<StationCommunicationValidationError> errors)
    {
        var validationErrors = new List<StationCommunicationValidationError>();
        target = default!;

        if (!TryParseMode(request.Mode, out var mode))
        {
            validationErrors.Add(new StationCommunicationValidationError("mode", "Mode must be Disabled, LocalLoopback, or LanController."));
        }

        var requestedPort = request.Port ?? snapshot.Ingress.Port;
        if (requestedPort is < 1 or > 65535)
        {
            validationErrors.Add(new StationCommunicationValidationError("port", "Port must be between 1 and 65535."));
        }

        if (validationErrors.Count > 0)
        {
            errors = validationErrors;
            return false;
        }

        var port = requestedPort <= 0 ? 5000 : requestedPort;
        if (!TryNormalizeLanHost(request.LanHost, snapshot.Metadata.LanHost, out var lanHost, out var lanHostError))
        {
            errors = new[]
            {
                new StationCommunicationValidationError("lanHost", lanHostError)
            };
            return false;
        }
        var localStationSyncEnabled = mode != StationCommunicationMode.Disabled &&
            (request.LocalStationSyncEnabled ?? snapshot.StationSync?.Enabled ?? true);
        var token = !string.IsNullOrWhiteSpace(request.SharedToken)
            ? request.SharedToken.Trim()
            : ResolveToken(snapshot);

        if (mode != StationCommunicationMode.Disabled && string.IsNullOrWhiteSpace(token))
        {
            token = GenerateToken();
        }

        var ingress = CloneIngress(snapshot.Ingress);
        ingress.Enabled = mode != StationCommunicationMode.Disabled;
        ingress.ListenMode = mode == StationCommunicationMode.LanController
            ? StationIngressListenMode.Lan
            : StationIngressListenMode.Loopback;
        ingress.Port = port;
        ingress.SharedToken = token;
        ingress.AllowInsecureDevelopment = false;

        var stationSync = CloneStationSync(snapshot.StationSync ?? new LocalStationSyncOptions());
        stationSync.Enabled = localStationSyncEnabled;
        stationSync.StudioBaseUrl = mode == StationCommunicationMode.Disabled
            ? string.Empty
            : $"http://127.0.0.1:{port}";
        stationSync.StudioHubUrl = string.Empty;
        stationSync.SharedToken = token;

        var metadata = BuildMetadata(mode, lanHost, localStationSyncEnabled);
        var studioDocument = new StudioStationCommunicationSettingsDocument
        {
            StationCommunication = metadata,
            StationIngress = ingress
        };
        var stationSyncDocument = new StationSyncSettingsDocument
        {
            StationSync = stationSync
        };

        target = new StationCommunicationTarget(studioDocument, stationSyncDocument, ingress, stationSync);
        errors = Array.Empty<StationCommunicationValidationError>();
        return true;
    }

    private StationCommunicationSettingsView BuildView(
        PersistedStationCommunicationSnapshot snapshot,
        StationIngressOptions runningIngress,
        bool? requiresStudioRestart,
        bool? requiresLocalStationRestart,
        string message)
    {
        var mode = InferMode(snapshot.Ingress, snapshot.Metadata);
        var port = snapshot.Ingress.Port <= 0 ? 5000 : snapshot.Ingress.Port;
        var lanAddresses = DiscoverLanAddresses();
        var lanHost = NormalizeLanHost(snapshot.Metadata.LanHost, lanAddresses.FirstOrDefault());
        var localStationSyncEnabled = snapshot.StationSync?.Enabled ?? mode != StationCommunicationMode.Disabled;
        var token = ResolveToken(snapshot);
        var remoteBaseUrl = mode == StationCommunicationMode.LanController
            ? $"http://{FormatHostForUrl(lanHost)}:{port}"
            : string.Empty;
        var localBaseUrl = mode == StationCommunicationMode.Disabled
            ? string.Empty
            : $"http://127.0.0.1:{port}";
        var studioRestart = requiresStudioRestart ?? !AreIngressOptionsEquivalent(snapshot.Ingress, runningIngress);
        var stationRestart = requiresLocalStationRestart ?? IsStationSyncRestartRequired();

        return new StationCommunicationSettingsView
        {
            Success = true,
            Message = message,
            GenerationId = snapshot.GenerationId ?? string.Empty,
            Mode = mode.ToString(),
            Port = port,
            LanHost = lanHost,
            LanAddresses = lanAddresses,
            LocalStationSyncEnabled = localStationSyncEnabled,
            Token = BuildTokenInfo(token),
            Paths = new StationCommunicationPathView
            {
                Studio = StudioSettingsPath,
                LocalStation = StationSyncSettingsPath
            },
            CurrentRunning = new StationCommunicationRunningView
            {
                StudioEnabled = runningIngress.Enabled,
                StudioListenMode = runningIngress.ListenMode.ToString(),
                StudioPort = runningIngress.Port,
                StudioToken = BuildTokenInfo(runningIngress.SharedToken)
            },
            RequiresRestart = new StationCommunicationRestartView
            {
                Studio = studioRestart,
                LocalStation = stationRestart
            },
            LocalStationBaseUrl = localBaseUrl,
            RemoteStationBaseUrl = remoteBaseUrl,
            RemoteStationHubUrl = string.IsNullOrWhiteSpace(remoteBaseUrl)
                ? string.Empty
                : remoteBaseUrl.TrimEnd('/') + StationSyncContractDefaults.HubPath,
            LocalStationHubUrl = string.IsNullOrWhiteSpace(localBaseUrl)
                ? string.Empty
                : localBaseUrl.TrimEnd('/') + StationSyncContractDefaults.HubPath,
            Diagnostics = BuildDiagnostics(mode, token, studioRestart, stationRestart, remoteBaseUrl)
        };
    }

    private StudioStationCommunicationSettingsDocument ReadStudioDocumentNoLock()
    {
        return ReadDocumentNoLock(
            StudioSettingsPath,
            static () => new StudioStationCommunicationSettingsDocument());
    }

    private StationSyncSettingsDocument ReadStationSyncDocumentNoLock()
    {
        return ReadDocumentNoLock(
            StationSyncSettingsPath,
            static () => new StationSyncSettingsDocument());
    }

    private T ReadDocumentNoLock<T>(string path, Func<T> createMissing)
        where T : class
    {
        if (!File.Exists(path))
        {
            return createMissing();
        }

        return RunPersistenceStage(
            StationCommunicationPersistenceStage.AuthoritativeRead,
            string.Empty,
            () =>
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<T>(json, JsonOptions)
                    ?? throw new InvalidDataException("Station communication configuration contains a null JSON document.");
            });
    }

    // This is a recoverable two-file publish protocol, not a claim of power-loss atomicity across
    // two filesystem entries. The durable intent plus old/new bundles lets the next authority entry
    // converge the pair to one complete generation before it is returned to a caller.
    private void CommitGenerationNoLock(
        string generationId,
        StationCommunicationTarget target,
        PersistedStationCommunicationSnapshot previousSnapshot)
    {
        var transactionDirectory = GetTransactionDirectory(generationId);
        RunPersistenceStage(
            StationCommunicationPersistenceStage.CandidateStarted,
            generationId,
            () =>
            {
                Directory.CreateDirectory(transactionDirectory);
            });

        var studioCandidatePath = Path.Combine(transactionDirectory, StudioCandidateFileName);
        var stationCandidatePath = Path.Combine(transactionDirectory, StationCandidateFileName);
        var studioCandidateBytes = JsonSerializer.SerializeToUtf8Bytes(target.StudioDocument, JsonOptions);
        var stationCandidateBytes = JsonSerializer.SerializeToUtf8Bytes(target.StationSyncDocument, JsonOptions);

        RunPersistenceStage(
            StationCommunicationPersistenceStage.StudioCandidateWrite,
            generationId,
            () => WriteAllBytesDurable(studioCandidatePath, studioCandidateBytes));
        RunPersistenceStage(
            StationCommunicationPersistenceStage.StationCandidateWrite,
            generationId,
            () => WriteAllBytesDurable(stationCandidatePath, stationCandidateBytes));
        ValidateCandidateDocuments(studioCandidateBytes, stationCandidateBytes, generationId);

        var previousStudio = CaptureFileNoLock(StudioSettingsPath, generationId);
        var previousStation = CaptureFileNoLock(StationSyncSettingsPath, generationId);
        var previousMarker = CaptureFileNoLock(CommitMarkerPath, generationId);
        PersistPreviousFileNoLock(
            transactionDirectory,
            PreviousStudioFileName,
            previousStudio,
            generationId);
        PersistPreviousFileNoLock(
            transactionDirectory,
            PreviousStationFileName,
            previousStation,
            generationId);
        PersistPreviousFileNoLock(
            transactionDirectory,
            PreviousMarkerFileName,
            previousMarker,
            generationId);
        Checkpoint(StationCommunicationPersistenceStage.PreviousGenerationPrepared, generationId);

        var marker = new StationCommunicationCommitMarker
        {
            SchemaVersion = CommitMarkerSchemaVersion,
            State = StationCommunicationCommitState.CommitIntended,
            GenerationId = generationId,
            PreviousGenerationId = previousSnapshot.GenerationId ?? string.Empty,
            StudioSha256 = ComputeSha256(studioCandidateBytes),
            StationSha256 = ComputeSha256(stationCandidateBytes),
            PreviousStudioExists = previousStudio.Exists,
            PreviousStudioSha256 = previousStudio.Exists ? ComputeSha256(previousStudio.Bytes!) : string.Empty,
            PreviousStudioLastWriteUtc = previousStudio.LastWriteUtc,
            PreviousStationExists = previousStation.Exists,
            PreviousStationSha256 = previousStation.Exists ? ComputeSha256(previousStation.Bytes!) : string.Empty,
            PreviousStationLastWriteUtc = previousStation.LastWriteUtc,
            PreviousMarkerExists = previousMarker.Exists,
            PreviousMarkerSha256 = previousMarker.Exists ? ComputeSha256(previousMarker.Bytes!) : string.Empty,
            PreparedAtUtc = DateTimeOffset.UtcNow
        };

        PersistCommitMarkerNoLock(
            marker,
            StationCommunicationPersistenceStage.CommitIntentWrite,
            injectFault: true);
        Checkpoint(StationCommunicationPersistenceStage.CommitIntended, generationId);

        PublishCandidateNoLock(
            studioCandidatePath,
            marker.StudioSha256,
            StudioSettingsPath,
            generationId,
            StationCommunicationPersistenceStage.StudioPublish,
            injectFault: true);
        Checkpoint(StationCommunicationPersistenceStage.StudioPublished, generationId);
        PublishCandidateNoLock(
            stationCandidatePath,
            marker.StationSha256,
            StationSyncSettingsPath,
            generationId,
            StationCommunicationPersistenceStage.StationPublish,
            injectFault: true);
        Checkpoint(StationCommunicationPersistenceStage.StationPublished, generationId);

        EnsureTargetGenerationPublished(marker);
        marker.State = StationCommunicationCommitState.Committed;
        marker.CommittedAtUtc = DateTimeOffset.UtcNow;
        PersistCommitMarkerNoLock(
            marker,
            StationCommunicationPersistenceStage.CommitCompleteWrite,
            injectFault: true);
        Checkpoint(StationCommunicationPersistenceStage.CommitCompleted, generationId);
        CleanupTransactionDirectoriesNoThrow(generationId, previousSnapshot.GenerationId);
    }

    private void RecoverIfNeededNoLock()
    {
        var marker = ReadCommitMarkerNoLock(injectFault: true);
        if (marker == null)
        {
            CleanupTransactionDirectoriesNoThrow();
            return;
        }

        if (TargetGenerationIsPublished(marker))
        {
            if (marker.State == StationCommunicationCommitState.CommitIntended)
            {
                Checkpoint(StationCommunicationPersistenceStage.RecoveryStarted, marker.GenerationId);
                marker.State = StationCommunicationCommitState.Committed;
                marker.CommittedAtUtc = DateTimeOffset.UtcNow;
                PersistCommitMarkerNoLock(
                    marker,
                    StationCommunicationPersistenceStage.RecoveryCommit,
                    injectFault: true);
                Checkpoint(StationCommunicationPersistenceStage.RecoveryCompleted, marker.GenerationId);
            }

            CleanupTransactionDirectoriesNoThrow(marker.GenerationId, marker.PreviousGenerationId);
            return;
        }

        Checkpoint(StationCommunicationPersistenceStage.RecoveryStarted, marker.GenerationId);
        try
        {
            RollForwardGenerationNoLock(marker, injectFault: true);
        }
        catch (Exception forwardFailure)
        {
            var persistenceFailure = ToPersistenceException(
                StationCommunicationPersistenceStage.RecoveryPublish,
                forwardFailure);
            if (persistenceFailure.Interruption)
            {
                throw persistenceFailure;
            }

            try
            {
                RestorePreviousGenerationNoLock(marker, injectFault: false);
            }
            catch (Exception rollbackFailure)
            {
                throw CreatePersistenceException(
                    StationCommunicationPersistenceStage.RecoveryPublish,
                    new AggregateException(forwardFailure, rollbackFailure));
            }
        }

        Checkpoint(StationCommunicationPersistenceStage.RecoveryCompleted, marker.GenerationId);
    }

    private void RollForwardGenerationNoLock(
        StationCommunicationCommitMarker marker,
        bool injectFault)
    {
        var transactionDirectory = GetTransactionDirectory(marker.GenerationId);
        var studioCandidatePath = Path.Combine(transactionDirectory, StudioCandidateFileName);
        var stationCandidatePath = Path.Combine(transactionDirectory, StationCandidateFileName);
        var studioBytes = ReadAndValidateCandidateNoLock(
            studioCandidatePath,
            marker.StudioSha256,
            marker.GenerationId,
            injectFault);
        var stationBytes = ReadAndValidateCandidateNoLock(
            stationCandidatePath,
            marker.StationSha256,
            marker.GenerationId,
            injectFault);
        ValidateCandidateDocuments(studioBytes, stationBytes, marker.GenerationId);

        PublishBytesIfNeededNoLock(
            studioBytes,
            marker.StudioSha256,
            StudioSettingsPath,
            marker.GenerationId,
            StationCommunicationPersistenceStage.RecoveryPublish,
            injectFault);
        PublishBytesIfNeededNoLock(
            stationBytes,
            marker.StationSha256,
            StationSyncSettingsPath,
            marker.GenerationId,
            StationCommunicationPersistenceStage.RecoveryPublish,
            injectFault);
        EnsureTargetGenerationPublished(marker);

        marker.State = StationCommunicationCommitState.Committed;
        marker.CommittedAtUtc = DateTimeOffset.UtcNow;
        PersistCommitMarkerNoLock(
            marker,
            StationCommunicationPersistenceStage.RecoveryCommit,
            injectFault);
        CleanupTransactionDirectoriesNoThrow(marker.GenerationId, marker.PreviousGenerationId);
    }

    private StationCommunicationPersistenceException RecoverAfterMutationFailureNoLock(
        StationCommunicationPersistenceException failure)
    {
        if (failure.Interruption)
        {
            return failure;
        }

        try
        {
            var marker = ReadCommitMarkerNoLock(injectFault: false);
            if (marker?.State == StationCommunicationCommitState.CommitIntended)
            {
                RestorePreviousGenerationNoLock(marker, injectFault: false);
            }
        }
        catch (Exception recoveryFailure)
        {
            return CreatePersistenceException(
                StationCommunicationPersistenceStage.RecoveryPublish,
                recoveryFailure);
        }

        return failure;
    }

    private void RestorePreviousGenerationNoLock(
        StationCommunicationCommitMarker marker,
        bool injectFault)
    {
        var transactionDirectory = GetTransactionDirectory(marker.GenerationId);
        RestorePreviousFileNoLock(
            Path.Combine(transactionDirectory, PreviousStudioFileName),
            marker.PreviousStudioExists,
            marker.PreviousStudioSha256,
            marker.PreviousStudioLastWriteUtc,
            StudioSettingsPath,
            marker.GenerationId,
            injectFault);
        RestorePreviousFileNoLock(
            Path.Combine(transactionDirectory, PreviousStationFileName),
            marker.PreviousStationExists,
            marker.PreviousStationSha256,
            marker.PreviousStationLastWriteUtc,
            StationSyncSettingsPath,
            marker.GenerationId,
            injectFault);
        EnsurePreviousGenerationPublished(marker);

        string? generationToKeep = null;
        string? priorGenerationToKeep = null;
        if (marker.PreviousMarkerExists)
        {
            var previousMarkerPath = Path.Combine(transactionDirectory, PreviousMarkerFileName);
            var previousMarkerBytes = ReadAndValidateCandidateNoLock(
                previousMarkerPath,
                marker.PreviousMarkerSha256,
                marker.GenerationId,
                injectFault);
            var previousMarker = DeserializeAndValidateMarker(previousMarkerBytes);
            if (previousMarker.State != StationCommunicationCommitState.Committed)
            {
                throw new InvalidDataException("The previous Station communication marker was not committed.");
            }

            if (!string.Equals(
                    previousMarker.GenerationId,
                    marker.PreviousGenerationId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previousMarker.StudioSha256,
                    marker.PreviousStudioSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    previousMarker.StationSha256,
                    marker.PreviousStationSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The previous Station communication marker does not match the rollback generation.");
            }

            PublishBytesIfNeededNoLock(
                previousMarkerBytes,
                marker.PreviousMarkerSha256,
                CommitMarkerPath,
                marker.GenerationId,
                StationCommunicationPersistenceStage.RecoveryRollback,
                injectFault);
            generationToKeep = previousMarker.GenerationId;
            priorGenerationToKeep = previousMarker.PreviousGenerationId;
        }
        else
        {
            RunPersistenceStage(
                StationCommunicationPersistenceStage.RecoveryRollback,
                marker.GenerationId,
                () =>
                {
                    if (File.Exists(CommitMarkerPath))
                    {
                        File.Delete(CommitMarkerPath);
                    }
                },
                injectFault);
        }

        TryRestoreLastWriteTime(StudioSettingsPath, marker.PreviousStudioLastWriteUtc);
        TryRestoreLastWriteTime(StationSyncSettingsPath, marker.PreviousStationLastWriteUtc);
        CleanupTransactionDirectoriesNoThrow(generationToKeep, priorGenerationToKeep);
    }

    private void RestorePreviousFileNoLock(
        string previousCandidatePath,
        bool previousExists,
        string previousSha256,
        DateTimeOffset? previousLastWriteUtc,
        string activePath,
        string generationId,
        bool injectFault)
    {
        if (!previousExists)
        {
            RunPersistenceStage(
                StationCommunicationPersistenceStage.RecoveryRollback,
                generationId,
                () =>
                {
                    if (File.Exists(activePath))
                    {
                        File.Delete(activePath);
                    }
                },
                injectFault);
            return;
        }

        var previousBytes = ReadAndValidateCandidateNoLock(
            previousCandidatePath,
            previousSha256,
            generationId,
            injectFault);
        PublishBytesIfNeededNoLock(
            previousBytes,
            previousSha256,
            activePath,
            generationId,
            StationCommunicationPersistenceStage.RecoveryRollback,
            injectFault);
        TryRestoreLastWriteTime(activePath, previousLastWriteUtc);
    }

    private StationCommunicationCommitMarker? ReadCommitMarkerNoLock(bool injectFault)
    {
        if (!File.Exists(CommitMarkerPath))
        {
            return null;
        }

        return RunPersistenceStage(
            StationCommunicationPersistenceStage.AuthoritativeRead,
            string.Empty,
            () => DeserializeAndValidateMarker(File.ReadAllBytes(CommitMarkerPath)),
            injectFault);
    }

    private void PersistCommitMarkerNoLock(
        StationCommunicationCommitMarker marker,
        StationCommunicationPersistenceStage stage,
        bool injectFault)
    {
        ValidateCommitMarker(marker);
        var markerBytes = JsonSerializer.SerializeToUtf8Bytes(marker, JsonOptions);
        var tempPath = CommitMarkerPath + "." + marker.GenerationId + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            RunPersistenceStage(
                stage,
                marker.GenerationId,
                () =>
                {
                    EnsureParentDirectory(CommitMarkerPath);
                    WriteAllBytesDurable(tempPath, markerBytes);
                    File.Move(tempPath, CommitMarkerPath, overwrite: true);
                    EnsureFileHash(CommitMarkerPath, ComputeSha256(markerBytes));
                },
                injectFault);
        }
        finally
        {
            TryDeleteFileNoThrow(tempPath);
        }
    }

    private void PublishCandidateNoLock(
        string candidatePath,
        string expectedSha256,
        string activePath,
        string generationId,
        StationCommunicationPersistenceStage stage,
        bool injectFault)
    {
        var bytes = ReadAndValidateCandidateNoLock(
            candidatePath,
            expectedSha256,
            generationId,
            injectFault);
        PublishBytesIfNeededNoLock(
            bytes,
            expectedSha256,
            activePath,
            generationId,
            stage,
            injectFault);
    }

    private byte[] ReadAndValidateCandidateNoLock(
        string candidatePath,
        string expectedSha256,
        string generationId,
        bool injectFault)
    {
        return RunPersistenceStage(
            StationCommunicationPersistenceStage.CandidateRead,
            generationId,
            () =>
            {
                var bytes = File.ReadAllBytes(candidatePath);
                if (!string.Equals(ComputeSha256(bytes), expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Station communication candidate hash mismatch.");
                }

                return bytes;
            },
            injectFault);
    }

    private void PublishBytesIfNeededNoLock(
        byte[] bytes,
        string expectedSha256,
        string activePath,
        string generationId,
        StationCommunicationPersistenceStage stage,
        bool injectFault)
    {
        if (FileMatchesHash(activePath, expectedSha256))
        {
            return;
        }

        var tempPath = activePath + "." + generationId + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            RunPersistenceStage(
                stage,
                generationId,
                () =>
                {
                    EnsureParentDirectory(activePath);
                    WriteAllBytesDurable(tempPath, bytes);
                    File.Move(tempPath, activePath, overwrite: true);
                    EnsureFileHash(activePath, expectedSha256);
                },
                injectFault);
        }
        finally
        {
            TryDeleteFileNoThrow(tempPath);
        }
    }

    private FileSnapshot CaptureFileNoLock(string path, string generationId)
    {
        if (!File.Exists(path))
        {
            return FileSnapshot.Missing;
        }

        return RunPersistenceStage(
            StationCommunicationPersistenceStage.PreviousGenerationPrepared,
            generationId,
            () => new FileSnapshot(
                File.ReadAllBytes(path),
                new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)));
    }

    private void PersistPreviousFileNoLock(
        string transactionDirectory,
        string fileName,
        FileSnapshot snapshot,
        string generationId)
    {
        if (!snapshot.Exists)
        {
            return;
        }

        RunPersistenceStage(
            StationCommunicationPersistenceStage.PreviousGenerationPrepared,
            generationId,
            () => WriteAllBytesDurable(Path.Combine(transactionDirectory, fileName), snapshot.Bytes!));
    }

    private static void ValidateCandidateDocuments(
        byte[] studioBytes,
        byte[] stationBytes,
        string generationId)
    {
        var studioDocument = JsonSerializer.Deserialize<StudioStationCommunicationSettingsDocument>(studioBytes, JsonOptions)
            ?? throw new InvalidDataException("Studio Station communication candidate is null.");
        var stationDocument = JsonSerializer.Deserialize<StationSyncSettingsDocument>(stationBytes, JsonOptions)
            ?? throw new InvalidDataException("Local Station communication candidate is null.");
        if (!string.Equals(studioDocument.GenerationId, generationId, StringComparison.Ordinal) ||
            !string.Equals(stationDocument.GenerationId, generationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Station communication candidates do not describe one generation.");
        }
    }

    private string? ValidatePublishedGeneration(
        StudioStationCommunicationSettingsDocument studioDocument,
        StationSyncSettingsDocument stationDocument)
    {
        var studioGeneration = NormalizeGenerationId(studioDocument.GenerationId);
        var stationGeneration = NormalizeGenerationId(stationDocument.GenerationId);
        if (studioGeneration == null && stationGeneration == null)
        {
            return null;
        }

        if (studioGeneration != null &&
            string.Equals(studioGeneration, stationGeneration, StringComparison.Ordinal))
        {
            return studioGeneration;
        }

        throw CreatePersistenceException(
            StationCommunicationPersistenceStage.AuthoritativeRead,
            new InvalidDataException("Studio and local Station settings contain different generations."));
    }

    private static string? NormalizeGenerationId(string? generationId)
    {
        if (string.IsNullOrWhiteSpace(generationId))
        {
            return null;
        }

        generationId = generationId.Trim();
        if (!Guid.TryParseExact(generationId, "N", out _))
        {
            throw new InvalidDataException("Station communication generation identifier is invalid.");
        }

        return generationId;
    }

    private bool TargetGenerationIsPublished(StationCommunicationCommitMarker marker)
    {
        return FileMatchesHash(StudioSettingsPath, marker.StudioSha256) &&
            FileMatchesHash(StationSyncSettingsPath, marker.StationSha256);
    }

    private void EnsureTargetGenerationPublished(StationCommunicationCommitMarker marker)
    {
        if (!TargetGenerationIsPublished(marker))
        {
            throw new InvalidDataException("Station communication generation publish verification failed.");
        }
    }

    private void EnsurePreviousGenerationPublished(StationCommunicationCommitMarker marker)
    {
        if (marker.PreviousStudioExists != File.Exists(StudioSettingsPath) ||
            marker.PreviousStationExists != File.Exists(StationSyncSettingsPath) ||
            (marker.PreviousStudioExists && !FileMatchesHash(StudioSettingsPath, marker.PreviousStudioSha256)) ||
            (marker.PreviousStationExists && !FileMatchesHash(StationSyncSettingsPath, marker.PreviousStationSha256)))
        {
            throw new InvalidDataException("Station communication rollback verification failed.");
        }
    }

    private StationCommunicationCommitMarker DeserializeAndValidateMarker(byte[] markerBytes)
    {
        var marker = JsonSerializer.Deserialize<StationCommunicationCommitMarker>(markerBytes, JsonOptions)
            ?? throw new InvalidDataException("Station communication commit marker is null.");
        ValidateCommitMarker(marker);
        return marker;
    }

    private static void ValidateCommitMarker(StationCommunicationCommitMarker marker)
    {
        if (marker.SchemaVersion != CommitMarkerSchemaVersion ||
            !Guid.TryParseExact(marker.GenerationId, "N", out _) ||
            !Enum.IsDefined(typeof(StationCommunicationCommitState), marker.State) ||
            !IsSha256(marker.StudioSha256) ||
            !IsSha256(marker.StationSha256) ||
            (marker.PreviousStudioExists && !IsSha256(marker.PreviousStudioSha256)) ||
            (marker.PreviousStationExists && !IsSha256(marker.PreviousStationSha256)) ||
            (marker.PreviousMarkerExists && !IsSha256(marker.PreviousMarkerSha256)))
        {
            throw new InvalidDataException("Station communication commit marker is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(marker.PreviousGenerationId) &&
            !Guid.TryParseExact(marker.PreviousGenerationId, "N", out _))
        {
            throw new InvalidDataException("Previous Station communication generation identifier is invalid.");
        }
    }

    private static bool IsSha256(string? value)
    {
        return value?.Length == 64 && value.All(Uri.IsHexDigit);
    }

    private static void EnsureFileHash(string path, string expectedSha256)
    {
        if (!FileMatchesHash(path, expectedSha256))
        {
            throw new IOException("Station communication durable write verification failed.");
        }
    }

    private static bool FileMatchesHash(string path, string expectedSha256)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var bytes = File.ReadAllBytes(path);
        return string.Equals(ComputeSha256(bytes), expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void WriteAllBytesDurable(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private string GetTransactionDirectory(string generationId)
    {
        if (!Guid.TryParseExact(generationId, "N", out _))
        {
            throw new InvalidDataException("Station communication transaction identifier is invalid.");
        }

        return Path.Combine(TransactionRootPath, generationId);
    }

    private void CleanupTransactionDirectoriesNoThrow(params string?[] generationsToKeep)
    {
        try
        {
            if (!Directory.Exists(TransactionRootPath))
            {
                return;
            }

            var normalizedRoot = Path.GetFullPath(TransactionRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var keep = generationsToKeep
                .Where(static generation => !string.IsNullOrWhiteSpace(generation))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.EnumerateDirectories(TransactionRootPath))
            {
                var fullPath = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var directoryName = Path.GetFileName(fullPath);
                if (!string.Equals(Path.GetDirectoryName(fullPath), normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParseExact(directoryName, "N", out _) ||
                    keep.Contains(directoryName))
                {
                    continue;
                }

                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch
        {
            // Uniquely named, non-authoritative residue is retried by the next operation.
        }
    }

    private static void TryDeleteFileNoThrow(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A unique temp is never authoritative and can be cleaned on a later operation.
        }
    }

    private static void TryRestoreLastWriteTime(string path, DateTimeOffset? lastWriteUtc)
    {
        if (!lastWriteUtc.HasValue || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.SetLastWriteTimeUtc(path, lastWriteUtc.Value.UtcDateTime);
        }
        catch
        {
            // Content/generation identity, not timestamp restoration, is authoritative.
        }
    }

    private T RunPersistenceStage<T>(
        StationCommunicationPersistenceStage stage,
        string generationId,
        Func<T> action,
        bool injectFault = true)
    {
        try
        {
            if (injectFault)
            {
                _faultInjector.OnStage(stage, generationId);
            }

            return action();
        }
        catch (StationCommunicationPersistenceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreatePersistenceException(stage, ex);
        }
    }

    private void RunPersistenceStage(
        StationCommunicationPersistenceStage stage,
        string generationId,
        Action action,
        bool injectFault = true)
    {
        RunPersistenceStage(
            stage,
            generationId,
            () =>
            {
                action();
                return true;
            },
            injectFault);
    }

    private void Checkpoint(StationCommunicationPersistenceStage stage, string generationId)
    {
        RunPersistenceStage(stage, generationId, static () => { });
    }

    private static object GetOperationGate(string studioPath, string stationPath)
    {
        var key = Path.GetFullPath(studioPath) + "\0" + Path.GetFullPath(stationPath);
        return OperationGates.GetOrAdd(key, static _ => new object());
    }

    private static StationCommunicationPersistenceException ToPersistenceException(
        StationCommunicationPersistenceStage stage,
        Exception exception)
    {
        return exception as StationCommunicationPersistenceException
            ?? CreatePersistenceException(stage, exception);
    }

    private static StationCommunicationPersistenceException CreatePersistenceException(
        StationCommunicationPersistenceStage stage,
        Exception exception)
    {
        var interruption = ContainsException<StationCommunicationPersistenceInterruptionException>(exception);
        var errorCode = interruption
            ? "STATION_COMMUNICATION_INTERRUPTED"
            : ContainsException<UnauthorizedAccessException>(exception)
                ? "STATION_COMMUNICATION_PERMISSION_DENIED"
                : ContainsException<InvalidDataException>(exception) || ContainsException<JsonException>(exception)
                    ? "STATION_COMMUNICATION_RECOVERY_REQUIRED"
                    : ContainsException<IOException>(exception)
                        ? "STATION_COMMUNICATION_IO_FAILED"
                        : "STATION_COMMUNICATION_PERSISTENCE_FAILED";
        return new StationCommunicationPersistenceException(
            errorCode,
            GetPublicStage(stage),
            interruption,
            exception);
    }

    private static bool ContainsException<T>(Exception exception)
        where T : Exception
    {
        if (exception is T)
        {
            return true;
        }

        if (exception is AggregateException aggregate &&
            aggregate.InnerExceptions.Any(inner => ContainsException<T>(inner)))
        {
            return true;
        }

        return exception.InnerException != null && ContainsException<T>(exception.InnerException);
    }

    private static string GetPublicStage(StationCommunicationPersistenceStage stage)
    {
        return stage switch
        {
            StationCommunicationPersistenceStage.AuthoritativeRead or
            StationCommunicationPersistenceStage.AuthoritativeSnapshotRead => "authoritative-read",
            StationCommunicationPersistenceStage.CandidateStarted or
            StationCommunicationPersistenceStage.StudioCandidateWrite or
            StationCommunicationPersistenceStage.StationCandidateWrite or
            StationCommunicationPersistenceStage.CandidateRead or
            StationCommunicationPersistenceStage.PreviousGenerationPrepared => "candidate",
            StationCommunicationPersistenceStage.CommitIntentWrite or
            StationCommunicationPersistenceStage.CommitIntended => "commit-marker",
            StationCommunicationPersistenceStage.StudioPublish or
            StationCommunicationPersistenceStage.StudioPublished => "studio-publish",
            StationCommunicationPersistenceStage.StationPublish or
            StationCommunicationPersistenceStage.StationPublished => "station-publish",
            StationCommunicationPersistenceStage.CommitCompleteWrite or
            StationCommunicationPersistenceStage.CommitCompleted => "commit-complete",
            StationCommunicationPersistenceStage.RecoveryStarted or
            StationCommunicationPersistenceStage.RecoveryPublish or
            StationCommunicationPersistenceStage.RecoveryRollback or
            StationCommunicationPersistenceStage.RecoveryCommit or
            StationCommunicationPersistenceStage.RecoveryCompleted => "recovery",
            _ => "operation"
        };
    }

    private static StationCommunicationSaveResult BuildSaveFailure(
        StationCommunicationPersistenceException failure)
    {
        return StationCommunicationSaveResult.PersistenceFailed(
            failure.PublicMessage,
            failure.ErrorCode,
            failure.Stage,
            failure.Retryable);
    }

    private static StationCommunicationTokenResult BuildTokenFailure(
        string operation,
        StationCommunicationPersistenceException failure)
    {
        return new StationCommunicationTokenResult
        {
            Success = false,
            Operation = operation,
            Message = failure.PublicMessage,
            PublicMessage = failure.PublicMessage,
            ErrorCode = failure.ErrorCode,
            Stage = failure.Stage,
            Retryable = failure.Retryable
        };
    }

    private bool IsStationSyncRestartRequired()
    {
        if (!File.Exists(StationSyncSettingsPath))
        {
            return false;
        }

        if (!File.Exists(StationSyncAppliedMarkerPath))
        {
            return true;
        }

        try
        {
            return File.GetLastWriteTimeUtc(StationSyncSettingsPath) >
                File.GetLastWriteTimeUtc(StationSyncAppliedMarkerPath);
        }
        catch
        {
            return true;
        }
    }

    private static StationCommunicationMetadata BuildMetadata(
        StationCommunicationMode mode,
        string? lanHost,
        bool? localStationSyncEnabled)
    {
        return new StationCommunicationMetadata
        {
            Mode = mode,
            LanHost = NormalizeLanHost(lanHost, null),
            LocalStationSyncEnabled = localStationSyncEnabled ?? mode != StationCommunicationMode.Disabled,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static bool TryParseMode(string? value, out StationCommunicationMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = StationCommunicationMode.Disabled;
            return true;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out mode) &&
            Enum.IsDefined(typeof(StationCommunicationMode), mode);
    }

    private static StationCommunicationMode InferMode(
        StationIngressOptions ingress,
        StationCommunicationMetadata? metadata)
    {
        if (!ingress.Enabled)
        {
            return StationCommunicationMode.Disabled;
        }

        if (ingress.ListenMode == StationIngressListenMode.Lan)
        {
            return StationCommunicationMode.LanController;
        }

        if (metadata?.Mode == StationCommunicationMode.LanController)
        {
            return StationCommunicationMode.LanController;
        }

        return StationCommunicationMode.LocalLoopback;
    }

    private static string ResolveToken(PersistedStationCommunicationSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Ingress.SharedToken))
        {
            return snapshot.Ingress.SharedToken.Trim();
        }

        return snapshot.StationSync?.SharedToken?.Trim() ?? string.Empty;
    }

    private static StationCommunicationTokenView BuildTokenInfo(string? token)
    {
        token = token?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new StationCommunicationTokenView
            {
                HasToken = false,
                Mask = string.Empty,
                Last4 = string.Empty
            };
        }

        var last4 = token.Length <= 4 ? token : token[^4..];
        return new StationCommunicationTokenView
        {
            HasToken = true,
            Mask = "****" + last4,
            Last4 = last4
        };
    }

    private static string GenerateToken(string? excludedToken = null)
    {
        excludedToken = excludedToken?.Trim();
        for (var attempt = 0; attempt < 5; attempt += 1)
        {
            var token = FormatToken(RandomNumberGenerator.GetInt32(GeneratedTokenUpperBound));
            if (!string.Equals(token, excludedToken, StringComparison.Ordinal))
            {
                return token;
            }
        }

        if (int.TryParse(excludedToken, NumberStyles.None, CultureInfo.InvariantCulture, out var excludedValue) &&
            excludedValue is >= 0 and < GeneratedTokenUpperBound)
        {
            return FormatToken((excludedValue + 1) % GeneratedTokenUpperBound);
        }

        return FormatToken(RandomNumberGenerator.GetInt32(GeneratedTokenUpperBound));
    }

    private static string FormatToken(int value)
    {
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static bool TryNormalizeLanHost(
        string? value,
        string? fallback,
        out string lanHost,
        out string errorMessage)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            lanHost = ResolveMachineHostName();
            errorMessage = string.Empty;
            return true;
        }

        candidate = candidate.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out _) ||
            candidate.Any(char.IsWhiteSpace) ||
            candidate.IndexOfAny(new[] { '/', '\\', '?', '#', '@' }) >= 0)
        {
            lanHost = string.Empty;
            errorMessage = "LAN host must be a host name or IP address without scheme, path, or spaces.";
            return false;
        }

        var unbracketed = candidate;
        if (candidate.StartsWith("[", StringComparison.Ordinal) &&
            candidate.EndsWith("]", StringComparison.Ordinal) &&
            candidate.Length > 2)
        {
            unbracketed = candidate[1..^1];
        }

        if (IPAddress.TryParse(unbracketed, out var address))
        {
            lanHost = address.ToString();
            errorMessage = string.Empty;
            return true;
        }

        if (Uri.CheckHostName(candidate) == UriHostNameType.Dns)
        {
            lanHost = candidate;
            errorMessage = string.Empty;
            return true;
        }

        lanHost = string.Empty;
        errorMessage = "LAN host must be a valid host name or IP address.";
        return false;
    }

    private static string NormalizeLanHost(string? value, string? fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value;
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate.Trim();
        }

        return ResolveMachineHostName();
    }

    private static string ResolveMachineHostName()
    {
        try
        {
            return Dns.GetHostName();
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private static string FormatHostForUrl(string host)
    {
        return host.Contains(':', StringComparison.Ordinal) && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]"
            : host;
    }

    private static IReadOnlyList<string> DiscoverLanAddresses()
    {
        try
        {
            var host = Dns.GetHostName();
            return Dns.GetHostEntry(host)
                .AddressList
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> BuildDiagnostics(
        StationCommunicationMode mode,
        string token,
        bool requiresStudioRestart,
        bool requiresLocalStationRestart,
        string remoteBaseUrl)
    {
        var diagnostics = new List<string>();
        if (mode == StationCommunicationMode.Disabled)
        {
            diagnostics.Add("Station 通讯已关闭：本机 Studio 不接收 Station 注册，本机 Station 也不会主动同步。");
        }
        else
        {
            diagnostics.Add(requiresStudioRestart
                ? "需要重启本机 Studio：保存的监听模式、端口或 token 尚未被当前 Studio 进程读取。"
                : "本机 Studio 已按当前保存的监听模式、端口和 token 运行。");
            diagnostics.Add(requiresLocalStationRestart
                ? "需要重启本机 Station：保存的本机 Station 同步地址或 token 尚未被本机 Station 读取。"
                : "本机 Station 配置文件已被最近一次本机 Station 启动读取。");
        }

        if (mode == StationCommunicationMode.Disabled && (requiresStudioRestart || requiresLocalStationRestart))
        {
            diagnostics.Add("要完全停止通讯，请按上面的提示重启对应进程。");
        }

        if (mode == StationCommunicationMode.LanController)
        {
            diagnostics.Add(string.IsNullOrWhiteSpace(remoteBaseUrl)
                ? "局域网总控模式需要填写一个其他电脑能访问到的本机局域网 IP。"
                : $"另一台电脑的 Station 应填写 StudioBaseUrl={remoteBaseUrl}，并使用同一个 token。");
        }

        if (mode != StationCommunicationMode.Disabled && string.IsNullOrWhiteSpace(token))
        {
            diagnostics.Add("必须先生成共享 token，Station 才能注册到 Studio。");
        }

        if (!requiresStudioRestart && !requiresLocalStationRestart)
        {
            diagnostics.Add("当前页面保存值与本机已知运行值一致；这不代表远端 Station 已连接成功。");
        }

        return diagnostics;
    }

    private static bool AreIngressOptionsEquivalent(StationIngressOptions left, StationIngressOptions right)
    {
        return left.Enabled == right.Enabled &&
            left.ListenMode == right.ListenMode &&
            left.Port == right.Port &&
            string.Equals(left.SharedToken ?? string.Empty, right.SharedToken ?? string.Empty, StringComparison.Ordinal) &&
            left.AllowInsecureDevelopment == right.AllowInsecureDevelopment;
    }

    private static bool AreStationSyncOptionsEquivalent(LocalStationSyncOptions left, LocalStationSyncOptions right)
    {
        return left.Enabled == right.Enabled &&
            string.Equals(left.StudioBaseUrl ?? string.Empty, right.StudioBaseUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.StudioHubUrl ?? string.Empty, right.StudioHubUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.SharedToken ?? string.Empty, right.SharedToken ?? string.Empty, StringComparison.Ordinal);
    }

    private static StationIngressOptions CloneIngress(StationIngressOptions source)
    {
        return new StationIngressOptions
        {
            Enabled = source.Enabled,
            ListenMode = source.ListenMode,
            Port = source.Port,
            SharedToken = source.SharedToken ?? string.Empty,
            AllowInsecureDevelopment = source.AllowInsecureDevelopment,
            AllowMessagePack = source.AllowMessagePack,
            OfflineThresholdSeconds = source.OfflineThresholdSeconds,
            ResultBufferPerStation = source.ResultBufferPerStation,
            EventBufferSize = source.EventBufferSize,
            HealthBufferPerStation = source.HealthBufferPerStation,
            LogBufferPerStation = source.LogBufferPerStation,
            CommandBufferPerStation = source.CommandBufferPerStation
        };
    }

    private static LocalStationSyncOptions CloneStationSync(LocalStationSyncOptions source)
    {
        return new LocalStationSyncOptions
        {
            Enabled = source.Enabled,
            StudioBaseUrl = source.StudioBaseUrl ?? string.Empty,
            StudioHubUrl = source.StudioHubUrl ?? string.Empty,
            SharedToken = source.SharedToken ?? string.Empty,
            ExtensionData = CloneExtensionData(source.ExtensionData)
        };
    }

    private static Dictionary<string, JsonElement>? CloneExtensionData(Dictionary<string, JsonElement>? source)
    {
        if (source == null)
        {
            return null;
        }

        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed record PersistedStationCommunicationSnapshot(
        StudioStationCommunicationSettingsDocument StudioDocument,
        StationSyncSettingsDocument StationSyncDocument,
        StationCommunicationMetadata Metadata,
        StationIngressOptions Ingress,
        LocalStationSyncOptions? StationSync,
        string? GenerationId);

    private sealed record StationCommunicationTarget(
        StudioStationCommunicationSettingsDocument StudioDocument,
        StationSyncSettingsDocument StationSyncDocument,
        StationIngressOptions Ingress,
        LocalStationSyncOptions StationSync);

    private sealed record FileSnapshot(byte[]? Bytes, DateTimeOffset? LastWriteUtc)
    {
        public static FileSnapshot Missing { get; } = new(null, null);

        public bool Exists => Bytes != null;
    }

    private enum StationCommunicationCommitState
    {
        CommitIntended = 0,
        Committed = 1
    }

    private sealed class StationCommunicationCommitMarker
    {
        public StationCommunicationCommitMarker()
        {
        }

        public int SchemaVersion { get; set; }

        public StationCommunicationCommitState State { get; set; }

        public string GenerationId { get; set; } = string.Empty;

        public string PreviousGenerationId { get; set; } = string.Empty;

        public string StudioSha256 { get; set; } = string.Empty;

        public string StationSha256 { get; set; } = string.Empty;

        public bool PreviousStudioExists { get; set; }

        public string PreviousStudioSha256 { get; set; } = string.Empty;

        public DateTimeOffset? PreviousStudioLastWriteUtc { get; set; }

        public bool PreviousStationExists { get; set; }

        public string PreviousStationSha256 { get; set; } = string.Empty;

        public DateTimeOffset? PreviousStationLastWriteUtc { get; set; }

        public bool PreviousMarkerExists { get; set; }

        public string PreviousMarkerSha256 { get; set; } = string.Empty;

        public DateTimeOffset PreparedAtUtc { get; set; }

        public DateTimeOffset? CommittedAtUtc { get; set; }
    }
}

public sealed class StudioStationCommunicationSettingsDocument
{
    public string GenerationId { get; set; } = string.Empty;

    public StationCommunicationMetadata? StationCommunication { get; set; }

    public StationIngressOptions? StationIngress { get; set; }
}

public sealed class StationCommunicationMetadata
{
    public StationCommunicationMode Mode { get; set; } = StationCommunicationMode.Disabled;

    public string LanHost { get; set; } = string.Empty;

    public bool LocalStationSyncEnabled { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StationSyncSettingsDocument
{
    public string GenerationId { get; set; } = string.Empty;

    public LocalStationSyncOptions? StationSync { get; set; }
}

public sealed class LocalStationSyncOptions
{
    public bool Enabled { get; set; }

    public string StudioBaseUrl { get; set; } = string.Empty;

    public string StudioHubUrl { get; set; } = string.Empty;

    public string SharedToken { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class StationCommunicationSettingsUpdateRequest
{
    public string? Mode { get; set; }

    public int? Port { get; set; }

    public string? LanHost { get; set; }

    public bool? LocalStationSyncEnabled { get; set; }

    public string? SharedToken { get; set; }
}

public sealed class StationCommunicationTokenRequest
{
    public string? Operation { get; set; }

    public string? Action { get; set; }
}

public sealed class StationCommunicationSettingsView
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string GenerationId { get; set; } = string.Empty;

    public string Mode { get; set; } = StationCommunicationMode.Disabled.ToString();

    public int Port { get; set; }

    public string LanHost { get; set; } = string.Empty;

    public IReadOnlyList<string> LanAddresses { get; set; } = Array.Empty<string>();

    public bool LocalStationSyncEnabled { get; set; }

    public StationCommunicationTokenView Token { get; set; } = new();

    public StationCommunicationPathView Paths { get; set; } = new();

    public StationCommunicationRunningView CurrentRunning { get; set; } = new();

    public StationCommunicationRestartView RequiresRestart { get; set; } = new();

    public string LocalStationBaseUrl { get; set; } = string.Empty;

    public string RemoteStationBaseUrl { get; set; } = string.Empty;

    public string LocalStationHubUrl { get; set; } = string.Empty;

    public string RemoteStationHubUrl { get; set; } = string.Empty;

    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();
}

public sealed class StationCommunicationTokenView
{
    public bool HasToken { get; set; }

    public string Mask { get; set; } = string.Empty;

    public string Last4 { get; set; } = string.Empty;
}

public sealed class StationCommunicationPathView
{
    public string Studio { get; set; } = string.Empty;

    public string LocalStation { get; set; } = string.Empty;
}

public sealed class StationCommunicationRunningView
{
    public bool StudioEnabled { get; set; }

    public string StudioListenMode { get; set; } = string.Empty;

    public int StudioPort { get; set; }

    public StationCommunicationTokenView StudioToken { get; set; } = new();
}

public sealed class StationCommunicationRestartView
{
    public bool Studio { get; set; }

    public bool LocalStation { get; set; }
}

public sealed class StationCommunicationValidationError
{
    public StationCommunicationValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }

    public string Field { get; }

    public string Message { get; }
}

public sealed class StationCommunicationPersistenceException : Exception
{
    internal StationCommunicationPersistenceException(
        string errorCode,
        string stage,
        bool interruption,
        Exception innerException)
        : base("Station communication settings could not be durably committed or recovered.", innerException)
    {
        ErrorCode = errorCode;
        Stage = stage;
        Interruption = interruption;
    }

    public string ErrorCode { get; }

    public string Stage { get; }

    public bool Retryable => true;

    public string PublicMessage =>
        "Station communication settings were not durably published. Resolve the storage error and retry; recovery accepts only a complete previous or intended generation.";

    internal bool Interruption { get; }
}

public sealed class StationCommunicationSaveResult
{
    public bool Success { get; private init; }

    public string Message { get; private init; } = string.Empty;

    public string PublicMessage { get; private init; } = string.Empty;

    public StationCommunicationSettingsView? Settings { get; private init; }

    public IReadOnlyList<StationCommunicationValidationError> Errors { get; private init; } =
        Array.Empty<StationCommunicationValidationError>();

    public string ErrorCode { get; private init; } = string.Empty;

    public string Stage { get; private init; } = string.Empty;

    public bool Retryable { get; private init; }

    public static StationCommunicationSaveResult Succeeded(StationCommunicationSettingsView settings)
    {
        return new StationCommunicationSaveResult
        {
            Success = true,
            Message = settings.Message,
            Settings = settings
        };
    }

    public static StationCommunicationSaveResult Failed(
        string message,
        IReadOnlyList<StationCommunicationValidationError> errors)
    {
        return new StationCommunicationSaveResult
        {
            Success = false,
            Message = message,
            Errors = errors
        };
    }

    public static StationCommunicationSaveResult PersistenceFailed(
        string message,
        string errorCode,
        string stage,
        bool retryable)
    {
        return new StationCommunicationSaveResult
        {
            Success = false,
            Message = message,
            PublicMessage = message,
            ErrorCode = errorCode,
            Stage = stage,
            Retryable = retryable
        };
    }
}

public sealed class StationCommunicationTokenResult
{
    public bool Success { get; set; }

    public string Operation { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public StationCommunicationTokenView TokenInfo { get; set; } = new();

    public StationCommunicationSettingsView? Settings { get; set; }

    public string Message { get; set; } = string.Empty;

    public string PublicMessage { get; set; } = string.Empty;

    public IReadOnlyList<StationCommunicationValidationError> Errors { get; set; } =
        Array.Empty<StationCommunicationValidationError>();

    public string ErrorCode { get; set; } = string.Empty;

    public string Stage { get; set; } = string.Empty;

    public bool Retryable { get; set; }
}
