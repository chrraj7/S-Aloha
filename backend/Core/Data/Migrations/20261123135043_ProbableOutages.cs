using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SAloha.Api.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProbableOutages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProbableOutages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Feature = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PerimeterId = table.Column<int>(type: "integer", nullable: false),
                    Basis = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: true),
                    Reasons = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProbableOutages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProbableOutages_Perimeters_PerimeterId",
                        column: x => x.PerimeterId,
                        principalTable: "Perimeters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProbableOutageMembers",
                columns: table => new
                {
                    OutageId = table.Column<int>(type: "integer", nullable: false),
                    IncidentId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProbableOutageMembers", x => new { x.OutageId, x.IncidentId });
                    table.ForeignKey(
                        name: "FK_ProbableOutageMembers_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProbableOutageMembers_ProbableOutages_OutageId",
                        column: x => x.OutageId,
                        principalTable: "ProbableOutages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProbableOutageMembers_IncidentId",
                table: "ProbableOutageMembers",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProbableOutages_ClosedAt",
                table: "ProbableOutages",
                column: "ClosedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProbableOutages_PerimeterId",
                table: "ProbableOutages",
                column: "PerimeterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProbableOutageMembers");

            migrationBuilder.DropTable(
                name: "ProbableOutages");
        }
    }
}
