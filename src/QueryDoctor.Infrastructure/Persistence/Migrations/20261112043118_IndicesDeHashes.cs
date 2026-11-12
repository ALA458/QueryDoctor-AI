using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndicesDeHashes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_query_connection_id_query_hash",
                schema: "core",
                table: "query",
                columns: new[] { "connection_id", "query_hash" });

            migrationBuilder.CreateIndex(
                name: "IX_plan_connection_id_query_plan_hash",
                schema: "core",
                table: "plan",
                columns: new[] { "connection_id", "query_plan_hash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_query_connection_id_query_hash",
                schema: "core",
                table: "query");

            migrationBuilder.DropIndex(
                name: "IX_plan_connection_id_query_plan_hash",
                schema: "core",
                table: "plan");
        }
    }
}
