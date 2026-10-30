using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurringOperationsAndLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyLimit",
                table: "Categories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    NextDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Anchor = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringOperations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOperations_Accounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOperations_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOperations_AccountId",
                table: "RecurringOperations",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOperations_CategoryId",
                table: "RecurringOperations",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOperations_NextDate",
                table: "RecurringOperations",
                column: "NextDate");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOperations_ToAccountId",
                table: "RecurringOperations",
                column: "ToAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringOperations");

            migrationBuilder.DropColumn(
                name: "MonthlyLimit",
                table: "Categories");
        }
    }
}
