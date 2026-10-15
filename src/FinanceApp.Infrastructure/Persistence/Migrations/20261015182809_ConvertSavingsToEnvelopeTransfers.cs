using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvertSavingsToEnvelopeTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0010: "saving" stops being a transaction type. Every saving category that has transactions
            // becomes an envelope with the same name, and those transactions become transfers into it.
            // GUIDs are generated in SQL in EF's upper-case text format (version 4, variant 8).
            migrationBuilder.Sql("""
                INSERT INTO Accounts (Id, Name, Currency, Kind, OpeningBalance, IsHidden, SortOrder)
                SELECT
                    substr(s.h, 1, 8) || '-' || substr(s.h, 9, 4) || '-4' || substr(s.h, 14, 3) || '-8'
                        || substr(s.h, 18, 3) || '-' || substr(s.h, 21, 12),
                    s.Name, s.Currency, 'Envelope', '0.0', 0,
                    (SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM Accounts)
                FROM (
                    SELECT c.Name AS Name, MIN(t.Currency) AS Currency, hex(randomblob(16)) AS h
                    FROM Transactions t JOIN Categories c ON c.Id = t.CategoryId
                    WHERE t.Type = 'Saving'
                    GROUP BY c.Name
                ) s
                WHERE NOT EXISTS (SELECT 1 FROM Accounts a WHERE a.Name = s.Name);
                """);

            migrationBuilder.Sql("""
                UPDATE Transactions
                SET Type = 'Transfer',
                    ToAccountId = (
                        SELECT a.Id FROM Accounts a JOIN Categories c ON a.Name = c.Name
                        WHERE c.Id = Transactions.CategoryId),
                    ToAmount = Amount,
                    CategoryId = NULL
                WHERE Type = 'Saving';
                """);

            migrationBuilder.Sql("""
                DELETE FROM Categories
                WHERE Type = 'Saving'
                  AND Id NOT IN (SELECT CategoryId FROM Transactions WHERE CategoryId IS NOT NULL);
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // One-way: transfers created from savings stay transfers (see the schema migration's Down).

        }
    }
}
