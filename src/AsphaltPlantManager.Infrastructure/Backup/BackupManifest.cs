using AsphaltPlantManager.Core.Backup;

namespace AsphaltPlantManager.Infrastructure.Backup;

internal sealed record BackupManifest(
    int SchemaVersion,
    string CreatedAt,
    BackupReason Reason,
    string SourceDbFilename,
    long DbByteLength,
    string Sha256,
    string AppVersion);
