using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrazaYAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_event",
                schema: "audit",
                columns: table => new
                {
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    subject_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    prev_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entry_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_event", x => x.seq);
                });

            migrationBuilder.CreateTable(
                name: "trace_entry",
                schema: "audit",
                columns: table => new
                {
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    explanation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cause = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    validation_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    finding_codes_json = table.Column<string>(type: "text", nullable: false),
                    catalog_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    llm_profile = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    served_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    data_policy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    context_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reused_from_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prev_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entry_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trace_entry", x => x.seq);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_event_event_type_recorded_at",
                schema: "audit",
                table: "audit_event",
                columns: new[] { "event_type", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_event_recorded_at",
                schema: "audit",
                table: "audit_event",
                column: "recorded_at");

            migrationBuilder.CreateIndex(
                name: "IX_trace_entry_explanation_id",
                schema: "audit",
                table: "trace_entry",
                column: "explanation_id");

            migrationBuilder.CreateIndex(
                name: "IX_trace_entry_query_id_recorded_at",
                schema: "audit",
                table: "trace_entry",
                columns: new[] { "query_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_trace_entry_recorded_at",
                schema: "audit",
                table: "trace_entry",
                column: "recorded_at");

            // RF-TRZ-02 / ARQUITECTURA §7: solo adición en la base, no solo en la aplicación. El trigger rechaza
            // UPDATE, DELETE y TRUNCATE; si alguien con privilegios lo desactiva, la cadena de hash lo delata.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit.reject_modification() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.% es de solo adicion (RF-TRZ-02): % no permitido', TG_TABLE_NAME, TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trace_entry_append_only BEFORE UPDATE OR DELETE ON audit.trace_entry
                    FOR EACH ROW EXECUTE FUNCTION audit.reject_modification();
                CREATE TRIGGER trace_entry_no_truncate BEFORE TRUNCATE ON audit.trace_entry
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_modification();
                CREATE TRIGGER audit_event_append_only BEFORE UPDATE OR DELETE ON audit.audit_event
                    FOR EACH ROW EXECUTE FUNCTION audit.reject_modification();
                CREATE TRIGGER audit_event_no_truncate BEFORE TRUNCATE ON audit.audit_event
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_modification();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trace_entry_append_only ON audit.trace_entry;
                DROP TRIGGER IF EXISTS trace_entry_no_truncate ON audit.trace_entry;
                DROP TRIGGER IF EXISTS audit_event_append_only ON audit.audit_event;
                DROP TRIGGER IF EXISTS audit_event_no_truncate ON audit.audit_event;
                DROP FUNCTION IF EXISTS audit.reject_modification();
                """);

            migrationBuilder.DropTable(
                name: "audit_event",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "trace_entry",
                schema: "audit");
        }
    }
}
