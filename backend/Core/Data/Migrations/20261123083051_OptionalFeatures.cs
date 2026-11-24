using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SAloha.Api.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptionalFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "AuditEntries",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                // OPT-08 : les lignes antérieures viennent toutes d'une saisie.
                defaultValue: "Saisie");

            migrationBuilder.CreateTable(
                name: "OptionalFeatures",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptionalFeatures", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OptionalFeatures");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "AuditEntries");
        }
    }
}
