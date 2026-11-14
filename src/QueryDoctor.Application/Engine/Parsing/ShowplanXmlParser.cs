using System.Globalization;
using System.Xml;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.Application.Engine.Parsing;

/// <summary>
/// Implementación en streaming de <see cref="IPlanParser"/> basada en <see cref="XmlReader"/>
/// (RF-MOT-01, RNF-COM-02, RF-MOT-16, RNF-RND-04, OWASP A05/A10).
/// </summary>
public sealed class ShowplanXmlParser : IPlanParser
{
    private readonly PlanParserOptions _options;

    public ShowplanXmlParser(PlanParserOptions? options = null)
    {
        _options = options ?? new PlanParserOptions();
    }

    /// <inheritdoc />
    public PlanParseResult Parse(
        string? showplanXml,
        string? planIdentifier = null,
        string? fallbackEngineVersion = null)
    {
        if (string.IsNullOrWhiteSpace(showplanXml))
        {
            return PlanParseResult.Failure(
                PlanParseFailureReason.EmptyOrNullXml,
                "El contenido del Showplan XML es nulo o está vacío.",
                planIdentifier);
        }

        if (showplanXml.Length > _options.MaxCharactersInDocument)
        {
            return PlanParseResult.Failure(
                PlanParseFailureReason.SizeLimitExceeded,
                $"El tamaño del Showplan XML ({showplanXml.Length} caracteres) excede el límite permitido ({_options.MaxCharactersInDocument}).",
                planIdentifier);
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = _options.MaxCharactersInDocument,
            MaxCharactersFromEntities = 1024,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        try
        {
            using var stringReader = new StringReader(showplanXml);
            using var reader = XmlReader.Create(stringReader, settings);

            if (reader.MoveToContent() != XmlNodeType.Element ||
                !string.Equals(reader.LocalName, "ShowPlanXML", StringComparison.Ordinal))
            {
                return PlanParseResult.Failure(
                    PlanParseFailureReason.UnexpectedRootElement,
                    $"El elemento raíz '{reader.LocalName}' no corresponde a 'ShowPlanXML'.",
                    planIdentifier);
            }

            var schemaVersion = reader.GetAttribute("Version");
            var engineVersion = reader.GetAttribute("Build");
            if (string.IsNullOrWhiteSpace(engineVersion))
            {
                engineVersion = fallbackEngineVersion;
            }

            var statements = new List<PlanStatement>();

            if (!reader.IsEmptyElement)
            {
                while (reader.Read())
                {
                    EnsureDepthWithinLimit(reader.Depth);

                    if (reader.NodeType == XmlNodeType.Element &&
                        reader.LocalName.StartsWith("Stmt", StringComparison.Ordinal))
                    {
                        statements.Add(ReadStatement(reader));
                    }
                }
            }

            var parsedPlan = new ParsedPlan(schemaVersion, engineVersion, statements);
            return PlanParseResult.Success(parsedPlan, planIdentifier);
        }
        catch (DepthLimitExceededException ex)
        {
            return PlanParseResult.Failure(
                PlanParseFailureReason.MaxDepthExceeded,
                ex.Message,
                planIdentifier);
        }
        catch (XmlException ex)
        {
            var reason = ClassifyXmlException(ex);
            return PlanParseResult.Failure(reason, ex.Message, planIdentifier);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PlanParseResult> ParseBatch(IEnumerable<PlanInput> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var results = new List<PlanParseResult>();
        foreach (var item in plans)
        {
            if (item is null)
            {
                results.Add(PlanParseResult.Failure(
                    PlanParseFailureReason.EmptyOrNullXml,
                    "La entrada del plan en el lote es nula."));
                continue;
            }

            results.Add(Parse(item.ShowplanXml, item.PlanIdentifier, item.FallbackEngineVersion));
        }

        return results;
    }

    private PlanStatement ReadStatement(XmlReader reader)
    {
        var statementDepth = reader.Depth;
        var statementId = ParseInt(reader.GetAttribute("StatementId")) ?? 0;
        var statementType = reader.GetAttribute("StatementType");
        var statementText = reader.GetAttribute("StatementText");
        var subTreeCost = ParseDouble(reader.GetAttribute("StatementSubTreeCost"));
        var estRows = ParseDouble(reader.GetAttribute("StatementEstRows"));
        var optmLevel = reader.GetAttribute("StatementOptmLevel");
        var earlyAbortReason = reader.GetAttribute("StatementOptmEarlyAbortReason");
        var ceModelVersion = ParseInt(reader.GetAttribute("CardinalityEstimationModelVersion"));
        var queryHash = reader.GetAttribute("QueryHash");
        var queryPlanHash = reader.GetAttribute("QueryPlanHash");

        var isDispatcher = false;
        PlanOperatorNode? rootOperator = null;
        var warnings = new List<PlanWarning>();
        var missingIndexGroups = new List<MissingIndexGroup>();
        var parameters = new List<PlanParameter>();
        MemoryGrantSummary? memoryGrant = null;

        if (!reader.IsEmptyElement)
        {
            while (reader.Read())
            {
                EnsureDepthWithinLimit(reader.Depth);

                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == statementDepth)
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                switch (reader.LocalName)
                {
                    case "Dispatcher":
                        isDispatcher = true;
                        SkipCurrentElement(reader);
                        break;

                    case "QueryPlan":
                        ReadQueryPlan(
                            reader,
                            ref rootOperator,
                            warnings,
                            missingIndexGroups,
                            parameters,
                            ref memoryGrant);
                        break;
                }
            }
        }

        return new PlanStatement(
            statementId,
            statementType,
            statementText,
            subTreeCost,
            estRows,
            optmLevel,
            earlyAbortReason,
            ceModelVersion,
            queryHash,
            queryPlanHash,
            isDispatcher,
            rootOperator,
            warnings,
            missingIndexGroups,
            parameters,
            memoryGrant);
    }

    private void ReadQueryPlan(
        XmlReader reader,
        ref PlanOperatorNode? rootOperator,
        List<PlanWarning> warnings,
        List<MissingIndexGroup> missingIndexGroups,
        List<PlanParameter> parameters,
        ref MemoryGrantSummary? memoryGrant)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var queryPlanDepth = reader.Depth;

        while (reader.Read())
        {
            EnsureDepthWithinLimit(reader.Depth);

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == queryPlanDepth)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "Warnings":
                    ReadWarningsBlock(reader, warnings);
                    break;

                case "MissingIndexes":
                    ReadMissingIndexesBlock(reader, missingIndexGroups);
                    break;

                case "MemoryGrantInfo":
                    memoryGrant = ReadMemoryGrantInfo(reader);
                    break;

                case "ParameterList":
                    ReadParameterListBlock(reader, parameters);
                    break;

                case "RelOp":
                    rootOperator ??= ReadRelOpIterative(reader);
                    break;

                default:
                    // Ignora elementos desconocidos o de ejecución (RunTimeInformation, WaitStats, etc.) a nivel QueryPlan (RNF-COM-02).
                    SkipCurrentElement(reader);
                    break;
            }
        }
    }

    /// <summary>
    /// Construye el árbol de <c>RelOp</c> de manera iterativa usando una pila explícita en el heap,
    /// evitando <see cref="StackOverflowException"/> en planes profundamente anidados (hallazgo F8).
    /// </summary>
    private PlanOperatorNode ReadRelOpIterative(XmlReader reader)
    {
        var stack = new Stack<RelOpBuilder>();
        var rootBuilder = CreateRelOpBuilder(reader);

        if (reader.IsEmptyElement)
        {
            return rootBuilder.Build();
        }

        stack.Push(rootBuilder);

        while (stack.Count > 0 && reader.Read())
        {
            EnsureDepthWithinLimit(reader.Depth);

            var current = stack.Peek();

            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Depth == current.ElementDepth &&
                    string.Equals(reader.LocalName, "RelOp", StringComparison.Ordinal))
                {
                    var completed = stack.Pop().Build();
                    if (stack.Count == 0)
                    {
                        return completed;
                    }

                    stack.Peek().Children.Add(completed);
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            // Si aparece un RelOp hijo en cualquier nivel dentro del RelOp actual, apilamos un nuevo nodo.
            if (string.Equals(reader.LocalName, "RelOp", StringComparison.Ordinal))
            {
                var childBuilder = CreateRelOpBuilder(reader);
                if (reader.IsEmptyElement)
                {
                    current.Children.Add(childBuilder.Build());
                }
                else
                {
                    stack.Push(childBuilder);
                }

                continue;
            }

            // Elementos hijos directos del RelOp actual (OutputList, Warnings, MemoryFractions, RunTimeInformation, o el operador físico).
            if (reader.Depth == current.ElementDepth + 1)
            {
                switch (reader.LocalName)
                {
                    case "OutputList":
                        ReadColumnReferenceList(reader, current.OutputColumns);
                        break;

                    case "Warnings":
                        ReadWarningsBlock(reader, current.Warnings);
                        break;

                    case "RunTimeInformation":
                    case "MemoryFractions":
                    case "InternalInfo":
                        SkipCurrentElement(reader);
                        break;

                    default:
                        // Es el elemento específico del operador físico (IndexScan, TableScan, NestedLoops, Sort, Hash, etc.).
                        ReadPhysicalOperatorAttributes(reader, current);
                        break;
                }

                continue;
            }

            // Elementos internos del operador físico (antes de entrar a cualquier RelOp hijo).
            switch (reader.LocalName)
            {
                case "Object":
                    current.TargetObject ??= ReadObjectReference(reader);
                    break;

                case "SeekPredicates":
                    ReadSeekPredicatesBlock(reader, current.SeekColumns);
                    break;

                case "Predicate":
                    ReadPredicateBlock(reader, current);
                    break;

                case "Convert":
                    ReadConvertIfImplicit(reader, current, parentScalarString: null);
                    break;

                case "Const":
                    var constVal = reader.GetAttribute("ConstValue");
                    if (!string.IsNullOrEmpty(constVal))
                    {
                        current.ConstantLiterals.Add(constVal);
                    }

                    break;

                case "DefinedValues":
                case "OuterReferences":
                case "OrderBy":
                case "HashKeysBuild":
                case "HashKeysProbe":
                    // Estos bloques no contienen RelOp hijos; podemos recorrerlos para extraer Convert/Const si existieran.
                    break;
            }
        }

        while (stack.Count > 1)
        {
            var completed = stack.Pop().Build();
            stack.Peek().Children.Add(completed);
        }

        return stack.Pop().Build();
    }

    private static RelOpBuilder CreateRelOpBuilder(XmlReader reader)
    {
        var physicalOp = reader.GetAttribute("PhysicalOp") ?? string.Empty;
        var logicalOp = reader.GetAttribute("LogicalOp") ?? physicalOp;
        var isLookupByName =
            string.Equals(physicalOp, "Key Lookup", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(physicalOp, "RID Lookup", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(logicalOp, "Key Lookup", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(logicalOp, "RID Lookup", StringComparison.OrdinalIgnoreCase);

        return new RelOpBuilder
        {
            ElementDepth = reader.Depth,
            NodeId = ParseInt(reader.GetAttribute("NodeId")) ?? 0,
            PhysicalOp = physicalOp,
            LogicalOp = logicalOp,
            EstimateRows = ParseDouble(reader.GetAttribute("EstimateRows")) ?? 0d,
            EstimatedRowsRead = ParseDouble(reader.GetAttribute("EstimatedRowsRead")),
            TableCardinality = ParseDouble(reader.GetAttribute("TableCardinality")),
            EstimateIO = ParseDouble(reader.GetAttribute("EstimateIO")) ?? 0d,
            EstimateCPU = ParseDouble(reader.GetAttribute("EstimateCPU")) ?? 0d,
            AvgRowSize = ParseDouble(reader.GetAttribute("AvgRowSize")) ?? 0d,
            EstimatedTotalSubtreeCost = ParseDouble(reader.GetAttribute("EstimatedTotalSubtreeCost")) ?? 0d,
            EstimateRebinds = ParseDouble(reader.GetAttribute("EstimateRebinds")) ?? 0d,
            EstimateRewinds = ParseDouble(reader.GetAttribute("EstimateRewinds")) ?? 0d,
            IsParallel = ParseBool(reader.GetAttribute("Parallel")) ?? false,
            EstimatedExecutionMode = reader.GetAttribute("EstimatedExecutionMode"),
            IsLookup = isLookupByName,
        };
    }

    private static void ReadPhysicalOperatorAttributes(XmlReader reader, RelOpBuilder current)
    {
        var lookupAttr = ParseBool(reader.GetAttribute("Lookup"));
        if (lookupAttr == true)
        {
            current.IsLookup = true;
        }

        var orderedAttr = ParseBool(reader.GetAttribute("Ordered"));
        if (orderedAttr.HasValue)
        {
            current.IsOrdered = orderedAttr.Value;
        }
    }

    private void ReadPredicateBlock(XmlReader reader, RelOpBuilder current)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var predicateDepth = reader.Depth;
        string? currentScalarString = null;

        while (reader.Read())
        {
            EnsureDepthWithinLimit(reader.Depth);

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == predicateDepth)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "ScalarOperator":
                    var scalar = reader.GetAttribute("ScalarString");
                    if (!string.IsNullOrWhiteSpace(scalar))
                    {
                        currentScalarString = scalar;
                        current.PredicateScalarString ??= scalar;
                    }

                    break;

                case "Convert":
                    ReadConvertIfImplicit(reader, current, currentScalarString);
                    break;

                case "Const":
                    var constVal = reader.GetAttribute("ConstValue");
                    if (!string.IsNullOrEmpty(constVal))
                    {
                        current.ConstantLiterals.Add(constVal);
                    }

                    break;
            }
        }
    }

    private void ReadConvertIfImplicit(XmlReader reader, RelOpBuilder current, string? parentScalarString)
    {
        var isImplicit = ParseBool(reader.GetAttribute("Implicit")) ?? false;
        if (!isImplicit)
        {
            return;
        }

        var dataType = reader.GetAttribute("DataType") ?? string.Empty;
        var length = ParseInt(reader.GetAttribute("Length"));
        PlanColumnReference? convertedColumn = null;

        if (!reader.IsEmptyElement)
        {
            var convertDepth = reader.Depth;
            while (reader.Read())
            {
                EnsureDepthWithinLimit(reader.Depth);

                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == convertDepth)
                {
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element &&
                    string.Equals(reader.LocalName, "ColumnReference", StringComparison.Ordinal) &&
                    convertedColumn is null)
                {
                    convertedColumn = ReadColumnReference(reader);
                }
                else if (reader.NodeType == XmlNodeType.Element &&
                         string.Equals(reader.LocalName, "Const", StringComparison.Ordinal))
                {
                    var constVal = reader.GetAttribute("ConstValue");
                    if (!string.IsNullOrEmpty(constVal))
                    {
                        current.ConstantLiterals.Add(constVal);
                    }
                }
            }
        }

        current.ImplicitConversions.Add(
            new ImplicitConversionInfo(dataType, length, parentScalarString, convertedColumn));
    }

    private void ReadSeekPredicatesBlock(XmlReader reader, List<PlanColumnReference> seekColumns)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var seekDepth = reader.Depth;
        var insideRangeColumns = false;
        var rangeColumnsDepth = -1;

        while (reader.Read())
        {
            EnsureDepthWithinLimit(reader.Depth);

            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (insideRangeColumns && reader.Depth == rangeColumnsDepth)
                {
                    insideRangeColumns = false;
                }

                if (reader.Depth == seekDepth)
                {
                    break;
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (string.Equals(reader.LocalName, "RangeColumns", StringComparison.Ordinal) && !reader.IsEmptyElement)
            {
                insideRangeColumns = true;
                rangeColumnsDepth = reader.Depth;
            }
            else if (insideRangeColumns && string.Equals(reader.LocalName, "ColumnReference", StringComparison.Ordinal))
            {
                var col = ReadColumnReference(reader);
                if (!string.IsNullOrEmpty(col.Column))
                {
                    seekColumns.Add(col);
                }
            }
        }
    }

    private void ReadColumnReferenceList(XmlReader reader, List<PlanColumnReference> target)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var listDepth = reader.Depth;
        while (reader.Read())
        {
            EnsureDepthWithinLimit(reader.Depth);

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == listDepth)
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element &&
                string.Equals(reader.LocalName, "ColumnReference", StringComparison.Ordinal))
            {
                var col = ReadColumnReference(reader);
                if (!string.IsNullOrEmpty(col.Column))
                {
                    target.Add(col);
                }
            }
        }
    }

    private void ReadWarningsBlock(XmlReader reader, List<PlanWarning> warnings)
    {
        if (reader.GetAttribute("NoJoinPredicate") is { } noJoin && ParseBool(noJoin) == true)
        {
            warnings.Add(new PlanWarning(PlanWarningKind.NoJoinPredicate));
        }

        if (reader.IsEmptyElement)
        {
            return;
        }

        var warningsDepth = reader.Depth;
        while (reader.Read())
        {
            EnsureDepthWithinLimit(reader.Depth);

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == warningsDepth)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "PlanAffectingConvert":
                    warnings.Add(new PlanWarning(
                        PlanWarningKind.PlanAffectingConvert,
                        ConvertIssue: reader.GetAttribute("ConvertIssue"),
                        Expression: reader.GetAttribute("Expression")));
                    SkipCurrentElement(reader);
                    break;

                case "SpillToTempDb":
                    warnings.Add(new PlanWarning(
                        PlanWarningKind.SpillToTempDb,
                        SpillLevel: ParseInt(reader.GetAttribute("SpillLevel")),
                        SpilledThreadCount: ParseInt(reader.GetAttribute("SpilledThreadCount"))));
                    SkipCurrentElement(reader);
                    break;

                case "SortSpillDetails":
                    warnings.Add(new PlanWarning(
                        PlanWarningKind.SortSpillDetails,
                        Details: $"GrantedMemoryKb={reader.GetAttribute("GrantedMemoryKb")},UsedMemoryKb={reader.GetAttribute("UsedMemoryKb")},WritesToTempDb={reader.GetAttribute("WritesToTempDb")},ReadsFromTempDb={reader.GetAttribute("ReadsFromTempDb")}"));
                    SkipCurrentElement(reader);
                    break;

                case "HashSpillDetails":
                    warnings.Add(new PlanWarning(
                        PlanWarningKind.HashSpillDetails,
                        Details: $"GrantedMemoryKb={reader.GetAttribute("GrantedMemoryKb")},UsedMemoryKb={reader.GetAttribute("UsedMemoryKb")},WritesToTempDb={reader.GetAttribute("WritesToTempDb")},ReadsFromTempDb={reader.GetAttribute("ReadsFromTempDb")}"));
                    SkipCurrentElement(reader);
                    break;

                case "ColumnsWithNoStatistics":
                    warnings.Add(new PlanWarning(PlanWarningKind.ColumnsWithNoStatistics));
                    SkipCurrentElement(reader);
                    break;

                case "MemoryGrantWarning":
                    warnings.Add(new PlanWarning(
                        PlanWarningKind.MemoryGrantWarning,
                        Details: reader.GetAttribute("GrantWarningKind")));
                    SkipCurrentElement(reader);
                    break;

                default:
                    warnings.Add(new PlanWarning(PlanWarningKind.Other, Details: reader.LocalName));
                    SkipCurrentElement(reader);
                    break;
            }
        }
    }

    private void ReadMissingIndexesBlock(XmlReader reader, List<MissingIndexGroup> groups)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var blockDepth = reader.Depth;
        while (reader.Read())
        {
            EnsureDepthWithinLimit(reader.Depth);

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == blockDepth)
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element &&
                string.Equals(reader.LocalName, "MissingIndexGroup", StringComparison.Ordinal))
            {
                groups.Add(ReadMissingIndexGroup(reader));
            }
        }
    }

    private MissingIndexGroup ReadMissingIndexGroup(XmlReader reader)
    {
        var impact = ParseDouble(reader.GetAttribute("Impact")) ?? 0d;
        var suggestions = new List<MissingIndexSuggestion>();

        if (!reader.IsEmptyElement)
        {
            var groupDepth = reader.Depth;
            while (reader.Read())
            {
                EnsureDepthWithinLimit(reader.Depth);

                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == groupDepth)
                {
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element &&
                    string.Equals(reader.LocalName, "MissingIndex", StringComparison.Ordinal))
                {
                    suggestions.Add(ReadMissingIndex(reader));
                }
            }
        }

        return new MissingIndexGroup(impact, suggestions);
    }

    private MissingIndexSuggestion ReadMissingIndex(XmlReader reader)
    {
        var database = TrimBrackets(reader.GetAttribute("Database"));
        var schema = TrimBrackets(reader.GetAttribute("Schema"));
        var table = TrimBrackets(reader.GetAttribute("Table")) ?? string.Empty;

        var equality = new List<string>();
        var inequality = new List<string>();
        var include = new List<string>();

        if (!reader.IsEmptyElement)
        {
            var indexDepth = reader.Depth;
            string? currentUsage = null;

            while (reader.Read())
            {
                EnsureDepthWithinLimit(reader.Depth);

                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == indexDepth)
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (string.Equals(reader.LocalName, "ColumnGroup", StringComparison.Ordinal))
                {
                    currentUsage = reader.GetAttribute("Usage");
                }
                else if (string.Equals(reader.LocalName, "Column", StringComparison.Ordinal))
                {
                    var colName = TrimBrackets(reader.GetAttribute("Name"));
                    if (!string.IsNullOrEmpty(colName))
                    {
                        switch (currentUsage?.ToUpperInvariant())
                        {
                            case "EQUALITY":
                                equality.Add(colName);
                                break;
                            case "INEQUALITY":
                                inequality.Add(colName);
                                break;
                            case "INCLUDE":
                                include.Add(colName);
                                break;
                        }
                    }
                }
            }
        }

        return new MissingIndexSuggestion(database, schema, table, equality, inequality, include);
    }

    private void ReadParameterListBlock(XmlReader reader, List<PlanParameter> parameters)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var listDepth = reader.Depth;
        while (reader.Read())
        {
            EnsureDepthWithinLimit(reader.Depth);

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == listDepth)
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element &&
                string.Equals(reader.LocalName, "ColumnReference", StringComparison.Ordinal))
            {
                var name = reader.GetAttribute("Column");
                if (!string.IsNullOrEmpty(name))
                {
                    parameters.Add(new PlanParameter(
                        name,
                        reader.GetAttribute("ParameterDataType"),
                        reader.GetAttribute("ParameterCompiledValue")));
                }
            }
        }
    }

    private void SkipCurrentElement(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var startDepth = reader.Depth;
        while (reader.Read())
        {
            EnsureDepthWithinLimit(reader.Depth);

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == startDepth)
            {
                break;
            }
        }
    }

    private MemoryGrantSummary ReadMemoryGrantInfo(XmlReader reader)
    {
        var summary = new MemoryGrantSummary(
            ParseLong(reader.GetAttribute("SerialRequiredMemory")),
            ParseLong(reader.GetAttribute("SerialDesiredMemory")),
            ParseLong(reader.GetAttribute("GrantedMemory")),
            ParseLong(reader.GetAttribute("MaxUsedMemory")));

        SkipCurrentElement(reader);
        return summary;
    }

    private static PlanObjectReference ReadObjectReference(XmlReader reader) =>
        new(
            TrimBrackets(reader.GetAttribute("Database")),
            TrimBrackets(reader.GetAttribute("Schema")),
            TrimBrackets(reader.GetAttribute("Table")),
            TrimBrackets(reader.GetAttribute("Index")),
            TrimBrackets(reader.GetAttribute("Alias")),
            reader.GetAttribute("IndexKind"),
            reader.GetAttribute("Storage"));

    private static PlanColumnReference ReadColumnReference(XmlReader reader) =>
        new(
            TrimBrackets(reader.GetAttribute("Database")),
            TrimBrackets(reader.GetAttribute("Schema")),
            TrimBrackets(reader.GetAttribute("Table")),
            TrimBrackets(reader.GetAttribute("Alias")),
            TrimBrackets(reader.GetAttribute("Column")) ?? string.Empty);

    private void EnsureDepthWithinLimit(int depth)
    {
        if (depth > _options.MaxElementDepth)
        {
            throw new DepthLimitExceededException(
                $"La profundidad del documento XML ({depth}) supera el límite máximo configurado ({_options.MaxElementDepth}).");
        }
    }

    private static PlanParseFailureReason ClassifyXmlException(XmlException ex)
    {
        var msg = ex.Message;
        if (msg.Contains("DTD", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("DOCTYPE", StringComparison.OrdinalIgnoreCase))
        {
            return PlanParseFailureReason.DtdProhibited;
        }

        if (msg.Contains("MaxCharactersInDocument", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("exceeded the maximum", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("excedió", StringComparison.OrdinalIgnoreCase))
        {
            return PlanParseFailureReason.SizeLimitExceeded;
        }

        return PlanParseFailureReason.MalformedXml;
    }

    private static string? TrimBrackets(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var trimmed = identifier.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool? ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value, "1", StringComparison.Ordinal) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "0", StringComparison.Ordinal) ||
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private sealed class DepthLimitExceededException(string message) : Exception(message);

    private sealed class RelOpBuilder
    {
        public required int ElementDepth { get; init; }
        public required int NodeId { get; init; }
        public required string PhysicalOp { get; init; }
        public required string LogicalOp { get; init; }
        public required double EstimateRows { get; init; }
        public double? EstimatedRowsRead { get; init; }
        public double? TableCardinality { get; init; }
        public required double EstimateIO { get; init; }
        public required double EstimateCPU { get; init; }
        public required double AvgRowSize { get; init; }
        public required double EstimatedTotalSubtreeCost { get; init; }
        public required double EstimateRebinds { get; init; }
        public required double EstimateRewinds { get; init; }
        public required bool IsParallel { get; init; }
        public string? EstimatedExecutionMode { get; init; }
        public bool IsLookup { get; set; }
        public bool? IsOrdered { get; set; }
        public PlanObjectReference? TargetObject { get; set; }
        public List<PlanColumnReference> OutputColumns { get; } = [];
        public List<PlanColumnReference> SeekColumns { get; } = [];
        public string? PredicateScalarString { get; set; }
        public List<ImplicitConversionInfo> ImplicitConversions { get; } = [];
        public List<string> ConstantLiterals { get; } = [];
        public List<PlanWarning> Warnings { get; } = [];
        public List<PlanOperatorNode> Children { get; } = [];

        public PlanOperatorNode Build() =>
            new(
                NodeId,
                PhysicalOp,
                LogicalOp,
                EstimateRows,
                EstimatedRowsRead,
                TableCardinality,
                EstimateIO,
                EstimateCPU,
                AvgRowSize,
                EstimatedTotalSubtreeCost,
                EstimateRebinds,
                EstimateRewinds,
                IsParallel,
                EstimatedExecutionMode,
                IsLookup,
                IsOrdered,
                TargetObject,
                OutputColumns,
                SeekColumns,
                PredicateScalarString,
                ImplicitConversions,
                ConstantLiterals,
                Warnings,
                Children);
    }
}
