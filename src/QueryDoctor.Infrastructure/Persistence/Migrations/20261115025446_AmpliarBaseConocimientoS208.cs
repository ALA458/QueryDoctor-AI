using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AmpliarBaseConocimientoS208 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "ai",
                table: "kb_fragment",
                columns: new[] { "id", "content", "created_at", "rule_id", "source_citation", "source_title", "source_url", "title" },
                values: new object[,]
                {
                    { new Guid("a1010003-0003-0000-0000-000000000003"), "Cuando un operador Table Scan, Clustered Index Scan o Index Scan recorre un volumen elevado de filas en una consulta con alta participación de consumo dentro de la ventana de Query Store, el motor lee secuencialmente gran cantidad de páginas de datos hacia el buffer pool incluso sin predicado residual selectivo. Este acceso amplio incrementa las lecturas lógicas, presiona la caché de páginas para otras consultas y amplifica el tiempo de CPU y duración acumulada según la frecuencia de ejecución.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-03", "Sección 'Operadores lógicos y físicos de Showplan: Recorrido de tabla y Recorrido de índice agrupado'.", "Microsoft Learn: Guía de arquitectura de procesamiento de consultas", "https://learn.microsoft.com/es-es/sql/relational-databases/query-processing-architecture-guide", "Sobrecarga de E/S y buffer pool en recorridos de alto volumen y consumo" },
                    { new Guid("a1010003-0003-0000-0000-000000000004"), "Para mitigar recorridos completos de alto volumen donde no existe un filtro puntual único, proyecte únicamente las columnas necesarias evitando SELECT * y evalúe un índice no agrupado cubriente más estrecho que el índice agrupado base para reducir el número de páginas leídas por ejecución. Cuando la consulta deba leer la mayoría de la tabla, analice compresión de páginas o particionamiento por rango para acotar las particiones examinadas y disminuir la presión sobre memoria y E/S.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-03", "Sección 'Directrices generales para el diseño de índices' y 'Ancho del índice'.", "Microsoft Learn: Guía de arquitectura y diseño de índices de SQL Server", "https://learn.microsoft.com/es-es/sql/relational-databases/sql-server-index-design-guide", "Índices no agrupados estrechos, cobertura y proyección mínima de columnas" },
                    { new Guid("a1010011-0011-0000-0000-000000000001"), "En SQL Server, las vistas sys.query_store_plan y sys.dm_exec_query_plan_stats exponen el Showplan en formato XML (o texto XML en Query Store), sujeto al límite estructural de 128 niveles de anidamiento del tipo xml y a posibles truncamientos o ausencia de plan compilado en caché. Cuando el documento recibido es nulo, queda incompleto o presenta una estructura XML no válida o no soportada, el analizador no puede construir el árbol de operadores de forma segura y registra el hallazgo informativo REG-11 sin interrumpir el lote.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-11", "Sección 'Observaciones' (límite de niveles de anidamiento del tipo de datos xml en Showplan).", "Microsoft Learn: sys.dm_exec_query_plan_stats (Transact-SQL)", "https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-views/sys-dm-exec-query-plan-stats-transact-sql", "Límites de representación XML de Showplan y planes truncados o no recuperables" },
                    { new Guid("a1010011-0011-0000-0000-000000000002"), "Ante un hallazgo REG-11, utilice las métricas agregadas de ejecución de sys.query_store_runtime_stats para priorizar la consulta aunque el XML no haya sido analizable y consulte sys.query_store_plan en modo de solo lectura para verificar si el plan está vacío, truncado o supera el anidamiento XML permitido. Si el plan supera los 128 niveles por subconsultas, CTE encadenadas o expresiones complejas, refactorice la consulta en pasos más simples o tablas temporales para recuperar un Showplan XML analizable.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-11", "Descripción de la columna 'query_plan' y compatibilidad de representación Showplan XML.", "Microsoft Learn: sys.query_store_plan (Transact-SQL)", "https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-plan-transact-sql", "Inspección de query_plan en Query Store y simplificación de planes profundamente anidados" }
                });

            migrationBuilder.InsertData(
                schema: "ai",
                table: "kb_causal_relation",
                columns: new[] { "id", "fragment_id", "relation_type", "rule_id" },
                values: new object[,]
                {
                    { new Guid("b1010003-0003-0000-0000-000000000003"), new Guid("a1010003-0003-0000-0000-000000000003"), "Interpretacion", "REG-03" },
                    { new Guid("b1010003-0003-0000-0000-000000000004"), new Guid("a1010003-0003-0000-0000-000000000004"), "Recomendacion", "REG-03" },
                    { new Guid("b1010011-0011-0000-0000-000000000001"), new Guid("a1010011-0011-0000-0000-000000000001"), "Interpretacion", "REG-11" },
                    { new Guid("b1010011-0011-0000-0000-000000000002"), new Guid("a1010011-0011-0000-0000-000000000002"), "Recomendacion", "REG-11" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "ai",
                table: "kb_causal_relation",
                keyColumn: "id",
                keyValue: new Guid("b1010003-0003-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                schema: "ai",
                table: "kb_causal_relation",
                keyColumn: "id",
                keyValue: new Guid("b1010003-0003-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                schema: "ai",
                table: "kb_causal_relation",
                keyColumn: "id",
                keyValue: new Guid("b1010011-0011-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "ai",
                table: "kb_causal_relation",
                keyColumn: "id",
                keyValue: new Guid("b1010011-0011-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                schema: "ai",
                table: "kb_fragment",
                keyColumn: "id",
                keyValue: new Guid("a1010003-0003-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                schema: "ai",
                table: "kb_fragment",
                keyColumn: "id",
                keyValue: new Guid("a1010003-0003-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                schema: "ai",
                table: "kb_fragment",
                keyColumn: "id",
                keyValue: new Guid("a1010011-0011-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "ai",
                table: "kb_fragment",
                keyColumn: "id",
                keyValue: new Guid("a1010011-0011-0000-0000-000000000002"));
        }
    }
}
