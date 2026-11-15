using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AfirmacionesExplicacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "claim",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    explanation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_index = table.Column<int>(type: "integer", nullable: false),
                    claim_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    encrypted_text = table.Column<string>(type: "text", nullable: false),
                    kb_fragment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    indication_label = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    passed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim", x => x.id);
                    table.ForeignKey(
                        name: "FK_claim_explanation_explanation_id",
                        column: x => x.explanation_id,
                        principalSchema: "ai",
                        principalTable: "explanation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "claim_reference",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finding_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    node_ids_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim_reference", x => x.id);
                    table.ForeignKey(
                        name: "FK_claim_reference_claim_claim_id",
                        column: x => x.claim_id,
                        principalSchema: "ai",
                        principalTable: "claim",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_claim_explanation_id_claim_index",
                schema: "ai",
                table: "claim",
                columns: new[] { "explanation_id", "claim_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_claim_reference_claim_id",
                schema: "ai",
                table: "claim_reference",
                column: "claim_id");

            migrationBuilder.CreateIndex(
                name: "IX_claim_reference_finding_code",
                schema: "ai",
                table: "claim_reference",
                column: "finding_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "claim_reference",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "claim",
                schema: "ai");
        }
    }
}
