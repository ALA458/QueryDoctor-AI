using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExplicacionesIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "explanation",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cause = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    validation_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    context_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    finding_codes_json = table.Column<string>(type: "text", nullable: false),
                    catalog_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    llm_profile = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    served_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    data_policy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    encrypted_body = table.Column<string>(type: "text", nullable: true),
                    reused_from_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_explanation", x => x.id);
                    table.ForeignKey(
                        name: "FK_explanation_connection_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "core",
                        principalTable: "connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_explanation_explanation_reused_from_id",
                        column: x => x.reused_from_id,
                        principalSchema: "ai",
                        principalTable: "explanation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_explanation_query_query_id",
                        column: x => x.query_id,
                        principalSchema: "core",
                        principalTable: "query",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_explanation_connection_id",
                schema: "ai",
                table: "explanation",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_explanation_context_hash",
                schema: "ai",
                table: "explanation",
                column: "context_hash");

            migrationBuilder.CreateIndex(
                name: "IX_explanation_query_id_requested_at",
                schema: "ai",
                table: "explanation",
                columns: new[] { "query_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_explanation_reused_from_id",
                schema: "ai",
                table: "explanation",
                column: "reused_from_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "explanation",
                schema: "ai");
        }
    }
}
