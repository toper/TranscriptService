using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TranscriptService.Api.Jobs.Migrations
{
    /// <inheritdoc />
    public partial class StatusCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Status_CreatedAt",
                table: "Jobs",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_Status_CreatedAt",
                table: "Jobs");
        }
    }
}
