using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CalibracionUmbralesS301 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "analysis",
                table: "rule_catalog",
                keyColumn: "id",
                keyValue: new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0003"),
                column: "default_thresholds_json",
                value: "{\"min_estimated_rows\":10000,\"min_predicate_volume_rows\":1000,\"min_query_consumption_pct\":1,\"rows_read_to_returned_ratio\":10}");

            migrationBuilder.UpdateData(
                schema: "analysis",
                table: "threshold_set",
                keyColumn: "id",
                keyValue: new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a1001"),
                columns: new[] { "configuration_hash", "thresholds_json" },
                values: new object[] { "641FC0A9B5A4B4768E6F82D09755C3F453BA55DFCABCEC42620E168C88A2EB25", "{\"min_query_consumption_pct\":1,\"reg03_min_estimated_rows\":10000,\"reg03_min_predicate_volume_rows\":1000,\"reg03_rows_read_to_returned_ratio\":10,\"reg04_min_estimated_executions\":1000,\"reg07_cardinality_ratio\":10,\"reg08_min_executions_per_plan\":100,\"reg08_regression_increase_pct\":50,\"reg10_duration_max_min_ratio\":5}" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "analysis",
                table: "rule_catalog",
                keyColumn: "id",
                keyValue: new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a0003"),
                column: "default_thresholds_json",
                value: "{\"min_estimated_rows\":10000,\"min_query_consumption_pct\":1,\"rows_read_to_returned_ratio\":10}");

            migrationBuilder.UpdateData(
                schema: "analysis",
                table: "threshold_set",
                keyColumn: "id",
                keyValue: new Guid("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a1001"),
                columns: new[] { "configuration_hash", "thresholds_json" },
                values: new object[] { "8182349B6FF3DAD35F1B6FCCBD38C6AA8B0AFDDF9E7B91C28EBB061D8502E61E", "{\"min_query_consumption_pct\":1,\"reg03_min_estimated_rows\":10000,\"reg03_rows_read_to_returned_ratio\":10,\"reg04_min_estimated_executions\":1000,\"reg07_cardinality_ratio\":10,\"reg08_min_executions_per_plan\":100,\"reg08_regression_increase_pct\":50,\"reg10_duration_max_min_ratio\":5}" });
        }
    }
}
