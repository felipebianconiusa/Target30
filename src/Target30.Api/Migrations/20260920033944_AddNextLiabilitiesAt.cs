using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Target30.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNextLiabilitiesAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextLiabilitiesAt",
                table: "PlaidItems",
                type: "TEXT",
                nullable: true);

            // Os bancos já existentes buscaram liabilities há pouco: só volta a chamar daqui a 72h.
            migrationBuilder.Sql("UPDATE PlaidItems SET NextLiabilitiesAt = datetime('now', '+72 hours');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextLiabilitiesAt",
                table: "PlaidItems");
        }
    }
}
