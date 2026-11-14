namespace QueryDoctor.Domain.Plans;

/// <summary>
/// Representación tipada e inmutable de un documento Showplan XML (RF-MOT-01, RNF-COM-02).
/// Conserva la versión del motor (<see cref="EngineVersion"/>) y las sentencias del lote.
/// </summary>
public sealed record ParsedPlan(
    string? SchemaVersion,
    string? EngineVersion,
    IReadOnlyList<PlanStatement> Statements)
{
    /// <summary>
    /// Enumera todos los operadores de todas las sentencias en preorden para evaluación en un solo recorrido (RNF-RND-04).
    /// </summary>
    public IEnumerable<PlanOperatorNode> EnumerateAllOperators() =>
        Statements.SelectMany(statement => statement.EnumerateOperators());
}

/// <summary>
/// Sentencia individual dentro de un lote Showplan XML (por ejemplo, <c>StmtSimple</c>).
/// </summary>
public sealed record PlanStatement(
    int StatementId,
    string? StatementType,
    string? StatementText,
    double? StatementSubTreeCost,
    double? StatementEstRows,
    string? StatementOptmLevel,
    string? StatementOptmEarlyAbortReason,
    int? CardinalityEstimationModelVersion,
    string? QueryHash,
    string? QueryPlanHash,
    bool IsDispatcherPlan,
    PlanOperatorNode? RootOperator,
    IReadOnlyList<PlanWarning> Warnings,
    IReadOnlyList<MissingIndexGroup> MissingIndexGroups,
    IReadOnlyList<PlanParameter> Parameters,
    MemoryGrantSummary? MemoryGrant)
{
    /// <summary>
    /// Enumera el árbol de operadores de esta sentencia en preorden.
    /// Si es un plan Dispatcher de PSP sin <c>RelOp</c>, devuelve una secuencia vacía.
    /// </summary>
    public IEnumerable<PlanOperatorNode> EnumerateOperators() =>
        RootOperator is null ? [] : RootOperator.EnumerateSubtree();
}

/// <summary>
/// Nodo tipado del árbol de operadores (<c>RelOp</c>) de un Showplan XML (RF-MOT-01).
/// </summary>
public sealed record PlanOperatorNode(
    int NodeId,
    string PhysicalOp,
    string LogicalOp,
    double EstimateRows,
    double? EstimatedRowsRead,
    double? TableCardinality,
    double EstimateIO,
    double EstimateCPU,
    double AvgRowSize,
    double EstimatedTotalSubtreeCost,
    double EstimateRebinds,
    double EstimateRewinds,
    bool IsParallel,
    string? EstimatedExecutionMode,
    bool IsLookup,
    bool? IsOrdered,
    PlanObjectReference? TargetObject,
    IReadOnlyList<PlanColumnReference> OutputColumns,
    IReadOnlyList<PlanColumnReference> SeekColumns,
    string? PredicateScalarString,
    IReadOnlyList<ImplicitConversionInfo> ImplicitConversions,
    IReadOnlyList<string> ConstantLiterals,
    IReadOnlyList<PlanWarning> Warnings,
    IReadOnlyList<PlanOperatorNode> Children)
{
    /// <summary>
    /// Recorre este operador y sus descendientes en preorden de manera iterativa (sin riesgo de desbordamiento de pila en planes profundos como F8).
    /// </summary>
    public IEnumerable<PlanOperatorNode> EnumerateSubtree()
    {
        var stack = new Stack<PlanOperatorNode>();
        stack.Push(this);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            for (var i = current.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(current.Children[i]);
            }
        }
    }
}

/// <summary>
/// Objeto de base de datos (tabla/índice) referenciado por un operador físico.
/// Todos los identificadores se entregan normalizados sin corchetes exteriores <c>[...]</c>.
/// </summary>
public sealed record PlanObjectReference(
    string? Database,
    string? Schema,
    string? Table,
    string? Index,
    string? Alias,
    string? IndexKind,
    string? Storage);

/// <summary>
/// Columna referenciada en listas de salida, predicados o claves de búsqueda.
/// </summary>
public sealed record PlanColumnReference(
    string? Database,
    string? Schema,
    string? Table,
    string? Alias,
    string Column);

/// <summary>
/// Conversión implícita detectada dentro de una expresión escalar (<c>&lt;Convert Implicit="1"&gt;</c>, REG-01).
/// </summary>
public sealed record ImplicitConversionInfo(
    string TargetDataType,
    int? Length,
    string? ScalarString,
    PlanColumnReference? Column);

/// <summary>
/// Tipo de advertencia emitida en tiempo de compilación o ejecución dentro del Showplan XML.
/// </summary>
public enum PlanWarningKind
{
    PlanAffectingConvert,
    NoJoinPredicate,
    ColumnsWithNoStatistics,
    SpillToTempDb,
    SortSpillDetails,
    HashSpillDetails,
    MemoryGrantWarning,
    Other,
}

/// <summary>
/// Advertencia asociada al plan completo o a un operador individual.
/// </summary>
public sealed record PlanWarning(
    PlanWarningKind Kind,
    string? ConvertIssue = null,
    string? Expression = null,
    int? SpillLevel = null,
    int? SpilledThreadCount = null,
    string? Details = null);

/// <summary>
/// Grupo de sugerencias de índices faltantes emitido por el optimizador (<c>MissingIndexGroup</c>, REG-02).
/// </summary>
public sealed record MissingIndexGroup(
    double Impact,
    IReadOnlyList<MissingIndexSuggestion> Indexes);

/// <summary>
/// Sugerencia individual de índice faltante sobre una tabla (<c>MissingIndex</c>, REG-02).
/// </summary>
public sealed record MissingIndexSuggestion(
    string? Database,
    string? Schema,
    string Table,
    IReadOnlyList<string> EqualityColumns,
    IReadOnlyList<string> InequalityColumns,
    IReadOnlyList<string> IncludeColumns);

/// <summary>
/// Parámetro compilado en el plan (<c>ParameterList</c>).
/// </summary>
public sealed record PlanParameter(
    string Name,
    string? DataType,
    string? CompiledValue);

/// <summary>
/// Resumen de concesión de memoria del plan (<c>MemoryGrantInfo</c>).
/// </summary>
public sealed record MemoryGrantSummary(
    long? SerialRequiredMemoryKb,
    long? SerialDesiredMemoryKb,
    long? GrantedMemoryKb,
    long? MaxUsedMemoryKb);
