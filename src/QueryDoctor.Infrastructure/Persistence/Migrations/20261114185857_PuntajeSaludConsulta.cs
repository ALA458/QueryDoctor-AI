using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PuntajeSaludConsulta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "health_score",
                schema: "analysis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingestion_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    priority = table.Column<double>(type: "double precision", nullable: false),
                    band = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    impact_component = table.Column<double>(type: "double precision", nullable: false),
                    severity_component = table.Column<double>(type: "double precision", nullable: false),
                    alpha_used = table.Column<double>(type: "double precision", nullable: false),
                    cpu_share = table.Column<double>(type: "double precision", nullable: false),
                    duration_share = table.Column<double>(type: "double precision", nullable: false),
                    logical_reads_share = table.Column<double>(type: "double precision", nullable: false),
                    weighted_consumption_pct = table.Column<double>(type: "double precision", nullable: false),
                    total_executions = table.Column<long>(type: "bigint", nullable: false),
                    total_cpu_microseconds = table.Column<double>(type: "double precision", nullable: false),
                    total_duration_microseconds = table.Column<double>(type: "double precision", nullable: false),
                    total_logical_reads = table.Column<double>(type: "double precision", nullable: false),
                    findings_count = table.Column<int>(type: "integer", nullable: false),
                    contributing_findings_json = table.Column<string>(type: "text", nullable: false),
                    parameters_json = table.Column<string>(type: "text", nullable: false),
                    parameters_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_health_score", x => x.id);
                    table.ForeignKey(
                        name: "FK_health_score_connection_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "core",
                        principalTable: "connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_health_score_ingestion_run_ingestion_run_id",
                        column: x => x.ingestion_run_id,
                        principalSchema: "core",
                        principalTable: "ingestion_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_health_score_query_query_id",
                        column: x => x.query_id,
                        principalSchema: "core",
                        principalTable: "query",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_health_score_connection_id_calculated_at",
                schema: "analysis",
                table: "health_score",
                columns: new[] { "connection_id", "calculated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_health_score_connection_id_score",
                schema: "analysis",
                table: "health_score",
                columns: new[] { "connection_id", "score" });

            migrationBuilder.CreateIndex(
                name: "IX_health_score_ingestion_run_id",
                schema: "analysis",
                table: "health_score",
                column: "ingestion_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_health_score_query_id_calculated_at",
                schema: "analysis",
                table: "health_score",
                columns: new[] { "query_id", "calculated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "health_score",
                schema: "analysis");
        }
    }
}
