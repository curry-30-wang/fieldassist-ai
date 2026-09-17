namespace AsphaltPlantManager.Core.Backup;

public interface IBackupService
{
    string DefaultDatabasePath { get; }

    string DefaultDestinationRoot { get; }

    Task<string> CreateAsync(BackupReason reason, string destinationRoot, CancellationToken cancellationToken);

    Task<BackupValidationResult> ValidateAsync(string backupPath, CancellationToken cancellationToken);

    Task RestoreAsync(string backupPath, CancellationToken cancellationToken);

    Task PruneAsync(string destinationRoot, int keepCount, CancellationToken cancellationToken);
}

public sealed record BackupManifestInfo(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    BackupReason Reason,
    string SourceDbFilename,
    long DbByteLength,
    string Sha256,
    string AppVersion);

public sealed record BackupValidationResult(bool IsValid, BackupManifestInfo Manifest);
