using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Infrastructure.Database;
using AsphaltPlantManager.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Database;

public sealed class SqliteArchiveRepositoryTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"asphalt-archive-{Guid.NewGuid():N}.db");
    private string _connectionString = null!;
    private SqliteRecordRepository _records = null!;
    private SqliteArchiveRepository _archives = null!;

    public async Task InitializeAsync()
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
        await new DatabaseInitializer(_connectionString).InitializeAsync(CancellationToken.None);
        _records = new SqliteRecordRepository(_connectionString);
        _archives = new SqliteArchiveRepository(_connectionString);
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Seal_persists_record_number_and_first_snapshot_in_one_transaction()
    {
        var record = await SaveCompletedRecordAsync("{\"quantity\":10}");

        var sealedVersion = await _archives.SealAsync(record.Id, "DZ", "首次封存", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);
        var loaded = await _records.GetAsync(record.Id, CancellationToken.None);

        sealedVersion.ArchiveNumber.Should().Be("DZ-202608-0001");
        loaded!.Status.Should().Be(RecordStatus.Sealed);
        loaded.ArchiveNumber.Should().Be(sealedVersion.ArchiveNumber);
        (await _archives.GetVersionsAsync(record.Id, CancellationToken.None)).Should().ContainSingle().Which.PayloadJson.Should().Be("{\"quantity\":10}");
    }

    [Fact]
    public async Task Same_prefix_and_month_concurrent_seals_get_unique_consecutive_numbers()
    {
        var first = await SaveCompletedRecordAsync("{\"quantity\":1}");
        var second = await SaveCompletedRecordAsync("{\"quantity\":2}");
        var now = DateTimeOffset.Parse("2026-08-07T08:00:00+08:00");

        var versions = await Task.WhenAll(
            _archives.SealAsync(first.Id, "DZ", "首次封存", now, CancellationToken.None),
            _archives.SealAsync(second.Id, "DZ", "首次封存", now, CancellationToken.None));

        versions.Select(version => version.ArchiveNumber).Should().BeEquivalentTo("DZ-202608-0001", "DZ-202608-0002");
    }

    [Fact]
    public async Task First_seal_in_a_new_month_restarts_its_sequence_at_one()
    {
        var august = await SaveCompletedRecordAsync("{\"quantity\":1}");
        var september = await SaveCompletedRecordAsync("{\"quantity\":2}");

        var first = await _archives.SealAsync(august.Id, "DZ", "首次封存", DateTimeOffset.Parse("2026-08-31T08:00:00+08:00"), CancellationToken.None);
        var second = await _archives.SealAsync(september.Id, "DZ", "首次封存", DateTimeOffset.Parse("2026-09-01T08:00:00+08:00"), CancellationToken.None);

        first.ArchiveNumber.Should().Be("DZ-202608-0001");
        second.ArchiveNumber.Should().Be("DZ-202609-0001");
    }

    [Fact]
    public async Task Archived_edit_and_restore_append_versions_without_changing_original_snapshot()
    {
        var record = await SaveCompletedRecordAsync("{\"quantity\":10}");
        await _archives.SealAsync(record.Id, "DZ", "首次封存", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);
        await _archives.SaveArchivedEditAsync(record.Id, "{\"quantity\":20}", new Dictionary<string, string> { ["company"] = "新公司" }, "修改", DateTimeOffset.Parse("2026-08-07T09:00:00+08:00"), CancellationToken.None);
        var restored = await _archives.RestoreVersionAsync(record.Id, 1, "恢复", DateTimeOffset.Parse("2026-08-07T10:00:00+08:00"), CancellationToken.None);

        var versions = await _archives.GetVersionsAsync(record.Id, CancellationToken.None);
        versions.Select(version => version.Version).Should().Equal(2, 3);
        versions.Select(version => version.PayloadJson).Should().Equal("{\"quantity\":20}", "{\"quantity\":10}");
        restored.ArchiveNumber.Should().Be("DZ-202608-0001");
    }

    [Fact]
    public async Task Moving_to_trash_and_restoring_are_idempotent_and_keep_versions()
    {
        var record = await SaveCompletedRecordAsync("{\"quantity\":10}");
        await _archives.SealAsync(record.Id, "DZ", "首次封存", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);

        (await _archives.MoveToTrashAsync(record.Id, DateTimeOffset.Parse("2026-08-07T11:00:00+08:00"), CancellationToken.None)).Should().BeTrue();
        (await _archives.MoveToTrashAsync(record.Id, DateTimeOffset.Parse("2026-08-07T11:00:00+08:00"), CancellationToken.None)).Should().BeFalse();
        (await _records.GetAsync(record.Id, CancellationToken.None))!.IsDeleted.Should().BeTrue();
        (await _archives.RestoreFromTrashAsync(record.Id, CancellationToken.None)).Should().BeTrue();
        (await _archives.RestoreFromTrashAsync(record.Id, CancellationToken.None)).Should().BeFalse();
        (await _archives.GetVersionsAsync(record.Id, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task Password_store_uses_verifiable_pbkdf2_material_without_storing_plaintext()
    {
        var passwords = new SqliteUnlockPasswordStore(_connectionString);

        await passwords.SetPasswordAsync("safe password", CancellationToken.None);

        (await passwords.HasPasswordAsync(CancellationToken.None)).Should().BeTrue();
        (await passwords.VerifyAsync("safe password", CancellationToken.None)).Should().BeTrue();
        (await passwords.VerifyAsync("wrong password", CancellationToken.None)).Should().BeFalse();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE setting_key = 'archive_unlock_password';";
        ((string)(await command.ExecuteScalarAsync())!).Should().NotContain("safe password");
    }

    [Fact]
    public async Task Blank_password_is_rejected()
    {
        var passwords = new SqliteUnlockPasswordStore(_connectionString);
        var set = () => passwords.SetPasswordAsync("   ", CancellationToken.None);

        await set.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Replacing_a_password_invalidates_the_previous_password()
    {
        var passwords = new SqliteUnlockPasswordStore(_connectionString);
        await passwords.SetPasswordAsync("first password", CancellationToken.None);
        await passwords.SetPasswordAsync("second password", CancellationToken.None);

        (await passwords.VerifyAsync("first password", CancellationToken.None)).Should().BeFalse();
        (await passwords.VerifyAsync("second password", CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task Seal_rolls_back_the_record_when_version_snapshot_insert_fails()
    {
        var record = await SaveCompletedRecordAsync("{\"quantity\":10}");
        await InstallFailingVersionTriggerAsync();

        var seal = () => _archives.SealAsync(record.Id, "DZ", "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);

        await seal.Should().ThrowAsync<SqliteException>();
        var loaded = await _records.GetAsync(record.Id, CancellationToken.None);
        loaded!.Status.Should().Be(RecordStatus.Completed);
        loaded.ArchiveNumber.Should().BeNull();
        (await _archives.GetVersionsAsync(record.Id, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Archived_edit_rolls_back_the_record_when_version_snapshot_insert_fails()
    {
        var record = await SaveCompletedRecordAsync("{\"quantity\":10}");
        await _archives.SealAsync(record.Id, "DZ", "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);
        await InstallFailingVersionTriggerAsync();

        var save = () => _archives.SaveArchivedEditAsync(record.Id, "{\"quantity\":20}", new Dictionary<string, string> { ["company"] = "new" }, "edit", DateTimeOffset.Parse("2026-08-07T09:00:00+08:00"), CancellationToken.None);

        await save.Should().ThrowAsync<SqliteException>();
        (await _records.GetAsync(record.Id, CancellationToken.None))!.PayloadJson.Should().Be("{\"quantity\":10}");
        (await _archives.GetVersionsAsync(record.Id, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task Restore_rolls_back_the_record_when_version_snapshot_insert_fails()
    {
        var record = await SaveCompletedRecordAsync("{\"quantity\":10}");
        await _archives.SealAsync(record.Id, "DZ", "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);
        await _archives.SaveArchivedEditAsync(record.Id, "{\"quantity\":20}", new Dictionary<string, string> { ["company"] = "new" }, "edit", DateTimeOffset.Parse("2026-08-07T09:00:00+08:00"), CancellationToken.None);
        await InstallFailingVersionTriggerAsync();

        var restore = () => _archives.RestoreVersionAsync(record.Id, 1, "restore", DateTimeOffset.Parse("2026-08-07T10:00:00+08:00"), CancellationToken.None);

        await restore.Should().ThrowAsync<SqliteException>();
        (await _records.GetAsync(record.Id, CancellationToken.None))!.PayloadJson.Should().Be("{\"quantity\":20}");
        (await _archives.GetVersionsAsync(record.Id, CancellationToken.None)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Pre_cancelled_archive_edit_does_not_change_the_record_or_versions()
    {
        var record = await SaveCompletedRecordAsync("{\"quantity\":10}");
        await _archives.SealAsync(record.Id, "DZ", "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var save = () => _archives.SaveArchivedEditAsync(record.Id, "{\"quantity\":20}", new Dictionary<string, string> { ["company"] = "new" }, "edit", DateTimeOffset.Parse("2026-08-07T09:00:00+08:00"), cancellation.Token);

        await save.Should().ThrowAsync<OperationCanceledException>();
        (await _records.GetAsync(record.Id, CancellationToken.None))!.PayloadJson.Should().Be("{\"quantity\":10}");
        (await _archives.GetVersionsAsync(record.Id, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task Password_store_rejects_a_persisted_hash_with_less_than_one_hundred_thousand_iterations()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2("weak password", salt, 1, HashAlgorithmName.SHA256, 32);
        var weakValue = JsonSerializer.Serialize(new { Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(hash), Iterations = 1 });
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO settings (setting_key, value, updated_at) VALUES ('archive_unlock_password', @value, @updated);";
            command.Parameters.AddWithValue("@value", weakValue);
            command.Parameters.AddWithValue("@updated", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        (await new SqliteUnlockPasswordStore(_connectionString).VerifyAsync("weak password", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Password_store_rejects_a_persisted_hash_with_an_invalid_salt_length()
    {
        var salt = new byte[] { 7 };
        var hash = Rfc2898DeriveBytes.Pbkdf2("candidate", salt, 100_000, HashAlgorithmName.SHA256, 32);
        var invalidValue = JsonSerializer.Serialize(new { Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(hash), Iterations = 100_000 });
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO settings (setting_key, value, updated_at) VALUES ('archive_unlock_password', @value, @updated);";
            command.Parameters.AddWithValue("@value", invalidValue);
            command.Parameters.AddWithValue("@updated", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        (await new SqliteUnlockPasswordStore(_connectionString).VerifyAsync("candidate", CancellationToken.None)).Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"Hash\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\",\"Iterations\":100000}")]
    [InlineData("{\"Salt\":null,\"Hash\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\",\"Iterations\":100000}")]
    [InlineData("{\"Salt\":\"AAAAAAAAAAAAAAAAAAAAAA==\",\"Hash\":null,\"Iterations\":100000}")]
    [InlineData("{\"Salt\":\"not-base64\",\"Hash\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\",\"Iterations\":100000}")]
    [InlineData("{\"Salt\":\"AAAAAAAAAAAAAAAAAAAAAA==\",\"Hash\":\"not-base64\",\"Iterations\":100000}")]
    public async Task Password_store_returns_false_for_missing_null_or_malformed_password_material(string value)
    {
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO settings (setting_key, value, updated_at) VALUES ('archive_unlock_password', @value, @updated);";
            command.Parameters.AddWithValue("@value", value);
            command.Parameters.AddWithValue("@updated", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        (await new SqliteUnlockPasswordStore(_connectionString).VerifyAsync("candidate", CancellationToken.None)).Should().BeFalse();
    }

    private async Task InstallFailingVersionTriggerAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER fail_record_version_insert BEFORE INSERT ON record_versions BEGIN SELECT RAISE(ABORT, 'injected version failure'); END;";
        await command.ExecuteNonQueryAsync();
    }

    private async Task<FormRecord> SaveCompletedRecordAsync(string payload)
    {
        var record = FormRecord.CreateDraft("production-daily", 1, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), new Dictionary<string, string> { ["company"] = "旧公司" }, payload, "旧公司", DateTimeOffset.Parse("2026-08-07T07:00:00+08:00"));
        record.MarkCompleted();
        await _records.SaveAsync(record, CancellationToken.None);
        return record;
    }
}
