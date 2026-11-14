using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Infrastructure.Persistence.Seeds;

/// <summary>
/// Catálogo inicial curado de la base de conocimiento para las reglas REG-01 a REG-04 (S1-17, RF-IA-04, RF-IA-03).
/// Todas las fuentes corresponden a documentación técnica oficial de Microsoft Learn y arquitectura de SQL Server.
/// </summary>
public static class KnowledgeBaseSeedData
{
    private static readonly DateTimeOffset FixedSeedDate = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    // Identificadores fijos para fragmentos
    public static readonly Guid FragmentReg01InterpretacionId = Guid.Parse("a1010001-0001-0000-0000-000000000001");
    public static readonly Guid FragmentReg01RecomendacionId = Guid.Parse("a1010001-0001-0000-0000-000000000002");

    public static readonly Guid FragmentReg02InterpretacionId = Guid.Parse("a1010002-0002-0000-0000-000000000001");
    public static readonly Guid FragmentReg02RecomendacionId = Guid.Parse("a1010002-0002-0000-0000-000000000002");

    public static readonly Guid FragmentReg03InterpretacionId = Guid.Parse("a1010003-0003-0000-0000-000000000001");
    public static readonly Guid FragmentReg03RecomendacionId = Guid.Parse("a1010003-0003-0000-0000-000000000002");

    public static readonly Guid FragmentReg04InterpretacionId = Guid.Parse("a1010004-0004-0000-0000-000000000001");
    public static readonly Guid FragmentReg04RecomendacionId = Guid.Parse("a1010004-0004-0000-0000-000000000002");

    // Identificadores fijos para relaciones causales
    public static readonly Guid RelationReg01InterpretacionId = Guid.Parse("b1010001-0001-0000-0000-000000000001");
    public static readonly Guid RelationReg01RecomendacionId = Guid.Parse("b1010001-0001-0000-0000-000000000002");

    public static readonly Guid RelationReg02InterpretacionId = Guid.Parse("b1010002-0002-0000-0000-000000000001");
    public static readonly Guid RelationReg02RecomendacionId = Guid.Parse("b1010002-0002-0000-0000-000000000002");

    public static readonly Guid RelationReg03InterpretacionId = Guid.Parse("b1010003-0003-0000-0000-000000000001");
    public static readonly Guid RelationReg03RecomendacionId = Guid.Parse("b1010003-0003-0000-0000-000000000002");

    public static readonly Guid RelationReg04InterpretacionId = Guid.Parse("b1010004-0004-0000-0000-000000000001");
    public static readonly Guid RelationReg04RecomendacionId = Guid.Parse("b1010004-0004-0000-0000-000000000002");

    public static IReadOnlyList<KbFragment> GetFragments() =>
    [
        // REG-01: Conversión implícita
        new KbFragment
        {
            Id = FragmentReg01InterpretacionId,
            RuleId = "REG-01",
            Title = "Precedencia de tipos de datos y degradación a Index Scan por CONVERT_IMPLICIT",
            Content = "En SQL Server, cuando un operador combina expresiones con tipos de datos diferentes, la precedencia de tipos determina que el tipo con menor precedencia se convierta al de mayor precedencia (por ejemplo, varchar a nvarchar). Si una columna indexada tiene menor precedencia que el parámetro o literal evaluado, el motor aplica la función escalar CONVERT_IMPLICIT sobre la columna. Esta transformación anula la sargabilidad del predicado, impidiendo que el optimizador realice una búsqueda puntual (Index Seek) y forzando un recorrido completo del índice (Index Scan), lo que incrementa notablemente el consumo de CPU y las lecturas lógicas.",
            SourceTitle = "Microsoft Learn: Precedencia de tipo de datos (Transact-SQL)",
            SourceUrl = "https://learn.microsoft.com/es-es/sql/t-sql/data-types/data-type-precedence-transact-sql",
            SourceCitation = "Sección 'Precedencia de los operadores' y 'Conversión de tipos de datos en el motor de base de datos'.",
            CreatedAt = FixedSeedDate
        },
        new KbFragment
        {
            Id = FragmentReg01RecomendacionId,
            RuleId = "REG-01",
            Title = "Alineación de tipos de datos entre parámetros y columnas para habilitar Index Seek",
            Content = "Para solucionar la advertencia de conversión implícita, alinee el tipo de datos del parámetro o variable con el tipo exacto definido en la columna de la tabla. En clientes y capas de datos (como SqlClient, EF Core o Dapper), evite enviar parámetros Unicode (nvarchar con prefijo N) cuando la columna destino sea varchar o char, o altere la columna subyacente para unificar tipos. Al eliminar CONVERT_IMPLICIT sobre la columna indexada, el optimizador recupera la sargabilidad y puede generar un operador Index Seek con predicado puntual eficiente.",
            SourceTitle = "Microsoft Learn: Guía de arquitectura y diseño de índices de SQL Server",
            SourceUrl = "https://learn.microsoft.com/es-es/sql/relational-databases/sql-server-index-design-guide",
            SourceCitation = "Sección 'Recomendaciones de diseño de índices' y 'Predicados sargables'.",
            CreatedAt = FixedSeedDate
        },

        // REG-02: Índice faltante
        new KbFragment
        {
            Id = FragmentReg02InterpretacionId,
            RuleId = "REG-02",
            Title = "Detección de índices faltantes por el optimizador basado en costos",
            Content = "Durante la fase de optimización basada en costos, el procesador de consultas evalúa los índices existentes frente a los predicados de filtro y unión. Si determina que un predicado carece de un índice que cubra las columnas evaluadas, genera una sugerencia en el elemento MissingIndexes del Showplan XML con un porcentaje de impacto estimado (Impact). Dicho porcentaje cuantifica la reducción teórica del costo total del plan si existiera un índice no agrupado óptimo con las columnas de igualdad (EqualityColumns), desigualdad (InequalityColumns) e inclusión (IncludeColumns).",
            SourceTitle = "Microsoft Learn: Optimización de índices no agrupados con sugerencias de índices que faltan",
            SourceUrl = "https://learn.microsoft.com/es-es/sql/relational-databases/indexes/tune-nonclustered-missing-index-suggestions",
            SourceCitation = "Sección 'Uso de la característica de índices faltantes para ajustar índices no agrupados'.",
            CreatedAt = FixedSeedDate
        },
        new KbFragment
        {
            Id = FragmentReg02RecomendacionId,
            RuleId = "REG-02",
            Title = "Diseño ponderado de índices no agrupados y evaluación del costo de escritura",
            Content = "Antes de crear un índice sugerido por MissingIndexes, examine si un índice existente puede adaptarse incorporando columnas en la cláusula INCLUDE, en lugar de crear un índice nuevo. Cada índice adicional consume almacenamiento y penaliza las operaciones DML (INSERT, UPDATE, DELETE), ya que el motor debe mantener actualizadas todas las páginas del árbol B. Diseñe el índice colocando las columnas de igualdad al inicio de la clave, seguidas por las columnas de desigualdad, y asigne las columnas proyectadas a la cláusula INCLUDE para no engrosar los niveles intermedios.",
            SourceTitle = "Microsoft Learn: Directrices generales para el diseño de índices",
            SourceUrl = "https://learn.microsoft.com/es-es/sql/relational-databases/sql-server-index-design-guide#general-index-design-guidelines",
            SourceCitation = "Sección 'Costo de mantenimiento de índices frente a beneficio en consultas de lectura'.",
            CreatedAt = FixedSeedDate
        },

        // REG-03: Recorrido completo contextual
        new KbFragment
        {
            Id = FragmentReg03InterpretacionId,
            RuleId = "REG-03",
            Title = "Lectura secuencial completa de páginas por falta de acceso indexado sargable",
            Content = "Un Clustered Index Scan o Table Scan obliga al motor de base de datos a leer secuencialmente todas las páginas de datos asignadas a la tabla. Cuando la consulta especifica una cláusula WHERE selectiva pero no existe un índice ordenado que permita recorrer el árbol B hasta las filas requeridas, el procesador evalúa el filtro fila por fila como un predicado residual (Predicate) tras cargar cada página en el buffer pool, lo que satura la memoria intermedia, multiplica las lecturas lógicas y eleva el uso de CPU.",
            SourceTitle = "Microsoft Learn: Guía de arquitectura de procesamiento de consultas",
            SourceUrl = "https://learn.microsoft.com/es-es/sql/relational-databases/query-processing-architecture-guide",
            SourceCitation = "Sección 'Operadores lógicos y físicos de Showplan: Recorrido de índice agrupado y Recorrido de tabla'.",
            CreatedAt = FixedSeedDate
        },
        new KbFragment
        {
            Id = FragmentReg03RecomendacionId,
            RuleId = "REG-03",
            Title = "Creación de índice no agrupado selectivo o índice filtrado para reemplazar escaneos",
            Content = "Para sustituir un escaneo completo de tabla por un Index Seek altamente eficiente, determine las columnas del predicado de búsqueda más selectivo y cree un índice no agrupado adecuado. Si las consultas filtran con frecuencia sobre un subconjunto conocido de filas (por ejemplo, registros con estado activo o pedidos pendientes), implemente un índice filtrado (Filtered Index) con una cláusula WHERE. Los índices filtrados tienen menor tamaño, requieren menos mantenimiento en operaciones DML y garantizan búsquedas directas en el árbol B.",
            SourceTitle = "Microsoft Learn: Crear índices no agrupados",
            SourceUrl = "https://learn.microsoft.com/es-es/sql/relational-databases/indexes/create-nonclustered-indexes",
            SourceCitation = "Sección 'Uso de índices no agrupados para optimizar consultas con predicados selectivos'.",
            CreatedAt = FixedSeedDate
        },

        // REG-04: Key Lookup contextual
        new KbFragment
        {
            Id = FragmentReg04InterpretacionId,
            RuleId = "REG-04",
            Title = "Sobrecarga de lecturas por saltos al índice agrupado base en Key Lookup",
            Content = "Un operador Key Lookup (Clustered) ocurre cuando una consulta aprovecha un índice no agrupado para localizar filas mediante un Index Seek, pero la lista de selección (SELECT) o condiciones posteriores requieren columnas que no están presentes en dicho índice. Por cada fila recuperada en el Index Seek, el operador Nested Loops realiza una búsqueda puntual individual en el índice agrupado base para recuperar las columnas faltantes. Cuando la cardinalidad resultante es alta, la multiplicación de búsquedas puntuales supera ampliamente el costo de un escaneo completo y dispara las lecturas lógicas.",
            SourceTitle = "Microsoft Learn: Referencia de operadores lógicos y físicos de Showplan",
            SourceUrl = "https://learn.microsoft.com/es-es/sql/relational-databases/showplan-logical-and-physical-operators-reference",
            SourceCitation = "Operador físico 'Clustered Index Seek' con Lookup='true' / 'Key Lookup'.",
            CreatedAt = FixedSeedDate
        },
        new KbFragment
        {
            Id = FragmentReg04RecomendacionId,
            RuleId = "REG-04",
            Title = "Cobertura de índice mediante cláusula INCLUDE para erradicar Key Lookups",
            Content = "Para eliminar de forma definitiva un Key Lookup costoso, convierta el índice no agrupado en un índice de cobertura (Covering Index) agregando las columnas proyectadas faltantes a la cláusula INCLUDE de la instrucción CREATE INDEX. Las columnas en INCLUDE se almacenan exclusivamente en las páginas del nivel hoja del índice y no participan en las claves de ordenación, permitiendo que la consulta se resuelva íntegramente desde el índice no agrupado sin incrementar la profundidad del árbol B ni sobrecargar los costos de mantenimiento de las claves.",
            SourceTitle = "Microsoft Learn: Crear índices con columnas incluidas",
            SourceUrl = "https://learn.microsoft.com/es-es/sql/relational-databases/indexes/create-indexes-with-included-columns",
            SourceCitation = "Sección 'Directrices de diseño para columnas incluidas'.",
            CreatedAt = FixedSeedDate
        }
    ];

    public static IReadOnlyList<KbCausalRelation> GetCausalRelations() =>
    [
        // REG-01
        new KbCausalRelation
        {
            Id = RelationReg01InterpretacionId,
            RuleId = "REG-01",
            FragmentId = FragmentReg01InterpretacionId,
            RelationType = CausalRelationType.Interpretacion
        },
        new KbCausalRelation
        {
            Id = RelationReg01RecomendacionId,
            RuleId = "REG-01",
            FragmentId = FragmentReg01RecomendacionId,
            RelationType = CausalRelationType.Recomendacion
        },

        // REG-02
        new KbCausalRelation
        {
            Id = RelationReg02InterpretacionId,
            RuleId = "REG-02",
            FragmentId = FragmentReg02InterpretacionId,
            RelationType = CausalRelationType.Interpretacion
        },
        new KbCausalRelation
        {
            Id = RelationReg02RecomendacionId,
            RuleId = "REG-02",
            FragmentId = FragmentReg02RecomendacionId,
            RelationType = CausalRelationType.Recomendacion
        },

        // REG-03
        new KbCausalRelation
        {
            Id = RelationReg03InterpretacionId,
            RuleId = "REG-03",
            FragmentId = FragmentReg03InterpretacionId,
            RelationType = CausalRelationType.Interpretacion
        },
        new KbCausalRelation
        {
            Id = RelationReg03RecomendacionId,
            RuleId = "REG-03",
            FragmentId = FragmentReg03RecomendacionId,
            RelationType = CausalRelationType.Recomendacion
        },

        // REG-04
        new KbCausalRelation
        {
            Id = RelationReg04InterpretacionId,
            RuleId = "REG-04",
            FragmentId = FragmentReg04InterpretacionId,
            RelationType = CausalRelationType.Interpretacion
        },
        new KbCausalRelation
        {
            Id = RelationReg04RecomendacionId,
            RuleId = "REG-04",
            FragmentId = FragmentReg04RecomendacionId,
            RelationType = CausalRelationType.Recomendacion
        }
    ];
}
