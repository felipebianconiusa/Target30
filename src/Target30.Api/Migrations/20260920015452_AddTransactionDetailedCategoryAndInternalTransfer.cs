using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Target30.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionDetailedCategoryAndInternalTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailedCategory",
                table: "PlaidTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInternalTransfer",
                table: "PlaidTransactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Zera o cursor do Plaid pra o próximo sync rebaixar tudo (grátis) e preencher a categoria
            // detalhada / o marcador de transferência interna das transações que já existem.
            migrationBuilder.Sql("UPDATE PlaidItems SET NextCursor = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailedCategory",
                table: "PlaidTransactions");

            migrationBuilder.DropColumn(
                name: "IsInternalTransfer",
                table: "PlaidTransactions");
        }
    }
}
