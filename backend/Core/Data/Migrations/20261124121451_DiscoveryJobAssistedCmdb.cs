using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SAloha.Api.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class DiscoveryJobAssistedCmdb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AssistedCmdb",
                table: "DiscoveryJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssistedCmdb",
                table: "DiscoveryJobs");
        }
    }
}
