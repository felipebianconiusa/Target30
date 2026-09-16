using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Target30.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCardsAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaidAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    OfficialName = table.Column<string>(type: "TEXT", nullable: true),
                    InstitutionName = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Subtype = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentBalance = table.Column<decimal>(type: "TEXT", nullable: true),
                    AvailableBalance = table.Column<decimal>(type: "TEXT", nullable: true),
                    CreditLimit = table.Column<decimal>(type: "TEXT", nullable: true),
                    IsoCurrencyCode = table.Column<string>(type: "TEXT", nullable: true),
                    LastStatementBalance = table.Column<decimal>(type: "TEXT", nullable: true),
                    LastStatementIssueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    NextPaymentDueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    MinimumPaymentAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    IsOverdue = table.Column<bool>(type: "INTEGER", nullable: true),
                    StatementClosingDay = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetUtilizationPercent = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaidAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    GlobalTargetUtilizationPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    NotifyDaysBeforeClosing = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaidAccounts_AccountId",
                table: "PlaidAccounts",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaidAccounts_UserId",
                table: "PlaidAccounts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaidAccounts");

            migrationBuilder.DropTable(
                name: "UserSettings");
        }
    }
}
