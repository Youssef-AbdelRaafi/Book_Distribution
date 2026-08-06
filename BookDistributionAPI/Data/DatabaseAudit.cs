using Microsoft.Data.Sqlite;

namespace BookDistributionAPI.Data;

/// <summary>
/// Performs read-only checks on a packaged SQLite database before it is accepted for deployment.
/// It intentionally reports possible duplicate invoices as warnings; deciding whether a matching
/// invoice is a genuine duplicate must always remain a human decision.
/// </summary>
public static class DatabaseAudit
{
    public static async Task<DatabaseAuditReport> RunAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly
        };

        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new InvalidOperationException("The SQLite connection string does not contain a data source.");

        var databasePath = Path.GetFullPath(builder.DataSource);
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("The database file was not found.", databasePath);

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var integrityCheck = await GetScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken);
        var foreignKeyViolationCount = await CountRowsAsync(connection, "PRAGMA foreign_key_check;", cancellationToken);

        var invoiceCount = await GetScalarIntAsync(connection, "SELECT COUNT(*) FROM Invoices;", cancellationToken);
        var invoiceItemCount = await GetScalarIntAsync(connection, "SELECT COUNT(*) FROM InvoiceItems;", cancellationToken);
        var emptyFinancialInvoiceCount = await GetScalarIntAsync(connection, """
            SELECT COUNT(*)
            FROM Invoices i
            WHERE i.Type IN ('order', 'refund')
              AND i.TotalAmount = 0
              AND NOT EXISTS (SELECT 1 FROM InvoiceItems ii WHERE ii.InvoiceId = i.Id);
            """, cancellationToken);
        var headerItemMismatchCount = await GetScalarIntAsync(connection, """
            SELECT COUNT(*)
            FROM Invoices i
            WHERE ABS(i.TotalAmount - COALESCE(
                (SELECT SUM(ii.Total) FROM InvoiceItems ii WHERE ii.InvoiceId = i.Id),
                0)) > 0.001;
            """, cancellationToken);

        var duplicateGroups = await GetScalarIntAsync(connection, DuplicateInvoiceGroupsSql, cancellationToken);
        var duplicateInvoices = await GetScalarIntAsync(connection, DuplicateInvoiceCountSql, cancellationToken);
        var duplicateExamples = await GetStringsAsync(connection, DuplicateInvoiceExamplesSql, cancellationToken);

        var errors = new List<DatabaseAuditIssue>();
        var warnings = new List<DatabaseAuditIssue>();

        if (!string.Equals(integrityCheck, "ok", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new DatabaseAuditIssue(
                "integrity_check_failed",
                $"SQLite integrity_check returned '{integrityCheck}'.",
                1));
        }

        if (foreignKeyViolationCount > 0)
        {
            errors.Add(new DatabaseAuditIssue(
                "foreign_key_violations",
                "Foreign-key violations were found.",
                foreignKeyViolationCount));
        }

        if (headerItemMismatchCount > 0)
        {
            errors.Add(new DatabaseAuditIssue(
                "invoice_total_mismatch",
                "Invoice totals do not match the sum of their line items.",
                headerItemMismatchCount));
        }

        if (emptyFinancialInvoiceCount > 0)
        {
            warnings.Add(new DatabaseAuditIssue(
                "empty_financial_invoices",
                "Zero-value order/refund invoices without line items need source-document review.",
                emptyFinancialInvoiceCount));
        }

        if (duplicateGroups > 0)
        {
            var examples = duplicateExamples.Count == 0
                ? string.Empty
                : $" Examples: {string.Join("; ", duplicateExamples)}.";
            warnings.Add(new DatabaseAuditIssue(
                "possible_duplicate_invoices",
                $"Invoices with identical library, term, date, type, total, and line items were found.{examples}",
                duplicateInvoices,
                duplicateGroups));
        }

        return new DatabaseAuditReport(
            databasePath,
            integrityCheck,
            invoiceCount,
            invoiceItemCount,
            emptyFinancialInvoiceCount,
            duplicateGroups,
            duplicateInvoices,
            errors,
            warnings);
    }

    private static async Task<string> GetScalarStringAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string?)await command.ExecuteScalarAsync(cancellationToken) ?? string.Empty;
    }

    private static async Task<int> GetScalarIntAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<int> CountRowsAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var count = 0;
        while (await reader.ReadAsync(cancellationToken))
            count++;

        return count;
    }

    private static async Task<IReadOnlyList<string>> GetStringsAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
            values.Add(reader.GetString(0));

        return values;
    }

    private const string DuplicateInvoiceGroupsSql = """
        WITH ItemSignatures AS (
            SELECT InvoiceId, group_concat(LineSignature, '|') AS LineSignature
            FROM (
                SELECT InvoiceId,
                       printf('%d:%.3f:%.3f:%.3f', BookId, Quantity, UnitPrice, Total) AS LineSignature
                FROM InvoiceItems
                ORDER BY InvoiceId, BookId, Quantity, UnitPrice, Total
            )
            GROUP BY InvoiceId
        ), DuplicateGroups AS (
            SELECT i.LibraryId, i.SemesterId, i.Date, i.Type, i.IsActive, i.TotalAmount, s.LineSignature,
                   COUNT(*) AS DuplicateCount
            FROM Invoices i
            JOIN ItemSignatures s ON s.InvoiceId = i.Id
            WHERE i.Type IN ('order', 'refund') AND i.TotalAmount > 0
            GROUP BY i.LibraryId, i.SemesterId, i.Date, i.Type, i.IsActive, i.TotalAmount, s.LineSignature
            HAVING COUNT(*) > 1
        )
        SELECT COUNT(*) FROM DuplicateGroups;
        """;

    private const string DuplicateInvoiceCountSql = """
        WITH ItemSignatures AS (
            SELECT InvoiceId, group_concat(LineSignature, '|') AS LineSignature
            FROM (
                SELECT InvoiceId,
                       printf('%d:%.3f:%.3f:%.3f', BookId, Quantity, UnitPrice, Total) AS LineSignature
                FROM InvoiceItems
                ORDER BY InvoiceId, BookId, Quantity, UnitPrice, Total
            )
            GROUP BY InvoiceId
        ), DuplicateGroups AS (
            SELECT COUNT(*) AS DuplicateCount
            FROM Invoices i
            JOIN ItemSignatures s ON s.InvoiceId = i.Id
            WHERE i.Type IN ('order', 'refund') AND i.TotalAmount > 0
            GROUP BY i.LibraryId, i.SemesterId, i.Date, i.Type, i.IsActive, i.TotalAmount, s.LineSignature
            HAVING COUNT(*) > 1
        )
        SELECT COALESCE(SUM(DuplicateCount), 0) FROM DuplicateGroups;
        """;

    private const string DuplicateInvoiceExamplesSql = """
        WITH ItemSignatures AS (
            SELECT InvoiceId, group_concat(LineSignature, '|') AS LineSignature
            FROM (
                SELECT InvoiceId,
                       printf('%d:%.3f:%.3f:%.3f', BookId, Quantity, UnitPrice, Total) AS LineSignature
                FROM InvoiceItems
                ORDER BY InvoiceId, BookId, Quantity, UnitPrice, Total
            )
            GROUP BY InvoiceId
        ), DuplicateGroups AS (
            SELECT l.Name AS LibraryName, i.Date, i.TotalAmount, COUNT(*) AS DuplicateCount
            FROM Invoices i
            JOIN Libraries l ON l.Id = i.LibraryId
            JOIN ItemSignatures s ON s.InvoiceId = i.Id
            WHERE i.Type IN ('order', 'refund') AND i.TotalAmount > 0
            GROUP BY i.LibraryId, i.SemesterId, i.Date, i.Type, i.IsActive, i.TotalAmount, s.LineSignature
            HAVING COUNT(*) > 1
            ORDER BY DuplicateCount DESC, l.Name
            LIMIT 3
        )
        SELECT printf('%s (%s, %.3f) x%d', LibraryName, Date, TotalAmount, DuplicateCount)
        FROM DuplicateGroups;
        """;
}

public sealed record DatabaseAuditIssue(
    string Code,
    string Message,
    int Count,
    int? GroupCount = null);

public sealed record DatabaseAuditReport(
    string DatabasePath,
    string IntegrityCheck,
    int InvoiceCount,
    int InvoiceItemCount,
    int EmptyFinancialInvoiceCount,
    int PossibleDuplicateGroupCount,
    int PossibleDuplicateInvoiceCount,
    IReadOnlyList<DatabaseAuditIssue> Errors,
    IReadOnlyList<DatabaseAuditIssue> Warnings)
{
    public bool IsApproved => Errors.Count == 0 && Warnings.Count == 0;
}
