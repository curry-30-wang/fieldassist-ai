using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AsphaltPlantManager.Core.Backup;
using AsphaltPlantManager.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace AsphaltPlantManager.Infrastructure.Backup;

public sealed class BackupService : IBackupService
{
    private const string DatabaseFileName = "asphalt.db";
    private const string ManifestFileName = "manifest.json";
    private const int CurrentSchemaVersion = 1;
    private static readonly Regex AppBackupDirectoryPattern = new(
        @"^\d{8}-\d{6}-(manual|automaticdaily)-[0-9a-f]{12}$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    private readonly string _databasePath;
    private readonly TimeProvider _timeProvider;
    private readonly string _appVersion;
    private readonly BackupServiceHooks _hooks;

    public BackupService(string? databasePath = null, TimeProvider? timeProvider = null, string? appVersion = null)
        : this(databasePath, timeProvider, appVersion, BackupServiceHooks.Empty)
    {
    }

    internal BackupService(string? databasePath, TimeProvider? timeProvider, string? appVersion, BackupServiceHooks hooks)
    {
        if (databasePath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        }

        _databasePath = Path.GetFullPath(databasePath ?? DefaultDatabasePath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        _appVersion = string.IsNullOrWhiteSpace(appVersion)
            ? typeof(BackupService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(BackupService).Assembly.GetName().Version?.ToString()
                ?? "unknown"
            : appVersion;
    }

    public string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AsphaltPlantManager",
        "Data",
        DatabaseFileName);

    public string DefaultDestinationRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Documents",
        "沥青拌合站管理系统备份");

    public Task<string> CreateAsync(BackupReason reason, string destinationRoot, CancellationToken cancellationToken) =>
        CreateBackupAsync(reason, destinationRoot, isPreRestore: false, cancellationToken);

    private async Task<string> CreateBackupAsync(BackupReason reason, string destinationRoot, bool isPreRestore, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException("找不到需要备份的数据库。", _databasePath);
        }

        var root = Path.GetFullPath(destinationRoot);
        Directory.CreateDirectory(root);
        using var protectedRoot = PhysicalPathIdentity.ProtectDirectory(root);
        var timestamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var reasonName = reason.ToString().ToLowerInvariant();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var finalName = isPreRestore
            ? $".pre-restore-{timestamp}-{suffix}"
            : $"{timestamp}-{reasonName}-{suffix}";
        var finalPath = GetDirectChildPath(root, finalName);
        var temporaryPath = GetDirectChildPath(root, $".{finalName.TrimStart('.')}.tmp");
        PhysicalPathIdentity? temporaryIdentity = null;
        var published = false;

        try
        {
            Directory.CreateDirectory(temporaryPath);
            temporaryIdentity = PhysicalPathIdentity.Read(temporaryPath);
            var snapshotPath = Path.Combine(temporaryPath, DatabaseFileName);
            await CreateSqliteSnapshotAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
            await EnsureDatabaseIntegrityAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
            var length = new FileInfo(snapshotPath).Length;
            var sha256 = await ComputeSha256Async(snapshotPath, cancellationToken).ConfigureAwait(false);
            var manifest = new BackupManifest(
                CurrentSchemaVersion,
                _timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                reason,
                Path.GetFileName(_databasePath),
                length,
                sha256,
                _appVersion);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryPath, ManifestFileName),
                JsonSerializer.Serialize(manifest, ManifestJsonOptions),
                cancellationToken).ConfigureAwait(false);

            _ = await ValidateAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(temporaryPath, finalPath);
            published = true;
            _hooks.AfterTemporaryDirectoryPublished?.Invoke(finalPath);
            cancellationToken.ThrowIfCancellationRequested();
            return finalPath;
        }
        catch
        {
            TryDeleteDirectory(root, published ? finalPath : temporaryPath, temporaryIdentity);
            throw;
        }
    }

    public async Task<BackupValidationResult> ValidateAsync(string backupPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var fullBackupPath = Path.GetFullPath(backupPath);
            if (!Directory.Exists(fullBackupPath))
            {
                throw new BackupValidationException("备份目录不存在。");
            }

            var manifestPath = Path.Combine(fullBackupPath, ManifestFileName);
            var databasePath = Path.Combine(fullBackupPath, DatabaseFileName);
            if (!File.Exists(manifestPath) || !File.Exists(databasePath))
            {
                throw new BackupValidationException("备份缺少清单或数据库文件。");
            }

            if ((File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0
                || (File.GetAttributes(databasePath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new BackupValidationException("备份文件不能是指向其他位置的链接。");
            }

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson, ManifestJsonOptions)
                ?? throw new BackupValidationException("备份清单内容无效。");
            if (manifest.SchemaVersion != CurrentSchemaVersion
                || !DateTimeOffset.TryParseExact(manifest.CreatedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var createdAt)
                || createdAt.Offset != TimeSpan.Zero
                || !Enum.IsDefined(manifest.Reason)
                || string.IsNullOrWhiteSpace(manifest.SourceDbFilename)
                || Path.IsPathRooted(manifest.SourceDbFilename)
                || !string.Equals(Path.GetFileName(manifest.SourceDbFilename), manifest.SourceDbFilename, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(manifest.AppVersion)
                || manifest.DbByteLength < 1
                || manifest.Sha256.Length != 64)
            {
                throw new BackupValidationException("备份清单字段无效。");
            }

            if (new FileInfo(databasePath).Length != manifest.DbByteLength)
            {
                throw new BackupValidationException("备份数据库大小与清单不一致。");
            }

            var actualHash = await ComputeSha256Async(databasePath, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(manifest.Sha256)))
            {
                throw new BackupValidationException("备份数据库哈希校验失败。");
            }

            await EnsureDatabaseIntegrityAsync(databasePath, cancellationToken).ConfigureAwait(false);
            return new BackupValidationResult(
                true,
                new BackupManifestInfo(
                    manifest.SchemaVersion,
                    createdAt,
                    manifest.Reason,
                    manifest.SourceDbFilename,
                    manifest.DbByteLength,
                    manifest.Sha256.ToLowerInvariant(),
                    manifest.AppVersion));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BackupValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new BackupValidationException("备份校验失败：备份内容无效或已损坏。", exception);
        }
    }

    public async Task RestoreAsync(string backupPath, CancellationToken cancellationToken)
    {
        var validatedBackup = await ValidateAsync(backupPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var databaseDirectory = Path.GetDirectoryName(_databasePath)
            ?? throw new InvalidOperationException("数据库路径无效，无法恢复。");
        Directory.CreateDirectory(databaseDirectory);
        _ = await CreateBackupAsync(BackupReason.PreRestore, databaseDirectory, isPreRestore: true, cancellationToken).ConfigureAwait(false);

        var replacementPath = GetDirectChildPath(databaseDirectory, $".asphalt.restore-{Guid.NewGuid():N}.tmp");
        var rollbackPath = GetDirectChildPath(databaseDirectory, $".asphalt.restore-{Guid.NewGuid():N}.rollback");
        try
        {
            await CopyFileAsync(Path.Combine(Path.GetFullPath(backupPath), DatabaseFileName), replacementPath, cancellationToken).ConfigureAwait(false);
            await EnsureCandidateMatchesManifestAsync(replacementPath, validatedBackup.Manifest, cancellationToken).ConfigureAwait(false);
            await EnsureDatabaseIntegrityAndForeignKeysAsync(replacementPath, cancellationToken).ConfigureAwait(false);
            var candidateIdentity = PhysicalPathIdentity.Read(replacementPath);
            _hooks.BeforeMaintenanceLeaseAcquisition?.Invoke();
            await using var maintenance = await SqliteDatabaseAccess.AcquireExclusiveForPathAsync(_databasePath, cancellationToken).ConfigureAwait(false);
            await PrepareLiveDatabaseForReplacementAsync(cancellationToken).ConfigureAwait(false);
            await using (var candidateLock = new FileStream(
                             replacementPath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read))
            {
                _hooks.BeforeFinalCandidateValidation?.Invoke(replacementPath);
                await EnsureCandidateMatchesManifestAsync(replacementPath, validatedBackup.Manifest, cancellationToken).ConfigureAwait(false);
                await EnsureDatabaseIntegrityAndForeignKeysAsync(replacementPath, cancellationToken).ConfigureAwait(false);
                if (PhysicalPathIdentity.Read(replacementPath) != candidateIdentity)
                {
                    throw new BackupValidationException("恢复候选数据库在最终校验前已被替换。");
                }

                _hooks.AfterFinalCandidateValidation?.Invoke(replacementPath);
                await EnsureCandidateMatchesManifestAsync(replacementPath, validatedBackup.Manifest, cancellationToken).ConfigureAwait(false);
                await EnsureDatabaseIntegrityAndForeignKeysAsync(replacementPath, cancellationToken).ConfigureAwait(false);
                if (PhysicalPathIdentity.Read(replacementPath) != candidateIdentity)
                {
                    throw new BackupValidationException("恢复候选数据库在最终校验后被替换。");
                }
            }

            if (File.Exists(_databasePath + "-wal") || File.Exists(_databasePath + "-shm"))
            {
                throw new IOException("数据库日志在清理后重新出现，无法安全恢复。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Replace(replacementPath, _databasePath, rollbackPath, ignoreMetadataErrors: true);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(replacementPath);
            throw;
        }
        catch (Exception exception)
        {
            TryDeleteFile(replacementPath);
            throw new InvalidOperationException("恢复失败，当前数据库未被覆盖。", exception);
        }

        // File.Replace is the commit point. Everything after it is best effort and must not
        // turn a completed restore into a reported failure or attempt an unreliable rollback.
        TryDeleteFile(rollbackPath);
    }

    public async Task PruneAsync(string destinationRoot, int keepCount, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        if (keepCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(keepCount), keepCount, "备份保留数量必须至少为 1。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(destinationRoot);
        if (!Directory.Exists(root))
        {
            return;
        }

        PhysicalPathIdentity.ProtectedDirectory protectedRoot;
        try
        {
            protectedRoot = PhysicalPathIdentity.ProtectDirectory(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            throw new InvalidOperationException("备份清理根目录不安全，已停止清理。", exception);
        }

        using (protectedRoot)
        {
            await PruneProtectedRootAsync(root, keepCount, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PruneProtectedRootAsync(string root, int keepCount, CancellationToken cancellationToken)
    {

        var candidates = new List<(string Path, DateTimeOffset CreatedAt, PhysicalPathIdentity Identity)>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (!AppBackupDirectoryPattern.IsMatch(name)
                || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            try
            {
                var validation = await ValidateAsync(directory, cancellationToken).ConfigureAwait(false);
                if (validation.Manifest.Reason is BackupReason.Manual or BackupReason.AutomaticDaily)
                {
                    candidates.Add((directory, validation.Manifest.CreatedAt, PhysicalPathIdentity.Read(directory)));
                }
            }
            catch (BackupValidationException)
            {
                // Invalid or user-created lookalike directories are never deleted.
            }
        }

        foreach (var candidate in candidates
                     .OrderByDescending(item => item.CreatedAt)
                     .ThenByDescending(item => item.Path, StringComparer.Ordinal)
                     .Skip(keepCount))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _hooks.BeforePruneDeletion?.Invoke(candidate.Path);
            var directChild = GetDirectChildPath(root, Path.GetFileName(candidate.Path));
            if (!string.Equals(directChild, Path.GetFullPath(candidate.Path), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("备份清理路径超出了备份根目录。");
            }

            DeleteDirectChildDirectorySafely(root, directChild, candidate.Identity);
        }
    }

    private void DeleteDirectChildDirectorySafely(
        string root,
        string path,
        PhysicalPathIdentity expectedIdentity)
    {
        var fullRoot = Path.GetFullPath(root);
        var directChild = GetDirectChildPath(fullRoot, Path.GetFileName(path));
        if (!string.Equals(directChild, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("备份清理路径超出了备份根目录。");
        }

        using var protectedRoot = PhysicalPathIdentity.ProtectDirectory(fullRoot);
        if (!Directory.Exists(directChild))
        {
            throw new InvalidOperationException("备份清理候选目录在删除前已发生变化。");
        }

        var tombstone = GetDirectChildPath(fullRoot, $".delete-{Guid.NewGuid():N}.tombstone");
        try
        {
            PhysicalPathIdentity.MoveDirectChildDirectory(
                protectedRoot,
                directChild,
                Path.GetFileName(tombstone),
                expectedIdentity);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("备份清理候选目录在移动前身份已发生变化，未执行改名或删除。", exception);
        }

        _hooks.AfterDirectoryMovedToTombstone?.Invoke(directChild, tombstone);
        VerifyTombstoneOrRestoreOriginalName(directChild, tombstone, expectedIdentity);

        DeleteDirectoryTreeWithoutFollowingLinks(tombstone);
    }

    private static void VerifyTombstoneOrRestoreOriginalName(
        string originalPath,
        string tombstonePath,
        PhysicalPathIdentity expectedIdentity)
    {
        Exception? mismatch = null;
        try
        {
            if ((File.GetAttributes(tombstonePath) & FileAttributes.ReparsePoint) != 0
                || PhysicalPathIdentity.Read(tombstonePath) != expectedIdentity)
            {
                mismatch = new IOException("目录在移动后身份不符，已停止清理。");
            }
        }
        catch (Exception exception)
        {
            mismatch = exception;
        }

        if (mismatch is null)
        {
            return;
        }

        try
        {
            if (Directory.Exists(originalPath) || File.Exists(originalPath))
            {
                throw new IOException("原目录名称已被占用，无法恢复被移动对象。");
            }

            Directory.Move(tombstonePath, originalPath);
        }
        catch (Exception restoreException)
        {
            throw new InvalidOperationException(
                "目录在移动后身份不符，且无法恢复其原始名称；未执行递归删除。",
                new AggregateException(mismatch, restoreException));
        }

        throw new InvalidOperationException(
            "目录在移动后身份不符，已恢复其原始名称且未执行递归删除。",
            mismatch);
    }

    private static void DeleteDirectoryTreeWithoutFollowingLinks(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        if ((File.GetAttributes(fullDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(fullDirectory, recursive: false);
            return;
        }

        using (PhysicalPathIdentity.ProtectDirectory(fullDirectory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(fullDirectory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    DeleteDirectoryTreeWithoutFollowingLinks(entry);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }

        Directory.Delete(fullDirectory, recursive: false);
    }

    private async Task CreateSqliteSnapshotAsync(string snapshotPath, CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForPathAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(
            () =>
            {
                _hooks.BeforeNonInterruptibleSqliteBackup?.Invoke();
                source.BackupDatabase(destination);
            },
            CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task PrepareLiveDatabaseForReplacementAsync(CancellationToken cancellationToken)
    {
        SqliteConnection.ClearAllPools();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || reader.GetInt32(0) != 0)
            {
                throw new IOException("数据库日志仍在使用，无法安全恢复。");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        _hooks.BeforeSidecarDeletion?.Invoke(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
    }

    private static async Task EnsureDatabaseIntegrityAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackupValidationException("备份数据库完整性检查失败。");
        }
    }

    private static async Task EnsureDatabaseIntegrityAndForeignKeysAsync(string databasePath, CancellationToken cancellationToken)
    {
        await EnsureDatabaseIntegrityAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new BackupValidationException("恢复候选数据库的外键检查失败。");
        }
    }

    private static async Task EnsureCandidateMatchesManifestAsync(
        string databasePath,
        BackupManifestInfo manifest,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(databasePath).Length != manifest.DbByteLength)
        {
            throw new BackupValidationException("恢复候选数据库大小与清单不一致。");
        }

        var actualHash = await ComputeSha256Async(databasePath, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash),
                Convert.FromHexString(manifest.Sha256)))
        {
            throw new BackupValidationException("恢复候选数据库哈希与清单不一致。");
        }
    }

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static string GetDirectChildPath(string root, string childName)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, childName));
        if (!string.Equals(Path.GetDirectoryName(candidate), fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("备份目标路径不能超出备份根目录。");
        }

        return candidate;
    }

    private void TryDeleteDirectory(
        string root,
        string path,
        PhysicalPathIdentity? expectedIdentity)
    {
        try
        {
            if (expectedIdentity is not null && Directory.Exists(path))
            {
                _hooks.BeforeTemporaryDirectoryDeletion?.Invoke(path);
                DeleteDirectChildDirectorySafely(root, path, expectedIdentity.Value);
            }
        }
        catch
        {
            // Preserve the original operation failure.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Preserve the original operation result.
        }
    }
}

internal sealed class BackupServiceHooks
{
    internal static BackupServiceHooks Empty { get; } = new();

    internal Action<string>? BeforeSidecarDeletion { get; init; }

    internal Action? BeforeNonInterruptibleSqliteBackup { get; init; }

    internal Action? BeforeMaintenanceLeaseAcquisition { get; init; }

    internal Action<string>? BeforeFinalCandidateValidation { get; init; }

    internal Action<string>? AfterFinalCandidateValidation { get; init; }

    internal Action<string>? BeforePruneDeletion { get; init; }

    internal Action<string>? BeforeTemporaryDirectoryDeletion { get; init; }

    internal Action<string, string>? AfterDirectoryMovedToTombstone { get; init; }

    internal Action<string>? AfterTemporaryDirectoryPublished { get; init; }
}
