using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SAloha.Api.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssistedCmdbAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssistedCmdbAnalyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PerimeterId = table.Column<int>(type: "integer", nullable: false),
                    Tasks = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ItemIds = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    ProposalCount = table.Column<int>(type: "integer", nullable: false),
                    RejectedCount = table.Column<int>(type: "integer", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistedCmdbAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistedCmdbAnalyses_Perimeters_PerimeterId",
                        column: x => x.PerimeterId,
                        principalTable: "Perimeters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistedCmdbAnalyses_PerimeterId",
                table: "AssistedCmdbAnalyses",
                column: "PerimeterId",
                unique: true,
                filter: "\"Status\" IN ('En file', 'En cours')");

            migrationBuilder.CreateIndex(
                name: "IX_AssistedCmdbAnalyses_PerimeterId_Id",
                table: "AssistedCmdbAnalyses",
                columns: new[] { "PerimeterId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistedCmdbAnalyses");
        }
    }
}
