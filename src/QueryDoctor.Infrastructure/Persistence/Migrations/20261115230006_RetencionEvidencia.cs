using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetencionEvidencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "purged_at",
                schema: "core",
                table: "query_text",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "purged_at",
                schema: "core",
                table: "plan",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "purged_at",
                schema: "core",
                table: "query_text");

            migrationBuilder.DropColumn(
                name: "purged_at",
                schema: "core",
                table: "plan");
        }
    }
}
