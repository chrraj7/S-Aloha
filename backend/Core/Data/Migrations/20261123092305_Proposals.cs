using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SAloha.Api.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Proposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProposalCounts",
                columns: table => new
                {
                    Month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Feature = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Field = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Accepted = table.Column<int>(type: "integer", nullable: false),
                    Corrected = table.Column<int>(type: "integer", nullable: false),
                    Rejected = table.Column<int>(type: "integer", nullable: false),
                    Expired = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposalCounts", x => new { x.Month, x.Feature, x.Field });
                });

            migrationBuilder.CreateTable(
                name: "Proposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Feature = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: false),
                    PerimeterId = table.Column<int>(type: "integer", nullable: false),
                    Field = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Before = table.Column<string>(type: "text", nullable: true),
                    After = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Reasons = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CitedJson = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FinalValue = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proposals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_TargetType_TargetId_Status",
                table: "Proposals",
                columns: new[] { "TargetType", "TargetId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProposalCounts");

            migrationBuilder.DropTable(
                name: "Proposals");
        }
    }
}
