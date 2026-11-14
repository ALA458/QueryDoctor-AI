using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstadoDeSondeoYUnicidadDeConexion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_connection_server_database_name",
                schema: "core",
                table: "connection");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_probed_at",
                schema: "core",
                table: "connection",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "probe_failure_cause",
                schema: "core",
                table: "connection",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "database_name_key",
                schema: "core",
                table: "connection",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                computedColumnSql: "lower(database_name)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_connection_server_database_name_key",
                schema: "core",
                table: "connection",
                columns: new[] { "server", "database_name_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_connection_server_database_name_key",
                schema: "core",
                table: "connection");

            migrationBuilder.DropColumn(
                name: "database_name_key",
                schema: "core",
                table: "connection");

            migrationBuilder.DropColumn(
                name: "last_probed_at",
                schema: "core",
                table: "connection");

            migrationBuilder.DropColumn(
                name: "probe_failure_cause",
                schema: "core",
                table: "connection");

            migrationBuilder.CreateIndex(
                name: "IX_connection_server_database_name",
                schema: "core",
                table: "connection",
                columns: new[] { "server", "database_name" },
                unique: true);
        }
    }
}
