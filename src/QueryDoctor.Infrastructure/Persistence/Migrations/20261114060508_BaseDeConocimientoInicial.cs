using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QueryDoctor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BaseDeConocimientoInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "kb_fragment",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    source_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    source_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    source_citation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kb_fragment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kb_causal_relation",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fragment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relation_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kb_causal_relation", x => x.id);
                    table.ForeignKey(
                        name: "FK_kb_causal_relation_kb_fragment_fragment_id",
                        column: x => x.fragment_id,
                        principalSchema: "ai",
                        principalTable: "kb_fragment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "ai",
                table: "kb_fragment",
                columns: new[] { "id", "content", "created_at", "rule_id", "source_citation", "source_title", "source_url", "title" },
                values: new object[,]
                {
                    { new Guid("a1010001-0001-0000-0000-000000000001"), "En SQL Server, cuando un operador combina expresiones con tipos de datos diferentes, la precedencia de tipos determina que el tipo con menor precedencia se convierta al de mayor precedencia (por ejemplo, varchar a nvarchar). Si una columna indexada tiene menor precedencia que el parámetro o literal evaluado, el motor aplica la función escalar CONVERT_IMPLICIT sobre la columna. Esta transformación anula la sargabilidad del predicado, impidiendo que el optimizador realice una búsqueda puntual (Index Seek) y forzando un recorrido completo del índice (Index Scan), lo que incrementa notablemente el consumo de CPU y las lecturas lógicas.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-01", "Sección 'Precedencia de los operadores' y 'Conversión de tipos de datos en el motor de base de datos'.", "Microsoft Learn: Precedencia de tipo de datos (Transact-SQL)", "https://learn.microsoft.com/es-es/sql/t-sql/data-types/data-type-precedence-transact-sql", "Precedencia de tipos de datos y degradación a Index Scan por CONVERT_IMPLICIT" },
                    { new Guid("a1010001-0001-0000-0000-000000000002"), "Para solucionar la advertencia de conversión implícita, alinee el tipo de datos del parámetro o variable con el tipo exacto definido en la columna de la tabla. En clientes y capas de datos (como SqlClient, EF Core o Dapper), evite enviar parámetros Unicode (nvarchar con prefijo N) cuando la columna destino sea varchar o char, o altere la columna subyacente para unificar tipos. Al eliminar CONVERT_IMPLICIT sobre la columna indexada, el optimizador recupera la sargabilidad y puede generar un operador Index Seek con predicado puntual eficiente.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-01", "Sección 'Recomendaciones de diseño de índices' y 'Predicados sargables'.", "Microsoft Learn: Guía de arquitectura y diseño de índices de SQL Server", "https://learn.microsoft.com/es-es/sql/relational-databases/sql-server-index-design-guide", "Alineación de tipos de datos entre parámetros y columnas para habilitar Index Seek" },
                    { new Guid("a1010002-0002-0000-0000-000000000001"), "Durante la fase de optimización basada en costos, el procesador de consultas evalúa los índices existentes frente a los predicados de filtro y unión. Si determina que un predicado carece de un índice que cubra las columnas evaluadas, genera una sugerencia en el elemento MissingIndexes del Showplan XML con un porcentaje de impacto estimado (Impact). Dicho porcentaje cuantifica la reducción teórica del costo total del plan si existiera un índice no agrupado óptimo con las columnas de igualdad (EqualityColumns), desigualdad (InequalityColumns) e inclusión (IncludeColumns).", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-02", "Sección 'Uso de la característica de índices faltantes para ajustar índices no agrupados'.", "Microsoft Learn: Optimización de índices no agrupados con sugerencias de índices que faltan", "https://learn.microsoft.com/es-es/sql/relational-databases/indexes/tune-nonclustered-missing-index-suggestions", "Detección de índices faltantes por el optimizador basado en costos" },
                    { new Guid("a1010002-0002-0000-0000-000000000002"), "Antes de crear un índice sugerido por MissingIndexes, examine si un índice existente puede adaptarse incorporando columnas en la cláusula INCLUDE, en lugar de crear un índice nuevo. Cada índice adicional consume almacenamiento y penaliza las operaciones DML (INSERT, UPDATE, DELETE), ya que el motor debe mantener actualizadas todas las páginas del árbol B. Diseñe el índice colocando las columnas de igualdad al inicio de la clave, seguidas por las columnas de desigualdad, y asigne las columnas proyectadas a la cláusula INCLUDE para no engrosar los niveles intermedios.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-02", "Sección 'Costo de mantenimiento de índices frente a beneficio en consultas de lectura'.", "Microsoft Learn: Directrices generales para el diseño de índices", "https://learn.microsoft.com/es-es/sql/relational-databases/sql-server-index-design-guide#general-index-design-guidelines", "Diseño ponderado de índices no agrupados y evaluación del costo de escritura" },
                    { new Guid("a1010003-0003-0000-0000-000000000001"), "Un Clustered Index Scan o Table Scan obliga al motor de base de datos a leer secuencialmente todas las páginas de datos asignadas a la tabla. Cuando la consulta especifica una cláusula WHERE selectiva pero no existe un índice ordenado que permita recorrer el árbol B hasta las filas requeridas, el procesador evalúa el filtro fila por fila como un predicado residual (Predicate) tras cargar cada página en el buffer pool, lo que satura la memoria intermedia, multiplica las lecturas lógicas y eleva el uso de CPU.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-03", "Sección 'Operadores lógicos y físicos de Showplan: Recorrido de índice agrupado y Recorrido de tabla'.", "Microsoft Learn: Guía de arquitectura de procesamiento de consultas", "https://learn.microsoft.com/es-es/sql/relational-databases/query-processing-architecture-guide", "Lectura secuencial completa de páginas por falta de acceso indexado sargable" },
                    { new Guid("a1010003-0003-0000-0000-000000000002"), "Para sustituir un escaneo completo de tabla por un Index Seek altamente eficiente, determine las columnas del predicado de búsqueda más selectivo y cree un índice no agrupado adecuado. Si las consultas filtran con frecuencia sobre un subconjunto conocido de filas (por ejemplo, registros con estado activo o pedidos pendientes), implemente un índice filtrado (Filtered Index) con una cláusula WHERE. Los índices filtrados tienen menor tamaño, requieren menos mantenimiento en operaciones DML y garantizan búsquedas directas en el árbol B.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-03", "Sección 'Uso de índices no agrupados para optimizar consultas con predicados selectivos'.", "Microsoft Learn: Crear índices no agrupados", "https://learn.microsoft.com/es-es/sql/relational-databases/indexes/create-nonclustered-indexes", "Creación de índice no agrupado selectivo o índice filtrado para reemplazar escaneos" },
                    { new Guid("a1010004-0004-0000-0000-000000000001"), "Un operador Key Lookup (Clustered) ocurre cuando una consulta aprovecha un índice no agrupado para localizar filas mediante un Index Seek, pero la lista de selección (SELECT) o condiciones posteriores requieren columnas que no están presentes en dicho índice. Por cada fila recuperada en el Index Seek, el operador Nested Loops realiza una búsqueda puntual individual en el índice agrupado base para recuperar las columnas faltantes. Cuando la cardinalidad resultante es alta, la multiplicación de búsquedas puntuales supera ampliamente el costo de un escaneo completo y dispara las lecturas lógicas.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-04", "Operador físico 'Clustered Index Seek' con Lookup='true' / 'Key Lookup'.", "Microsoft Learn: Referencia de operadores lógicos y físicos de Showplan", "https://learn.microsoft.com/es-es/sql/relational-databases/showplan-logical-and-physical-operators-reference", "Sobrecarga de lecturas por saltos al índice agrupado base en Key Lookup" },
                    { new Guid("a1010004-0004-0000-0000-000000000002"), "Para eliminar de forma definitiva un Key Lookup costoso, convierta el índice no agrupado en un índice de cobertura (Covering Index) agregando las columnas proyectadas faltantes a la cláusula INCLUDE de la instrucción CREATE INDEX. Las columnas en INCLUDE se almacenan exclusivamente en las páginas del nivel hoja del índice y no participan en las claves de ordenación, permitiendo que la consulta se resuelva íntegramente desde el índice no agrupado sin incrementar la profundidad del árbol B ni sobrecargar los costos de mantenimiento de las claves.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REG-04", "Sección 'Directrices de diseño para columnas incluidas'.", "Microsoft Learn: Crear índices con columnas incluidas", "https://learn.microsoft.com/es-es/sql/relational-databases/indexes/create-indexes-with-included-columns", "Cobertura de índice mediante cláusula INCLUDE para erradicar Key Lookups" }
                });

            migrationBuilder.InsertData(
                schema: "ai",
                table: "kb_causal_relation",
                columns: new[] { "id", "fragment_id", "relation_type", "rule_id" },
                values: new object[,]
                {
                    { new Guid("b1010001-0001-0000-0000-000000000001"), new Guid("a1010001-0001-0000-0000-000000000001"), "Interpretacion", "REG-01" },
                    { new Guid("b1010001-0001-0000-0000-000000000002"), new Guid("a1010001-0001-0000-0000-000000000002"), "Recomendacion", "REG-01" },
                    { new Guid("b1010002-0002-0000-0000-000000000001"), new Guid("a1010002-0002-0000-0000-000000000001"), "Interpretacion", "REG-02" },
                    { new Guid("b1010002-0002-0000-0000-000000000002"), new Guid("a1010002-0002-0000-0000-000000000002"), "Recomendacion", "REG-02" },
                    { new Guid("b1010003-0003-0000-0000-000000000001"), new Guid("a1010003-0003-0000-0000-000000000001"), "Interpretacion", "REG-03" },
                    { new Guid("b1010003-0003-0000-0000-000000000002"), new Guid("a1010003-0003-0000-0000-000000000002"), "Recomendacion", "REG-03" },
                    { new Guid("b1010004-0004-0000-0000-000000000001"), new Guid("a1010004-0004-0000-0000-000000000001"), "Interpretacion", "REG-04" },
                    { new Guid("b1010004-0004-0000-0000-000000000002"), new Guid("a1010004-0004-0000-0000-000000000002"), "Recomendacion", "REG-04" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_kb_causal_relation_fragment_id",
                schema: "ai",
                table: "kb_causal_relation",
                column: "fragment_id");

            migrationBuilder.CreateIndex(
                name: "IX_kb_causal_relation_rule_id",
                schema: "ai",
                table: "kb_causal_relation",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_kb_causal_relation_rule_id_fragment_id_relation_type",
                schema: "ai",
                table: "kb_causal_relation",
                columns: new[] { "rule_id", "fragment_id", "relation_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kb_fragment_rule_id",
                schema: "ai",
                table: "kb_fragment",
                column: "rule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kb_causal_relation",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "kb_fragment",
                schema: "ai");
        }
    }
}
