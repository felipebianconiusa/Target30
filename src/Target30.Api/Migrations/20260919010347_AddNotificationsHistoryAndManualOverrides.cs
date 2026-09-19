using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Target30.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsHistoryAndManualOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "UserSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotificationsEnabled",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastAlertSentDate",
                table: "PlaidAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ManualCreditLimit",
                table: "PlaidAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ManualNextPaymentDueDate",
                table: "PlaidAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CardBalanceSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Balance = table.Column<decimal>(type: "TEXT", nullable: false),
                    Limit = table.Column<decimal>(type: "TEXT", nullable: true),
                    UtilizationPercent = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardBalanceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardBalanceSnapshots_AccountId_Date",
                table: "CardBalanceSnapshots",
                columns: new[] { "AccountId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardBalanceSnapshots");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "NotificationsEnabled",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "LastAlertSentDate",
                table: "PlaidAccounts");

            migrationBuilder.DropColumn(
                name: "ManualCreditLimit",
                table: "PlaidAccounts");

            migrationBuilder.DropColumn(
                name: "ManualNextPaymentDueDate",
                table: "PlaidAccounts");
        }
    }
}
