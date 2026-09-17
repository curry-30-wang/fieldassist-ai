using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AsphaltPlantManager.Core.Backup;
using AsphaltPlantManager.Core.MasterData;
using AsphaltPlantManager.Infrastructure.Backup;
using AsphaltPlantManager.Infrastructure.Database;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Backup;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"AsphaltPlantManager-Backup-{Guid.NewGuid():N}");

    [Fact]
    public void Default_paths_are_returned_without_accessing_user_data()
    {
        var service = new BackupService();

        service.DefaultDatabasePath.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsphaltPlantManager",
            "Data",
            "asphalt.db"));
        service.DefaultDestinationRoot.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents",
            "沥青拌合站管理系统备份"));
    }

    [Fact]
    public async Task CreateAsync_produces_a_valid_independent_snapshot_and_keeps_the_live_database_writable()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "backup-state");
        var service = new BackupService(databasePath, appVersion: "8.0-test");

        var backupPath = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);

        Directory.GetParent(backupPath)!.FullName.Should().Be(Path.GetFullPath(backupRoot));
        File.Exists(Path.Combine(backupPath, "asphalt.db")).Should().BeTrue();
        File.Exists(Path.Combine(backupPath, "manifest.json")).Should().BeTrue();
        var validation = await service.ValidateAsync(backupPath, CancellationToken.None);
        validation.IsValid.Should().BeTrue();
        validation.Manifest.SchemaVersion.Should().Be(1);
        validation.Manifest.Reason.Should().Be(BackupReason.Manual);
        validation.Manifest.SourceDbFilename.Should().Be("asphalt.db");
        validation.Manifest.AppVersion.Should().Be("8.0-test");
        validation.Manifest.DbByteLength.Should().Be(new FileInfo(Path.Combine(backupPath, "asphalt.db")).Length);
        validation.Manifest.Sha256.Should().Be(await ComputeSha256Async(Path.Combine(backupPath, "asphalt.db")));
        validation.Manifest.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        (await ReadValuesAsync(Path.Combine(backupPath, "asphalt.db"))).Should().Equal("backup-state");

        await InsertValueAsync(databasePath, "after-backup");
        (await ReadValuesAsync(databasePath)).Should().Equal("backup-state", "after-backup");
        (await ReadValuesAsync(Path.Combine(backupPath, "asphalt.db"))).Should().Equal("backup-state");

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(backupPath, "manifest.json")));
        manifest.RootElement.GetProperty("createdAt").GetString().Should().EndWith("Z");
    }

    [Fact]
    public async Task CreateAsync_uses_unique_direct_child_directories_when_the_clock_does_not_advance()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "state");
        var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-09T03:30:00Z"));
        var service = new BackupService(databasePath, clock, "test");

        var first = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        var second = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);

        second.Should().NotBe(first);
        Directory.GetParent(first)!.FullName.Should().Be(Path.GetFullPath(backupRoot));
        Directory.GetParent(second)!.FullName.Should().Be(Path.GetFullPath(backupRoot));
        Path.GetFileName(first).Should().MatchRegex(@"^20260809-033000-manual-[0-9a-f]{12}$");
        Directory.EnumerateDirectories(backupRoot).Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_cleans_its_temporary_directory_on_cancellation_and_preserves_existing_backups()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "state");
        var existingService = new BackupService(databasePath, new FixedTimeProvider(DateTimeOffset.Parse("2026-08-09T03:00:00Z")), "test");
        var existingBackup = await existingService.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var cancellingService = new BackupService(databasePath, new CancelOnSecondReadTimeProvider(cancellation), "test");

        var create = () => cancellingService.CreateAsync(BackupReason.Manual, backupRoot, cancellation.Token);

        await create.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(existingBackup).Should().BeTrue();
        Directory.EnumerateDirectories(backupRoot).Should().Equal(existingBackup);
    }

    [Fact]
    public async Task CreateAsync_temporary_cleanup_never_follows_a_swapped_junction()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        var externalRoot = Path.Combine(_root, "outside-temporary-root");
        var sentinel = Path.Combine(externalRoot, "sentinel.txt");
        await CreateDatabaseAsync(databasePath, "state");
        Directory.CreateDirectory(externalRoot);
        await File.WriteAllTextAsync(sentinel, "keep");
        using var cancellation = new CancellationTokenSource();
        string? swappedPath = null;
        var hooks = new BackupServiceHooks
        {
            BeforeTemporaryDirectoryDeletion = temporaryPath =>
            {
                swappedPath = temporaryPath;
                Directory.Move(temporaryPath, temporaryPath + ".original");
                CreateJunction(temporaryPath, externalRoot);
            }
        };
        var service = new BackupService(databasePath, new CancelOnSecondReadTimeProvider(cancellation), "test", hooks);

        var create = () => service.CreateAsync(BackupReason.Manual, backupRoot, cancellation.Token);

        await create.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(sentinel).Should().BeTrue();
        if (swappedPath is not null && Directory.Exists(swappedPath))
        {
            Directory.Delete(swappedPath);
        }
    }

    [Fact]
    public async Task CreateAsync_checks_cancellation_before_return_and_removes_a_just_published_backup()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "state");
        using var cancellation = new CancellationTokenSource();
        var hooks = new BackupServiceHooks
        {
            AfterTemporaryDirectoryPublished = _ => cancellation.Cancel()
        };
        var service = new BackupService(databasePath, null, "test", hooks);

        var create = () => service.CreateAsync(BackupReason.Manual, backupRoot, cancellation.Token);

        await create.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateDirectories(backupRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_observes_cancellation_after_the_non_interruptible_sqlite_backup_before_publishing()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "state");
        using var cancellation = new CancellationTokenSource();
        var hooks = new BackupServiceHooks
        {
            BeforeNonInterruptibleSqliteBackup = cancellation.Cancel
        };
        var service = new BackupService(databasePath, null, "test", hooks);

        var create = () => service.CreateAsync(BackupReason.Manual, backupRoot, cancellation.Token);

        await create.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateDirectories(backupRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_manifest_source_filename_that_escapes_the_backup_directory()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "state");
        var service = new BackupService(databasePath, appVersion: "test");
        var backupPath = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        var manifestPath = Path.Combine(backupPath, "manifest.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        manifest["sourceDbFilename"] = "../outside.db";
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        var validate = () => service.ValidateAsync(backupPath, CancellationToken.None);

        var exception = await validate.Should().ThrowAsync<BackupValidationException>();
        exception.Which.Message.Should().MatchRegex(@"[\u4e00-\u9fff]");
    }

    [Fact]
    public async Task ValidateAsync_rejects_missing_files_bad_json_hash_mismatch_and_corrupt_sqlite_with_Chinese_errors()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "state");
        var service = new BackupService(databasePath, appVersion: "test");

        var missingFileBackup = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        File.Delete(Path.Combine(missingFileBackup, "asphalt.db"));
        await AssertInvalidBackupHasChineseMessageAsync(service, missingFileBackup);

        var badJsonBackup = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(badJsonBackup, "manifest.json"), "{ bad json");
        await AssertInvalidBackupHasChineseMessageAsync(service, badJsonBackup);

        var badHashBackup = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await SetManifestPropertyAsync(badHashBackup, "sha256", new string('0', 64));
        await AssertInvalidBackupHasChineseMessageAsync(service, badHashBackup);

        var corruptDatabaseBackup = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        var corruptPath = Path.Combine(corruptDatabaseBackup, "asphalt.db");
        await File.WriteAllBytesAsync(corruptPath, "not a sqlite database"u8.ToArray());
        await SetManifestPropertyAsync(corruptDatabaseBackup, "dbByteLength", new FileInfo(corruptPath).Length);
        await SetManifestPropertyAsync(corruptDatabaseBackup, "sha256", await ComputeSha256Async(corruptPath));
        await AssertInvalidBackupHasChineseMessageAsync(service, corruptDatabaseBackup);
    }

    [Fact]
    public async Task RestoreAsync_rejects_an_invalid_backup_before_touching_the_current_database()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "current-state");
        var service = new BackupService(databasePath, appVersion: "test");
        var backupPath = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await SetManifestPropertyAsync(backupPath, "sha256", new string('f', 64));
        var before = await File.ReadAllBytesAsync(databasePath);

        var restore = () => service.RestoreAsync(backupPath, CancellationToken.None);

        var exception = await restore.Should().ThrowAsync<BackupValidationException>();
        exception.Which.Message.Should().MatchRegex(@"[\u4e00-\u9fff]");
        (await File.ReadAllBytesAsync(databasePath)).Should().Equal(before);
    }

    [Fact]
    public async Task RestoreAsync_restores_the_snapshot_keeps_a_valid_pre_restore_backup_and_removes_old_sidecars()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "backup-state");
        var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-09T04:00:00Z"));
        var service = new BackupService(databasePath, clock, "test");
        var backupPath = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await InsertValueAsync(databasePath, "pre-restore-state");

        await service.RestoreAsync(backupPath, CancellationToken.None);

        File.Exists(databasePath + "-wal").Should().BeFalse();
        File.Exists(databasePath + "-shm").Should().BeFalse();
        (await ReadValuesAsync(databasePath)).Should().Equal("backup-state");
        var preRestoreDirectories = Directory.EnumerateDirectories(Path.GetDirectoryName(databasePath)!, ".pre-restore-*").ToArray();
        preRestoreDirectories.Should().ContainSingle();
        var preRestorePath = preRestoreDirectories[0];
        var validation = await service.ValidateAsync(preRestorePath, CancellationToken.None);
        validation.Manifest.Reason.Should().Be(BackupReason.PreRestore);
        (await ReadValuesAsync(Path.Combine(preRestorePath, "asphalt.db"))).Should().Equal("backup-state", "pre-restore-state");
    }

    [Fact]
    public async Task RestoreAsync_does_not_overwrite_the_database_when_atomic_replace_is_denied()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "backup-state");
        var service = new BackupService(databasePath, appVersion: "test");
        var backupPath = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await InsertValueAsync(databasePath, "current-state");
        var before = await File.ReadAllBytesAsync(databasePath);

        await using (var lockFile = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var restore = () => service.RestoreAsync(backupPath, CancellationToken.None);
            var exception = await restore.Should().ThrowAsync<InvalidOperationException>();
            exception.Which.Message.Should().MatchRegex(@"[\u4e00-\u9fff]");
        }

        (await File.ReadAllBytesAsync(databasePath)).Should().Equal(before);
        (await ReadValuesAsync(databasePath)).Should().Equal("backup-state", "current-state");
    }

    [Fact]
    public async Task RestoreAsync_releases_idle_sqlite_pool_handles_before_atomic_replace()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "backup-state");
        var service = new BackupService(databasePath, appVersion: "test");
        var backupPath = await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await InsertValueWithDefaultPoolingAsync(databasePath, "pooled-current-state");

        await service.RestoreAsync(backupPath, CancellationToken.None);

        (await ReadValuesAsync(databasePath)).Should().Equal("backup-state");
    }

    [Fact]
    public async Task RestoreAsync_sidecar_delete_failure_happens_before_replace_and_preserves_the_complete_old_state()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "backup-state");
        var baselineService = new BackupService(databasePath, appVersion: "test");
        var backupPath = await baselineService.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await InsertValueAsync(databasePath, "current-state");
        string[]? stateSeenAtSidecarCleanup = null;
        FileStream? sidecarLock = null;
        var hooks = new BackupServiceHooks
        {
            BeforeSidecarDeletion = path =>
            {
                stateSeenAtSidecarCleanup = ReadValues(path);
                var walPath = path + "-wal";
                File.WriteAllBytes(walPath, "locked sidecar"u8.ToArray());
                sidecarLock = new FileStream(walPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
        };
        var service = new BackupService(databasePath, null, "test", hooks);

        try
        {
            var restore = () => service.RestoreAsync(backupPath, CancellationToken.None);
            var exception = await restore.Should().ThrowAsync<InvalidOperationException>();
            exception.Which.Message.Should().Contain("未被覆盖");
        }
        finally
        {
            sidecarLock?.Dispose();
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
        }

        stateSeenAtSidecarCleanup.Should().Equal("backup-state", "current-state");
        (await ReadValuesAsync(databasePath)).Should().Equal("backup-state", "current-state");
    }

    [Fact]
    public async Task RestoreAsync_rechecks_candidate_identity_and_contents_immediately_before_replace()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "backup-state");
        var baselineService = new BackupService(databasePath, appVersion: "test");
        var backupPath = await baselineService.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await InsertValueAsync(databasePath, "current-state");
        var hooks = new BackupServiceHooks
        {
            BeforeFinalCandidateValidation = candidatePath =>
            {
                var originalCandidatePath = candidatePath + ".swapped";
                File.Move(candidatePath, originalCandidatePath);
                File.Copy(originalCandidatePath, candidatePath);
                File.Delete(originalCandidatePath);
            }
        };
        var service = new BackupService(databasePath, null, "test", hooks);

        var restore = () => service.RestoreAsync(backupPath, CancellationToken.None);

        var exception = await restore.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("未被覆盖");
        (await ReadValuesAsync(databasePath)).Should().Equal("backup-state", "current-state");
    }

    [Fact]
    public async Task RestoreAsync_stops_before_replace_if_a_live_sidecar_reappears_after_cleanup()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "backup-state");
        var baselineService = new BackupService(databasePath, appVersion: "test");
        var backupPath = await baselineService.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await InsertValueAsync(databasePath, "current-state");
        var hooks = new BackupServiceHooks
        {
            BeforeFinalCandidateValidation = _ => File.WriteAllBytes(databasePath + "-wal", "reappeared sidecar"u8.ToArray())
        };
        var service = new BackupService(databasePath, null, "test", hooks);

        try
        {
            var restore = () => service.RestoreAsync(backupPath, CancellationToken.None);
            var exception = await restore.Should().ThrowAsync<InvalidOperationException>();
            exception.Which.Message.Should().Contain("未被覆盖");
        }
        finally
        {
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
        }

        (await ReadValuesAsync(databasePath)).Should().Equal("backup-state", "current-state");
    }

    [Fact]
    public async Task RestoreAsync_blocks_app_writes_after_final_validation_until_the_new_database_is_installed()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "backup-state");
        await new DatabaseInitializer($"Data Source={databasePath};Pooling=False").InitializeAsync(CancellationToken.None);
        var baselineService = new BackupService(databasePath, appVersion: "test");
        var backupPath = await baselineService.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await InsertValueAsync(databasePath, "current-state");
        var repository = new SqliteMasterDataRepository($"Data Source={databasePath};Pooling=False");
        Task? writeTask = null;
        var writeCompletedBeforeCommit = false;
        var hooks = new BackupServiceHooks
        {
            AfterFinalCandidateValidation = _ =>
            {
                writeTask = repository.UpsertAsync(
                    new MasterDataItem("plant", "gate-test", "new-database", DateTimeOffset.UtcNow),
                    CancellationToken.None);
                Thread.Sleep(TimeSpan.FromMilliseconds(500));
                writeCompletedBeforeCommit = writeTask.IsCompleted;
            }
        };
        var service = new BackupService(databasePath, null, "test", hooks);

        await service.RestoreAsync(backupPath, CancellationToken.None);
        await writeTask!.WaitAsync(TimeSpan.FromSeconds(5));

        writeCompletedBeforeCommit.Should().BeFalse();
        (await ReadValuesAsync(databasePath)).Should().Equal("backup-state");
        (await repository.GetAsync("plant", "gate-test", CancellationToken.None))!.Value.Should().Be("new-database");
    }

    [Fact]
    public async Task RestoreAsync_waits_for_an_existing_app_database_access_before_checkpointing()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        await CreateDatabaseAsync(databasePath, "backup-state");
        var baselineService = new BackupService(databasePath, appVersion: "test");
        var backupPath = await baselineService.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await InsertValueAsync(databasePath, "current-state");
        var acquisitionStarting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpointStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new BackupService(databasePath, null, "test", new BackupServiceHooks
        {
            BeforeMaintenanceLeaseAcquisition = acquisitionStarting.SetResult,
            BeforeSidecarDeletion = _ => checkpointStarted.SetResult()
        });
        var access = await SqliteDatabaseAccess.AcquireReadForPathAsync(databasePath, CancellationToken.None);
        await using var existingConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await existingConnection.OpenAsync();

        var restoreTask = service.RestoreAsync(backupPath, CancellationToken.None);
        await acquisitionStarting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        restoreTask.IsCompleted.Should().BeFalse();
        checkpointStarted.Task.IsCompleted.Should().BeFalse();
        await existingConnection.DisposeAsync();
        await access.DisposeAsync();
        await restoreTask.WaitAsync(TimeSpan.FromSeconds(5));
        (await ReadValuesAsync(databasePath)).Should().Equal("backup-state");
    }

    [Fact]
    public async Task RestoreAsync_does_not_install_a_candidate_replaced_after_final_validation()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        var maliciousPath = Path.Combine(_root, "malicious.db");
        await CreateDatabaseAsync(databasePath, "backup-state");
        await CreateDatabaseAsync(maliciousPath, "malicious-state");
        var baselineService = new BackupService(databasePath, appVersion: "test");
        var backupPath = await baselineService.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        await InsertValueAsync(databasePath, "current-state");
        var mutationRejected = false;
        var mutationAttempted = false;
        var hooks = new BackupServiceHooks
        {
            AfterFinalCandidateValidation = candidatePath =>
            {
                mutationAttempted = true;
                try
                {
                    File.Copy(maliciousPath, candidatePath, overwrite: true);
                }
                catch (IOException)
                {
                    mutationRejected = true;
                }
            }
        };
        var service = new BackupService(databasePath, null, "test", hooks);

        var failure = await Record.ExceptionAsync(() => service.RestoreAsync(backupPath, CancellationToken.None));

        mutationAttempted.Should().BeTrue();
        var restoredValues = await ReadValuesAsync(databasePath);
        restoredValues.Should().NotContain("malicious-state");
        if (mutationRejected)
        {
            failure.Should().BeNull();
            restoredValues.Should().Equal("backup-state");
        }
        else
        {
            failure.Should().BeOfType<InvalidOperationException>();
            restoredValues.Should().Equal("backup-state", "current-state");
        }
    }

    [Fact]
    public async Task PruneAsync_keeps_the_latest_thirty_valid_app_backups_without_touching_other_paths()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        var outsideMarker = Path.Combine(_root, "outside.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(outsideMarker, "keep");
        await CreateDatabaseAsync(databasePath, "state");
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        var service = new BackupService(databasePath, clock, "test");
        var backups = new List<string>();
        for (var index = 0; index < 32; index++)
        {
            backups.Add(await service.CreateAsync(BackupReason.AutomaticDaily, backupRoot, CancellationToken.None));
            clock.Advance(TimeSpan.FromDays(1));
        }

        var unrelated = Path.Combine(backupRoot, "用户自己的资料");
        Directory.CreateDirectory(unrelated);
        await File.WriteAllTextAsync(Path.Combine(unrelated, "keep.txt"), "keep");
        var invalidLookalike = Path.Combine(backupRoot, "20260101-000000-manual-000000000000");
        Directory.CreateDirectory(invalidLookalike);
        await File.WriteAllTextAsync(Path.Combine(invalidLookalike, "keep.txt"), "keep");

        await service.PruneAsync(backupRoot, 30, CancellationToken.None);

        Directory.Exists(backups[0]).Should().BeFalse();
        Directory.Exists(backups[1]).Should().BeFalse();
        backups.Skip(2).Should().OnlyContain(path => Directory.Exists(path));
        Directory.Exists(unrelated).Should().BeTrue();
        Directory.Exists(invalidLookalike).Should().BeTrue();
        File.Exists(outsideMarker).Should().BeTrue();

        var invalidCount = () => service.PruneAsync(backupRoot, 0, CancellationToken.None);
        var exception = await invalidCount.Should().ThrowAsync<ArgumentOutOfRangeException>();
        exception.Which.Message.Should().MatchRegex(@"[\u4e00-\u9fff]");
    }

    [Fact]
    public async Task PruneAsync_rejects_a_reparse_root_without_deleting_external_backups()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var externalRoot = Path.Combine(_root, "external-backups");
        var linkedRoot = Path.Combine(_root, "linked-backups");
        await CreateDatabaseAsync(databasePath, "state");
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        var service = new BackupService(databasePath, clock, "test");
        var oldest = await service.CreateAsync(BackupReason.AutomaticDaily, externalRoot, CancellationToken.None);
        var sentinel = Path.Combine(oldest, "external-sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "keep");
        clock.Advance(TimeSpan.FromDays(1));
        _ = await service.CreateAsync(BackupReason.AutomaticDaily, externalRoot, CancellationToken.None);
        CreateJunction(linkedRoot, externalRoot);

        try
        {
            var prune = () => service.PruneAsync(linkedRoot, 1, CancellationToken.None);
            await prune.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            if (Directory.Exists(linkedRoot))
            {
                Directory.Delete(linkedRoot);
            }
        }

        Directory.Exists(oldest).Should().BeTrue();
        File.Exists(sentinel).Should().BeTrue();
    }

    [Fact]
    public async Task PruneAsync_candidate_swap_to_junction_never_deletes_the_external_target()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        var externalRoot = Path.Combine(_root, "outside-prune-root");
        var sentinel = Path.Combine(externalRoot, "sentinel.txt");
        await CreateDatabaseAsync(databasePath, "state");
        Directory.CreateDirectory(externalRoot);
        await File.WriteAllTextAsync(sentinel, "keep");
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        string? swappedCandidate = null;
        var hooks = new BackupServiceHooks
        {
            BeforePruneDeletion = candidatePath =>
            {
                swappedCandidate = candidatePath;
                Directory.Move(candidatePath, candidatePath + ".original");
                CreateJunction(candidatePath, externalRoot);
            }
        };
        var service = new BackupService(databasePath, clock, "test", hooks);
        _ = await service.CreateAsync(BackupReason.AutomaticDaily, backupRoot, CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(1));
        _ = await service.CreateAsync(BackupReason.AutomaticDaily, backupRoot, CancellationToken.None);

        var prune = () => service.PruneAsync(backupRoot, 1, CancellationToken.None);
        await prune.Should().ThrowAsync<InvalidOperationException>();

        File.Exists(sentinel).Should().BeTrue();
        if (swappedCandidate is not null && Directory.Exists(swappedCandidate))
        {
            Directory.Delete(swappedCandidate);
        }
    }

    [Fact]
    public async Task PruneAsync_candidate_swap_to_a_real_user_directory_keeps_its_name_and_contents()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        var userDirectory = Path.Combine(_root, "user-owned-directory");
        var sentinel = Path.Combine(userDirectory, "sentinel.txt");
        await CreateDatabaseAsync(databasePath, "state");
        Directory.CreateDirectory(userDirectory);
        await File.WriteAllTextAsync(sentinel, "keep");
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        string? candidateName = null;
        var hooks = new BackupServiceHooks
        {
            BeforePruneDeletion = candidatePath =>
            {
                candidateName = candidatePath;
                Directory.Move(candidatePath, candidatePath + ".app-original");
                Directory.Move(userDirectory, candidatePath);
            }
        };
        var service = new BackupService(databasePath, clock, "test", hooks);
        _ = await service.CreateAsync(BackupReason.AutomaticDaily, backupRoot, CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(1));
        _ = await service.CreateAsync(BackupReason.AutomaticDaily, backupRoot, CancellationToken.None);

        var prune = () => service.PruneAsync(backupRoot, 1, CancellationToken.None);
        await prune.Should().ThrowAsync<InvalidOperationException>();

        candidateName.Should().NotBeNull();
        Directory.Exists(candidateName!).Should().BeTrue();
        File.Exists(Path.Combine(candidateName!, "sentinel.txt")).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(candidateName!, "sentinel.txt"))).Should().Be("keep");
        Directory.EnumerateDirectories(backupRoot, ".delete-*.tombstone").Should().BeEmpty();
    }

    [Fact]
    public async Task PruneAsync_ancestor_swap_to_junction_cannot_redirect_deletion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protectedAncestor = Path.Combine(_root, "protected-ancestor");
        var backupRoot = Path.Combine(protectedAncestor, "backups");
        var movedAncestor = Path.Combine(_root, "moved-ancestor");
        var externalAncestor = Path.Combine(_root, "external-ancestor");
        var externalBackupRoot = Path.Combine(externalAncestor, "backups");
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        await CreateDatabaseAsync(databasePath, "state");
        Directory.CreateDirectory(externalBackupRoot);
        var sentinel = Path.Combine(externalBackupRoot, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "keep");
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        var hooks = new BackupServiceHooks
        {
            BeforePruneDeletion = _ =>
            {
                Directory.Move(protectedAncestor, movedAncestor);
                CreateJunction(protectedAncestor, externalAncestor);
            }
        };
        var service = new BackupService(databasePath, clock, "test", hooks);
        _ = await service.CreateAsync(BackupReason.AutomaticDaily, backupRoot, CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(1));
        _ = await service.CreateAsync(BackupReason.AutomaticDaily, backupRoot, CancellationToken.None);

        var prune = () => service.PruneAsync(backupRoot, 1, CancellationToken.None);
        await prune.Should().ThrowAsync<IOException>();

        File.Exists(sentinel).Should().BeTrue();
        (await File.ReadAllTextAsync(sentinel)).Should().Be("keep");
        Directory.Exists(protectedAncestor).Should().BeTrue();
        Directory.Exists(movedAncestor).Should().BeFalse();
    }

    [Fact]
    public async Task PruneAsync_restores_a_moved_object_when_post_move_identity_mismatches()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var backupRoot = Path.Combine(_root, "backups");
        var userDirectory = Path.Combine(_root, "user-owned-directory");
        await CreateDatabaseAsync(databasePath, "state");
        Directory.CreateDirectory(userDirectory);
        await File.WriteAllTextAsync(Path.Combine(userDirectory, "sentinel.txt"), "keep");
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        string? originalPath = null;
        var hooks = new BackupServiceHooks
        {
            AfterDirectoryMovedToTombstone = (sourcePath, tombstonePath) =>
            {
                originalPath = sourcePath;
                Directory.Move(tombstonePath, tombstonePath + ".app-original");
                Directory.Move(userDirectory, tombstonePath);
            }
        };
        var service = new BackupService(databasePath, clock, "test", hooks);
        _ = await service.CreateAsync(BackupReason.AutomaticDaily, backupRoot, CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(1));
        _ = await service.CreateAsync(BackupReason.AutomaticDaily, backupRoot, CancellationToken.None);

        var prune = () => service.PruneAsync(backupRoot, 1, CancellationToken.None);
        await prune.Should().ThrowAsync<InvalidOperationException>();

        originalPath.Should().NotBeNull();
        Directory.Exists(originalPath!).Should().BeTrue();
        File.Exists(Path.Combine(originalPath!, "sentinel.txt")).Should().BeTrue();
        Directory.EnumerateDirectories(backupRoot, ".delete-*.tombstone").Should().BeEmpty();
    }

    [Fact]
    public async Task Daily_coordinator_uses_local_dates_persists_success_and_does_not_count_manual_backups()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var backupRoot = Path.Combine(_root, "backups");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await new DatabaseInitializer(connectionString).InitializeAsync(CancellationToken.None);
        var localZone = TimeZoneInfo.CreateCustomTimeZone("UTC+08-test", TimeSpan.FromHours(8), "UTC+08-test", "UTC+08-test");
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-08T12:00:00Z"), localZone);
        var service = new BackupService(databasePath, clock, "test");
        await service.CreateAsync(BackupReason.Manual, backupRoot, CancellationToken.None);
        var coordinator = new DailyBackupCoordinator(service, connectionString, backupRoot, clock);

        (await coordinator.TryCreateOnNormalExitAsync(CancellationToken.None)).Should().BeTrue();
        (await coordinator.TryCreateOnNormalExitAsync(CancellationToken.None)).Should().BeFalse();
        var rebuiltCoordinator = new DailyBackupCoordinator(service, connectionString, backupRoot, clock);
        (await rebuiltCoordinator.TryCreateOnNormalExitAsync(CancellationToken.None)).Should().BeFalse();

        clock.Advance(TimeSpan.FromHours(5));
        (await rebuiltCoordinator.TryCreateOnNormalExitAsync(CancellationToken.None)).Should().BeTrue();

        var reasons = new List<BackupReason>();
        foreach (var directory in Directory.EnumerateDirectories(backupRoot))
        {
            reasons.Add((await service.ValidateAsync(directory, CancellationToken.None)).Manifest.Reason);
        }

        reasons.Should().ContainSingle(reason => reason == BackupReason.Manual);
        reasons.Count(reason => reason == BackupReason.AutomaticDaily).Should().Be(2);
        (await ReadSettingAsync(connectionString, "last_successful_daily_backup_date")).Should().Be("2026-08-09");
    }

    [Fact]
    public async Task Daily_coordinator_retries_after_failure_and_cancellation_without_marking_the_day_complete()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var backupRoot = Path.Combine(_root, "backups");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await new DatabaseInitializer(connectionString).InitializeAsync(CancellationToken.None);
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-08T12:00:00Z"));
        var realService = new BackupService(databasePath, clock, "test");
        var failOnceService = new FailOnceBackupService(realService);
        var coordinator = new DailyBackupCoordinator(failOnceService, connectionString, backupRoot, clock);

        var failed = () => coordinator.TryCreateOnNormalExitAsync(CancellationToken.None);
        await failed.Should().ThrowAsync<IOException>();
        (await ReadSettingAsync(connectionString, "last_successful_daily_backup_date")).Should().BeNull();
        Directory.Exists(backupRoot).Should().BeFalse();

        (await coordinator.TryCreateOnNormalExitAsync(CancellationToken.None)).Should().BeTrue();
        (await ReadSettingAsync(connectionString, "last_successful_daily_backup_date")).Should().Be("2026-08-08");

        clock.Advance(TimeSpan.FromDays(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => coordinator.TryCreateOnNormalExitAsync(cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        (await ReadSettingAsync(connectionString, "last_successful_daily_backup_date")).Should().Be("2026-08-08");
        (await coordinator.TryCreateOnNormalExitAsync(CancellationToken.None)).Should().BeTrue();
        (await ReadSettingAsync(connectionString, "last_successful_daily_backup_date")).Should().Be("2026-08-09");
    }

    [Fact]
    public async Task Daily_coordinator_retries_after_prune_failure_without_marking_the_day_complete()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var backupRoot = Path.Combine(_root, "backups");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await new DatabaseInitializer(connectionString).InitializeAsync(CancellationToken.None);
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-08T12:00:00Z"));
        var realService = new BackupService(databasePath, clock, "test");
        var pruneFailOnceService = new PruneFailOnceBackupService(realService);
        var coordinator = new DailyBackupCoordinator(pruneFailOnceService, connectionString, backupRoot, clock);

        var failed = () => coordinator.TryCreateOnNormalExitAsync(CancellationToken.None);
        await failed.Should().ThrowAsync<IOException>();
        (await ReadSettingAsync(connectionString, "last_successful_daily_backup_date")).Should().BeNull();

        (await coordinator.TryCreateOnNormalExitAsync(CancellationToken.None)).Should().BeTrue();
        (await ReadSettingAsync(connectionString, "last_successful_daily_backup_date")).Should().Be("2026-08-08");
    }

    [Fact]
    public async Task Daily_coordinator_serializes_two_instances_and_creates_only_one_daily_backup()
    {
        var databasePath = Path.Combine(_root, "data", "asphalt.db");
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var backupRoot = Path.Combine(_root, "backups");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await new DatabaseInitializer(connectionString).InitializeAsync(CancellationToken.None);
        var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-08T12:00:00Z"));
        var service = new BlockingBackupService();
        var firstCoordinator = new DailyBackupCoordinator(service, connectionString, backupRoot, clock);
        var secondCoordinator = new DailyBackupCoordinator(service, connectionString, backupRoot, clock);

        var first = firstCoordinator.TryCreateOnNormalExitAsync(CancellationToken.None);
        await service.FirstCreateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = secondCoordinator.TryCreateOnNormalExitAsync(CancellationToken.None);
        var secondEnteredBeforeRelease = await Task.WhenAny(
            service.SecondCreateEntered.Task,
            Task.Delay(TimeSpan.FromMilliseconds(300))) == service.SecondCreateEntered.Task;
        service.ReleaseFirstCreate.TrySetResult();
        var results = await Task.WhenAll(first, second);

        secondEnteredBeforeRelease.Should().BeFalse();
        service.CreateCallCount.Should().Be(1);
        results.Should().ContainSingle(result => result);
        results.Should().ContainSingle(result => !result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static async Task CreateDatabaseAsync(string databasePath, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE samples (id INTEGER PRIMARY KEY, value TEXT NOT NULL); INSERT INTO samples(value) VALUES ($value);";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertValueAsync(string databasePath, string value)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO samples(value) VALUES ($value);";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertValueWithDefaultPoolingAsync(string databasePath, string value)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO samples(value) VALUES ($value);";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ReadValuesAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM samples ORDER BY id;";
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static string[] ReadValues(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM samples ORDER BY id;";
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static async Task SetManifestPropertyAsync(string backupPath, string propertyName, object value)
    {
        var manifestPath = Path.Combine(backupPath, "manifest.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        manifest[propertyName] = value switch
        {
            string text => text,
            long number => number,
            int number => number,
            _ => throw new ArgumentException("Unsupported manifest test value.", nameof(value))
        };
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private static void CreateJunction(string linkPath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "/d", "/c", "mklink", "/J", linkPath, targetPath }
        }) ?? throw new InvalidOperationException("Could not start junction creation process.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }
    }

    private static async Task AssertInvalidBackupHasChineseMessageAsync(BackupService service, string backupPath)
    {
        var validate = () => service.ValidateAsync(backupPath, CancellationToken.None);
        var exception = await validate.Should().ThrowAsync<BackupValidationException>();
        exception.Which.Message.Should().MatchRegex(@"[\u4e00-\u9fff]");
    }

    private static async Task<string?> ReadSettingAsync(string connectionString, string key)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE setting_key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync() as string;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CancelOnSecondReadTimeProvider(CancellationTokenSource cancellation) : TimeProvider
    {
        private int _reads;

        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Increment(ref _reads) == 2)
            {
                cancellation.Cancel();
            }

            return DateTimeOffset.Parse("2026-08-09T03:00:00Z");
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow, TimeZoneInfo? localTimeZone = null) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override TimeZoneInfo LocalTimeZone { get; } = localTimeZone ?? TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow += value;
    }

    private sealed class FailOnceBackupService(IBackupService inner) : IBackupService
    {
        private bool _shouldFail = true;

        public string DefaultDatabasePath => inner.DefaultDatabasePath;

        public string DefaultDestinationRoot => inner.DefaultDestinationRoot;

        public Task<string> CreateAsync(BackupReason reason, string destinationRoot, CancellationToken cancellationToken)
        {
            if (_shouldFail)
            {
                _shouldFail = false;
                throw new IOException("injected backup failure");
            }

            return inner.CreateAsync(reason, destinationRoot, cancellationToken);
        }

        public Task<BackupValidationResult> ValidateAsync(string backupPath, CancellationToken cancellationToken) =>
            inner.ValidateAsync(backupPath, cancellationToken);

        public Task RestoreAsync(string backupPath, CancellationToken cancellationToken) =>
            inner.RestoreAsync(backupPath, cancellationToken);

        public Task PruneAsync(string destinationRoot, int keepCount, CancellationToken cancellationToken) =>
            inner.PruneAsync(destinationRoot, keepCount, cancellationToken);
    }

    private sealed class PruneFailOnceBackupService(IBackupService inner) : IBackupService
    {
        private bool _shouldFail = true;

        public string DefaultDatabasePath => inner.DefaultDatabasePath;

        public string DefaultDestinationRoot => inner.DefaultDestinationRoot;

        public Task<string> CreateAsync(BackupReason reason, string destinationRoot, CancellationToken cancellationToken) =>
            inner.CreateAsync(reason, destinationRoot, cancellationToken);

        public Task<BackupValidationResult> ValidateAsync(string backupPath, CancellationToken cancellationToken) =>
            inner.ValidateAsync(backupPath, cancellationToken);

        public Task RestoreAsync(string backupPath, CancellationToken cancellationToken) =>
            inner.RestoreAsync(backupPath, cancellationToken);

        public Task PruneAsync(string destinationRoot, int keepCount, CancellationToken cancellationToken)
        {
            if (_shouldFail)
            {
                _shouldFail = false;
                throw new IOException("injected prune failure");
            }

            return inner.PruneAsync(destinationRoot, keepCount, cancellationToken);
        }
    }

    private sealed class BlockingBackupService : IBackupService
    {
        private int _createCallCount;

        internal TaskCompletionSource FirstCreateEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource SecondCreateEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseFirstCreate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int CreateCallCount => Volatile.Read(ref _createCallCount);

        public string DefaultDatabasePath => "unused.db";

        public string DefaultDestinationRoot => "unused";

        public async Task<string> CreateAsync(BackupReason reason, string destinationRoot, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _createCallCount);
            if (call == 1)
            {
                FirstCreateEntered.TrySetResult();
                await ReleaseFirstCreate.Task.WaitAsync(cancellationToken);
            }
            else
            {
                SecondCreateEntered.TrySetResult();
            }

            return Path.Combine(destinationRoot, $"backup-{call}");
        }

        public Task<BackupValidationResult> ValidateAsync(string backupPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestoreAsync(string backupPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PruneAsync(string destinationRoot, int keepCount, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
