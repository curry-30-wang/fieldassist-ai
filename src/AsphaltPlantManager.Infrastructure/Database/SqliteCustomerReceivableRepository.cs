using AsphaltPlantManager.Core.Receivables;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace AsphaltPlantManager.Infrastructure.Database;

public sealed class SqliteCustomerReceivableRepository : ICustomerReceivableRepository
{
    private readonly string _connectionString;

    public SqliteCustomerReceivableRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<CustomerReceivable>> ListAsync(string? customerName, CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ReceivableRow>(new CommandDefinition(
            "SELECT id AS Id, customer_name AS CustomerName, receivable_amount AS ReceivableAmount, paid_amount AS PaidAmount, due_date AS DueDate, note AS Note, updated_at AS UpdatedAt FROM customer_receivables WHERE @CustomerName IS NULL OR customer_name LIKE @CustomerNamePattern ESCAPE '\\' ORDER BY customer_name COLLATE NOCASE;",
            new { CustomerName = string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim(), CustomerNamePattern = string.IsNullOrWhiteSpace(customerName) ? null : $"%{EscapeLike(customerName.Trim())}%" },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(ToModel).ToArray();
    }

    public async Task UpsertAsync(CustomerReceivable receivable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receivable);
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var parameters = new { Id = receivable.Id.ToString("D"), receivable.CustomerName, ReceivableAmount = (double)receivable.ReceivableAmount, PaidAmount = (double)receivable.PaidAmount, DueDate = receivable.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), receivable.Note, UpdatedAt = Format(receivable.UpdatedAt) };
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE customer_receivables SET customer_name = @CustomerName, receivable_amount = @ReceivableAmount, paid_amount = @PaidAmount, due_date = @DueDate, note = @Note, updated_at = @UpdatedAt WHERE id = @Id;",
            parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (updated == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO customer_receivables (id, customer_name, receivable_amount, paid_amount, due_date, note, updated_at) VALUES (@Id, @CustomerName, @ReceivableAmount, @PaidAmount, @DueDate, @Note, @UpdatedAt) ON CONFLICT(customer_name) DO UPDATE SET receivable_amount = excluded.receivable_amount, paid_amount = excluded.paid_amount, due_date = excluded.due_date, note = excluded.note, updated_at = excluded.updated_at;",
                parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("欠款记录编号不能为空。", nameof(id));
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await connection.ExecuteAsync(new CommandDefinition("DELETE FROM customer_receivables WHERE id = @Id;", new { Id = id.ToString("D") }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return deleted == 1;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try { await connection.OpenAsync(cancellationToken).ConfigureAwait(false); await DatabaseInitializer.ApplyConnectionPragmasAsync(connection, cancellationToken).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static CustomerReceivable ToModel(ReceivableRow row) => new(Guid.Parse(row.Id), row.CustomerName, (decimal)row.ReceivableAmount, (decimal)row.PaidAmount, string.IsNullOrWhiteSpace(row.DueDate) ? null : DateOnly.ParseExact(row.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), row.Note, DateTimeOffset.ParseExact(row.UpdatedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None));
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private sealed class ReceivableRow
    {
        public string Id { get; init; } = null!;
        public string CustomerName { get; init; } = null!;
        public double ReceivableAmount { get; init; }
        public double PaidAmount { get; init; }
        public string? DueDate { get; init; }
        public string Note { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = null!;
    }
}
