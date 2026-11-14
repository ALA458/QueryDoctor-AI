using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EsquemaAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "analysis");

            migrationBuilder.CreateTable(
                name: "rule_catalog",
                schema: "analysis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    catalog_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rule_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    documentation_source = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    rule_class = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    evidence_class = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    base_severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    default_thresholds_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_catalog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "threshold_set",
                schema: "analysis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    configuration_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    thresholds_json = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_threshold_set", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analysis_run",
                schema: "analysis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    threshold_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    engine_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    thresholds_used_json = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    findings_count = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_cause = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_run", x => x.id);
                    table.ForeignKey(
                        name: "FK_analysis_run_connection_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "core",
                        principalTable: "connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_analysis_run_plan_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "core",
                        principalTable: "plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_analysis_run_query_query_id",
                        column: x => x.query_id,
                        principalSchema: "core",
                        principalTable: "query",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_analysis_run_threshold_set_threshold_set_id",
                        column: x => x.threshold_set_id,
                        principalSchema: "analysis",
                        principalTable: "threshold_set",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finding",
                schema: "analysis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finding_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rule_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rule_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    catalog_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    schema_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    evidence_class = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    nodes_json = table.Column<string>(type: "text", nullable: false),
                    encrypted_evidence_json = table.Column<string>(type: "text", nullable: false),
                    context_json = table.Column<string>(type: "text", nullable: false),
                    thresholds_used_json = table.Column<string>(type: "text", nullable: false),
                    engine_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    analyzed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finding", x => x.id);
                    table.ForeignKey(
                        name: "FK_finding_analysis_run_analysis_run_id",
                        column: x => x.analysis_run_id,
                        principalSchema: "analysis",
                        principalTable: "analysis_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finding_connection_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "core",
                        principalTable: "connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finding_plan_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "core",
                        principalTable: "plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finding_query_query_id",
                        column: x => x.query_id,
                        principalSchema: "core",
                        principalTable: "query",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "analysis",
                table: "rule_catalog",
                columns: new[] { "id", "base_severity", "catalog_version", "default_thresholds_json", "description", "documentation_source", "evidence_class", "name", "priority", "rule_class", "rule_id", "rule_version" },
                values: new object[,]
                {
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0001"), "Alta", "1.0.0", "{}", "Advertencia PlanAffectingConvert del plan, con el tipo de problema (ConvertIssue) y la expresión afectada.", "Microsoft Learn: Showplan XML Warnings (PlanAffectingConvert) [4, 11]", "Estimada", "Conversión implícita que afecta el plan", "Must", "ClassA", "REG-01", "1.0.0" },
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0002"), "Media", "1.0.0", "{}", "Grupo MissingIndexes del plan, con el impacto estimado y las columnas por tipo de uso, acompañado de la advertencia de costo de escritura.", "Microsoft Learn: Tune nonclustered indexes with missing index suggestions [30]", "Estimada", "Índice faltante", "Must", "ClassA", "REG-02", "1.0.0" },
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0003"), "Media", "1.0.0", "{\"min_estimated_rows\":10000,\"min_query_consumption_pct\":1,\"rows_read_to_returned_ratio\":10}", "Operadores de recorrido completo de tabla o de índice que cumplen una condición de contexto: predicado con alta razón entre filas leídas y devueltas estimadas, o volumen estimado alto en una consulta con consumo relevante en la ventana.", "Microsoft Learn: Display the estimated execution plan & Table/Index Scan operators [4, 6]", "Estimada", "Lectura completa costosa (contextual)", "Must", "ClassA", "REG-03", "1.0.0" },
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0004"), "Media", "1.0.0", "{\"min_estimated_executions\":1000,\"min_query_consumption_pct\":1}", "Operador de búsqueda por clave (Key Lookup o RID Lookup) con un número estimado de ejecuciones por encima de un umbral configurable, en una consulta con consumo relevante en la ventana.", "Microsoft Learn: Showplan Logical and Physical Operators Reference (Key Lookup / RID Lookup) [4, 6]", "Estimada", "Búsqueda por clave excesiva (contextual)", "Must", "ClassA", "REG-04", "1.0.0" },
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0005"), "Media", "1.0.0", "{}", "Advertencia ColumnsWithNoStatistics en el plan compilado, con las columnas afectadas.", "Microsoft Learn: Showplan XML Warnings (ColumnsWithNoStatistics) [4, 11]", "Estimada", "Ausencia de estadísticas", "Should", "ClassA", "REG-05", "1.0.0" },
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0006"), "Alta", "1.0.0", "{}", "Advertencia de unión sin predicado explícito de cruce (NoJoinPredicate) en un operador del plan.", "Microsoft Learn: Showplan XML Warnings (NoJoinPredicate) [4, 11]", "Estimada", "Unión sin predicado", "Should", "ClassA", "REG-06", "1.0.0" },
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0007"), "Media", "1.0.0", "{\"cardinality_ratio\":10}", "Comparación entre las filas estimadas de la sentencia y las filas devueltas promedio de las estadísticas de ejecución en Query Store.", "Microsoft Learn: How Query Store collects data & sys.query_store_runtime_stats [3, 5]", "EjecucionAgregada", "Discrepancia de cardinalidad a nivel de sentencia", "Should", "ClassB", "REG-07", "1.0.0" },
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0008"), "Alta", "1.0.0", "{\"min_executions_per_plan\":100,\"regression_increase_pct\":50}", "Comparación de las estadísticas agregadas de duración y CPU entre múltiples planes de una misma consulta a lo largo del tiempo.", "Microsoft Learn: sys.query_store_plan & sys.query_store_runtime_stats [4, 5]", "EjecucionAgregada", "Regresión de rendimiento entre planes", "Should", "ClassD", "REG-08", "1.0.0" },
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0009"), "Media", "1.0.0", "{}", "Advertencias de desbordamiento presentes en un plan real; de no existir plan real, un uso elevado del espacio de almacenamiento temporal en las estadísticas agregadas se reporta únicamente como indicio.", "Microsoft Learn: sys.dm_exec_query_plan_stats & TempDB spill events in Showplan [7, 10]", "PlanReal", "Desbordamiento a almacenamiento temporal y advertencias de memoria", "Could", "ClassC", "REG-09", "1.0.0" },
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0010"), "Baja", "1.0.0", "{\"duration_max_min_ratio\":5}", "Variabilidad significativa de duración entre múltiples planes compilados de la misma consulta reportada como indicio no confirmado.", "Microsoft Learn: How Query Store collects data [3, 5]", "EjecucionAgregada", "Sensibilidad a parámetros (indicio)", "Could", "ClassD", "REG-10", "1.0.0" },
                    { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0011"), "Informativa", "1.0.0", "{}", "Estructura XML nula, inválida, truncada o no reconocida por el analizador Showplan.", "Microsoft Learn: sys.dm_exec_query_plan_stats (XML nesting limit, F8) [7]", "NoAplica", "Plan no analizable", "Must", "None", "REG-11", "1.0.0" }
                });

            migrationBuilder.InsertData(
                schema: "analysis",
                table: "threshold_set",
                columns: new[] { "id", "catalog_version", "configuration_hash", "created_at", "is_default", "thresholds_json" },
                values: new object[] { new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a1001"), "1.0.0", "8182349B6FF3DAD35F1B6FCCBD38C6AA8B0AFDDF9E7B91C28EBB061D8502E61E", new DateTimeOffset(new DateTime(2026, 10, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "{\"min_query_consumption_pct\":1,\"reg03_min_estimated_rows\":10000,\"reg03_rows_read_to_returned_ratio\":10,\"reg04_min_estimated_executions\":1000,\"reg07_cardinality_ratio\":10,\"reg08_min_executions_per_plan\":100,\"reg08_regression_increase_pct\":50,\"reg10_duration_max_min_ratio\":5}" });

            migrationBuilder.CreateIndex(
                name: "IX_analysis_run_connection_id",
                schema: "analysis",
                table: "analysis_run",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_analysis_run_plan_id_started_at",
                schema: "analysis",
                table: "analysis_run",
                columns: new[] { "plan_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_analysis_run_query_id",
                schema: "analysis",
                table: "analysis_run",
                column: "query_id");

            migrationBuilder.CreateIndex(
                name: "IX_analysis_run_threshold_set_id",
                schema: "analysis",
                table: "analysis_run",
                column: "threshold_set_id");

            migrationBuilder.CreateIndex(
                name: "IX_finding_analysis_run_id_finding_code",
                schema: "analysis",
                table: "finding",
                columns: new[] { "analysis_run_id", "finding_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finding_connection_id_severity",
                schema: "analysis",
                table: "finding",
                columns: new[] { "connection_id", "severity" });

            migrationBuilder.CreateIndex(
                name: "IX_finding_plan_id",
                schema: "analysis",
                table: "finding",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_finding_query_id",
                schema: "analysis",
                table: "finding",
                column: "query_id");

            migrationBuilder.CreateIndex(
                name: "IX_rule_catalog_rule_id_catalog_version_rule_version",
                schema: "analysis",
                table: "rule_catalog",
                columns: new[] { "rule_id", "catalog_version", "rule_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_threshold_set_catalog_version_configuration_hash",
                schema: "analysis",
                table: "threshold_set",
                columns: new[] { "catalog_version", "configuration_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finding",
                schema: "analysis");

            migrationBuilder.DropTable(
                name: "rule_catalog",
                schema: "analysis");

            migrationBuilder.DropTable(
                name: "analysis_run",
                schema: "analysis");

            migrationBuilder.DropTable(
                name: "threshold_set",
                schema: "analysis");
        }
    }
}
