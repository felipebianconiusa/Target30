using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Target30.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLastSyncedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAt",
                table: "PlaidItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "PlaidItems");
        }
    }
}
