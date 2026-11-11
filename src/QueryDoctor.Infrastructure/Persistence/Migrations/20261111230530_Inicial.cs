using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QueryDoctor.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class Inicial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "core");

        migrationBuilder.EnsureSchema(
            name: "ops");

        migrationBuilder.CreateTable(
            name: "connection",
            schema: "core",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                server = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                database_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                authentication_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                encryption = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                encrypted_credentials = table.Column<string>(type: "text", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                last_consolidated_interval_id = table.Column<long>(type: "bigint", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_connection", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "roles",
            schema: "ops",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                concurrency_stamp = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_roles", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "users",
            schema: "ops",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                password_hash = table.Column<string>(type: "text", nullable: true),
                security_stamp = table.Column<string>(type: "text", nullable: true),
                concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                phone_number = table.Column<string>(type: "text", nullable: true),
                phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                access_failed_count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "capability_profile",
            schema: "core",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                engine_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                edition = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                support_level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                has_view_database_state = table.Column<bool>(type: "boolean", nullable: false),
                has_excessive_permissions = table.Column<bool>(type: "boolean", nullable: false),
                query_store_actual_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                query_store_desired_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                query_store_read_only_reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                interval_length_minutes = table.Column<int>(type: "integer", nullable: true),
                capture_mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                retention_days = table.Column<int>(type: "integer", nullable: true),
                omitted_features_json = table.Column<string>(type: "text", nullable: true),
                probed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_capability_profile", x => x.id);
                table.ForeignKey(
                    name: "FK_capability_profile_connection_connection_id",
                    column: x => x.connection_id,
                    principalSchema: "core",
                    principalTable: "connection",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ingestion_run",
            schema: "core",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rows_upserted = table.Column<int>(type: "integer", nullable: true),
                error_cause = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ingestion_run", x => x.id);
                table.ForeignKey(
                    name: "FK_ingestion_run_connection_connection_id",
                    column: x => x.connection_id,
                    principalSchema: "core",
                    principalTable: "connection",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "query_text",
            schema: "core",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                query_store_text_id = table.Column<long>(type: "bigint", nullable: false),
                encrypted_sql_text = table.Column<string>(type: "text", nullable: true),
                ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_query_text", x => x.id);
                table.ForeignKey(
                    name: "FK_query_text_connection_connection_id",
                    column: x => x.connection_id,
                    principalSchema: "core",
                    principalTable: "connection",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "role_claims",
            schema: "ops",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                role_id = table.Column<Guid>(type: "uuid", nullable: false),
                claim_type = table.Column<string>(type: "text", nullable: true),
                claim_value = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_role_claims", x => x.id);
                table.ForeignKey(
                    name: "FK_role_claims_roles_role_id",
                    column: x => x.role_id,
                    principalSchema: "ops",
                    principalTable: "roles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_claims",
            schema: "ops",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                claim_type = table.Column<string>(type: "text", nullable: true),
                claim_value = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_claims", x => x.id);
                table.ForeignKey(
                    name: "FK_user_claims_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "ops",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_connection",
            schema: "ops",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_connection", x => new { x.user_id, x.connection_id });
                table.ForeignKey(
                    name: "FK_user_connection_connection_connection_id",
                    column: x => x.connection_id,
                    principalSchema: "core",
                    principalTable: "connection",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_user_connection_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "ops",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_logins",
            schema: "ops",
            columns: table => new
            {
                login_provider = table.Column<string>(type: "text", nullable: false),
                provider_key = table.Column<string>(type: "text", nullable: false),
                provider_display_name = table.Column<string>(type: "text", nullable: true),
                user_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_logins", x => new { x.login_provider, x.provider_key });
                table.ForeignKey(
                    name: "FK_user_logins_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "ops",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_roles",
            schema: "ops",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                role_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_roles", x => new { x.user_id, x.role_id });
                table.ForeignKey(
                    name: "FK_user_roles_roles_role_id",
                    column: x => x.role_id,
                    principalSchema: "ops",
                    principalTable: "roles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_user_roles_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "ops",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_tokens",
            schema: "ops",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                login_provider = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                value = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                table.ForeignKey(
                    name: "FK_user_tokens_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "ops",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "query",
            schema: "core",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                query_store_query_id = table.Column<long>(type: "bigint", nullable: false),
                query_text_id = table.Column<Guid>(type: "uuid", nullable: true),
                query_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                origin_context = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_query", x => x.id);
                table.ForeignKey(
                    name: "FK_query_connection_connection_id",
                    column: x => x.connection_id,
                    principalSchema: "core",
                    principalTable: "connection",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_query_query_text_query_text_id",
                    column: x => x.query_text_id,
                    principalSchema: "core",
                    principalTable: "query_text",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "plan",
            schema: "core",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                query_id = table.Column<Guid>(type: "uuid", nullable: false),
                query_store_plan_id = table.Column<long>(type: "bigint", nullable: false),
                query_plan_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                encrypted_plan_xml = table.Column<string>(type: "text", nullable: true),
                engine_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                analysis_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                not_analyzable_cause = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_plan", x => x.id);
                table.ForeignKey(
                    name: "FK_plan_connection_connection_id",
                    column: x => x.connection_id,
                    principalSchema: "core",
                    principalTable: "connection",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_plan_query_query_id",
                    column: x => x.query_id,
                    principalSchema: "core",
                    principalTable: "query",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "runtime_stat",
            schema: "core",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                execution_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                interval_id = table.Column<long>(type: "bigint", nullable: false),
                interval_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                interval_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                execution_count = table.Column<long>(type: "bigint", nullable: false),
                avg_duration_microseconds = table.Column<double>(type: "double precision", nullable: false),
                avg_cpu_time_microseconds = table.Column<double>(type: "double precision", nullable: false),
                avg_logical_io_reads = table.Column<double>(type: "double precision", nullable: false),
                avg_physical_io_reads = table.Column<double>(type: "double precision", nullable: false),
                avg_logical_io_writes = table.Column<double>(type: "double precision", nullable: false),
                avg_row_count = table.Column<double>(type: "double precision", nullable: false),
                avg_dop = table.Column<double>(type: "double precision", nullable: false),
                avg_max_used_memory_pages = table.Column<double>(type: "double precision", nullable: false),
                avg_temp_db_space_pages = table.Column<double>(type: "double precision", nullable: true),
                avg_log_bytes_used = table.Column<double>(type: "double precision", nullable: true),
                ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_runtime_stat", x => x.id);
                table.ForeignKey(
                    name: "FK_runtime_stat_connection_connection_id",
                    column: x => x.connection_id,
                    principalSchema: "core",
                    principalTable: "connection",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_runtime_stat_plan_plan_id",
                    column: x => x.plan_id,
                    principalSchema: "core",
                    principalTable: "plan",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            schema: "ops",
            table: "roles",
            columns: new[] { "id", "concurrency_stamp", "name", "normalized_name" },
            values: new object[,]
            {
                { new Guid("6f1c3b0e-8a52-4d0b-9c1e-2a7f4e5d1001"), "role-dba-v1", "DBA", "DBA" },
                { new Guid("6f1c3b0e-8a52-4d0b-9c1e-2a7f4e5d1002"), "role-desarrollador-v1", "Desarrollador", "DESARROLLADOR" }
            });

        migrationBuilder.CreateIndex(
            name: "IX_capability_profile_connection_id",
            schema: "core",
            table: "capability_profile",
            column: "connection_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_connection_server_database_name",
            schema: "core",
            table: "connection",
            columns: new[] { "server", "database_name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ingestion_run_connection_id_started_at",
            schema: "core",
            table: "ingestion_run",
            columns: new[] { "connection_id", "started_at" });

        migrationBuilder.CreateIndex(
            name: "IX_plan_connection_id_query_store_plan_id",
            schema: "core",
            table: "plan",
            columns: new[] { "connection_id", "query_store_plan_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_plan_query_id",
            schema: "core",
            table: "plan",
            column: "query_id");

        migrationBuilder.CreateIndex(
            name: "IX_query_connection_id_query_store_query_id",
            schema: "core",
            table: "query",
            columns: new[] { "connection_id", "query_store_query_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_query_query_text_id",
            schema: "core",
            table: "query",
            column: "query_text_id");

        migrationBuilder.CreateIndex(
            name: "IX_query_text_connection_id_query_store_text_id",
            schema: "core",
            table: "query_text",
            columns: new[] { "connection_id", "query_store_text_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_role_claims_role_id",
            schema: "ops",
            table: "role_claims",
            column: "role_id");

        migrationBuilder.CreateIndex(
            name: "RoleNameIndex",
            schema: "ops",
            table: "roles",
            column: "normalized_name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_runtime_stat_connection_id_interval_end",
            schema: "core",
            table: "runtime_stat",
            columns: new[] { "connection_id", "interval_end" });

        migrationBuilder.CreateIndex(
            name: "IX_runtime_stat_connection_id_plan_id_execution_type_interval_~",
            schema: "core",
            table: "runtime_stat",
            columns: new[] { "connection_id", "plan_id", "execution_type", "interval_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_runtime_stat_plan_id",
            schema: "core",
            table: "runtime_stat",
            column: "plan_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_claims_user_id",
            schema: "ops",
            table: "user_claims",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_connection_connection_id",
            schema: "ops",
            table: "user_connection",
            column: "connection_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_logins_user_id",
            schema: "ops",
            table: "user_logins",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_roles_role_id",
            schema: "ops",
            table: "user_roles",
            column: "role_id");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            schema: "ops",
            table: "users",
            column: "normalized_email");

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            schema: "ops",
            table: "users",
            column: "normalized_user_name",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "capability_profile",
            schema: "core");

        migrationBuilder.DropTable(
            name: "ingestion_run",
            schema: "core");

        migrationBuilder.DropTable(
            name: "role_claims",
            schema: "ops");

        migrationBuilder.DropTable(
            name: "runtime_stat",
            schema: "core");

        migrationBuilder.DropTable(
            name: "user_claims",
            schema: "ops");

        migrationBuilder.DropTable(
            name: "user_connection",
            schema: "ops");

        migrationBuilder.DropTable(
            name: "user_logins",
            schema: "ops");

        migrationBuilder.DropTable(
            name: "user_roles",
            schema: "ops");

        migrationBuilder.DropTable(
            name: "user_tokens",
            schema: "ops");

        migrationBuilder.DropTable(
            name: "plan",
            schema: "core");

        migrationBuilder.DropTable(
            name: "roles",
            schema: "ops");

        migrationBuilder.DropTable(
            name: "users",
            schema: "ops");

        migrationBuilder.DropTable(
            name: "query",
            schema: "core");

        migrationBuilder.DropTable(
            name: "query_text",
            schema: "core");

        migrationBuilder.DropTable(
            name: "connection",
            schema: "core");
    }
}
