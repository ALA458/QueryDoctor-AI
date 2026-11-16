using System.Text;
using System.Xml;
using QueryDoctor.Application.Engine.Parsing;

namespace QueryDoctor.Application.Masking;

/// <summary>
/// Copia del Showplan XML con los valores literales sustituidos por marcadores (RNF-SEG-05: "valores de
/// parámetros y predicados del plan"). Se lee con <see cref="XmlReader"/> con DTD prohibido, sin resolver y con
/// los mismos límites que <see cref="ShowplanXmlParser"/> (S1-12). Cualquier problema deja el plan entero no
/// enviable (D6): nunca se entrega una copia a medio enmascarar.
/// Denegación por defecto (auditoría H1): cada atributo es portador de literales (pasa por el lexer), conservado
/// (métrica, enumeración, identificador o hash) o desconocido; un atributo desconocido deja el plan no enviable.
/// </summary>
internal static class PlanXmlMasker
{
    /// <summary>
    /// Atributos del esquema Showplan que llevan texto T-SQL o valores literales. Se pasan por el mismo
    /// enmascarador léxico del SQL.
    /// </summary>
    private static readonly HashSet<string> LiteralCarrierAttributes = new(StringComparer.Ordinal)
    {
        "StatementText",
        "ParameterizedText",
        "ScalarString",
        "Expression",
        "ConstValue",
        "ParameterCompiledValue",
        "ParameterRuntimeValue",

        // SQL que se envía a un servidor vinculado (elemento RemoteQuery).
        "RemoteQuery",
    };

    /// <summary>
    /// Atributos que se conservan tal cual: métricas y estimaciones, enumeraciones, opciones SET, identificadores
    /// de objetos (DA-13 abierta) y hashes. Salen del inventario de los 16 planes del laboratorio (S1-07), más unos
    /// pocos del esquema showplanxml.xsd que tampoco llevan literales. Agregar uno exige confirmar que no puede
    /// contener un valor de la consulta.
    /// </summary>
    private static readonly HashSet<string> PreservedAttributes = new(StringComparer.Ordinal)
    {
        // Raíz y sentencia.
        "Version", "Build", "StatementId", "StatementCompId", "StatementType", "StatementSubTreeCost",
        "StatementEstRows", "StatementOptmLevel", "StatementOptmEarlyAbortReason", "StatementParameterizationType",
        "StatementSqlHandle", "BatchSqlHandle", "QueryHash", "QueryPlanHash", "RetrievedFromCache", "ParentObjectId",
        "DatabaseContextSettingsId", "CardinalityEstimationModelVersion", "SecurityPolicyApplied", "QueryVariantID",

        // Opciones SET de la sentencia.
        "ANSI_NULLS", "ANSI_PADDING", "ANSI_WARNINGS", "ARITHABORT", "CONCAT_NULL_YIELDS_NULL",
        "NUMERIC_ROUNDABORT", "QUOTED_IDENTIFIER",

        // QueryPlan, memoria, hilos y tiempos.
        "CachedPlanSize", "CompileCPU", "CompileMemory", "CompileTime", "DegreeOfParallelism", "MemoryGrant",
        "NonParallelPlanReason", "DesiredMemory", "GrantWaitTime", "GrantedMemory", "LastRequestedMemory",
        "IsMemoryGrantFeedbackAdjusted", "MaxQueryMemory", "MaxUsedMemory", "RequestedMemory", "RequiredMemory",
        "SerialDesiredMemory", "SerialRequiredMemory", "GrantWarningKind", "Input", "Output",
        "EstimatedAvailableDegreeOfParallelism", "EstimatedAvailableMemoryGrant", "EstimatedPagesCached",
        "MaxCompileMemory", "Branches", "UsedThreads", "ReservedThreads", "CpuTime", "ElapsedTime",
        "WaitCount", "WaitTimeMs", "WaitType",

        // RelOp: estimaciones y propiedades del operador.
        "NodeId", "PhysicalOp", "LogicalOp", "Parallel", "EstimatedExecutionMode", "EstimateRows",
        "EstimateRowsWithoutRowGoal", "EstimatedRowsRead", "EstimateIO", "EstimateCPU", "EstimateRebinds",
        "EstimateRewinds", "AvgRowSize", "EstimatedTotalSubtreeCost", "TableCardinality", "IsAdaptive",
        "AdaptiveThresholdRows", "EstimatedJoinType", "ManyToMany", "Partitioned", "Optimized",
        "WithOrderedPrefetch", "WithUnorderedPrefetch", "Distinct", "Operation", "CompareOp", "ScanType",
        "AggType", "ComputeSequence", "DMLRequestSort", "FunctionName", "IsPercent", "PartitioningType",
        "PrimaryNodeId", "RowCount", "StartupExpression", "WithTies",

        // Contadores reales por hilo.
        "Thread", "Batches", "ActualRows", "ActualRowsRead", "ActualExecutions", "ActualExecutionMode",
        "ActualEndOfScans", "ActualElapsedms", "ActualCPUms", "ActualScans", "ActualLogicalReads",
        "ActualPhysicalReads", "ActualReadAheads", "ActualLobLogicalReads", "ActualLobPhysicalReads",
        "ActualLobReadAheads", "ActualRebinds", "ActualRewinds", "InputMemoryGrant", "OutputMemoryGrant",
        "UsedMemoryGrant",

        // Acceso a índices y objetos (identificadores, DA-13).
        "Database", "Schema", "Table", "Index", "Alias", "IndexKind", "Storage", "TableReferenceId", "Column",
        "ColumnId", "Name", "Usage", "Ascending", "Ordered", "ScanDirection", "ForcedIndex", "ForceScan",
        "ForceSeek", "NoExpandHint", "Lookup",

        // Conversiones, advertencias y derrames.
        "DataType", "Length", "Precision", "Scale", "Style", "Implicit", "ConvertIssue", "ParameterDataType",
        "NoJoinPredicate", "SpillLevel", "SpilledThreadCount", "GrantedMemoryKb", "UsedMemoryKb",
        "ReadsFromTempDb", "WritesToTempDb",

        // Estadísticas, índices faltantes y planes sensibles a parámetros.
        "Statistics", "SamplingPercent", "ModificationCount", "LastUpdate", "Impact", "LowBoundary", "HighBoundary",
    };

    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    private const string RootElement = "ShowPlanXML";

    public static bool TryMask(string planXml, MaskingSession session, out string? masked, out MaskingFailureCause? cause)
    {
        masked = null;
        cause = null;

        if (planXml.Length > PlanParserOptions.DefaultMaxCharactersInDocument)
        {
            cause = MaskingFailureCause.InputTooLarge;
            return false;
        }

        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = PlanParserOptions.DefaultMaxCharactersInDocument,
            MaxCharactersFromEntities = 1024,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        var writerSettings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.Entitize,
        };

        var output = new StringBuilder(planXml.Length);
        try
        {
            using var stringReader = new StringReader(planXml);
            using var reader = XmlReader.Create(stringReader, readerSettings);
            using (var writer = XmlWriter.Create(output, writerSettings))
            {
                if (reader.MoveToContent() != XmlNodeType.Element ||
                    !string.Equals(reader.LocalName, RootElement, StringComparison.Ordinal))
                {
                    cause = MaskingFailureCause.PlanUnexpectedRoot;
                    return false;
                }

                do
                {
                    if (reader.Depth > PlanParserOptions.DefaultMaxElementDepth)
                    {
                        cause = MaskingFailureCause.PlanTooDeep;
                        return false;
                    }

                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (!CopyElementStart(reader, writer, session, out cause))
                            {
                                return false;
                            }

                            break;

                        case XmlNodeType.EndElement:
                            writer.WriteFullEndElement();
                            break;

                        case XmlNodeType.Text:
                        case XmlNodeType.CDATA:
                            // Showplan no lleva texto en elementos: si aparece, no sabemos qué contiene.
                            cause = MaskingFailureCause.PlanUnexpectedLiteralCarrier;
                            return false;

                        default:
                            // Declaración XML, espacios, comentarios e instrucciones de proceso no se copian.
                            break;
                    }
                }
                while (reader.Read());
            }
        }
        catch (XmlException ex)
        {
            // El mensaje de XmlException repite parte del documento: solo se usa para clasificar y se descarta.
            cause = ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase)
                ? MaskingFailureCause.PlanDtdProhibited
                : MaskingFailureCause.PlanMalformed;
            return false;
        }

        masked = output.ToString();
        return true;
    }

    private static bool CopyElementStart(XmlReader reader, XmlWriter writer, MaskingSession session, out MaskingFailureCause? cause)
    {
        cause = null;
        var isEmpty = reader.IsEmptyElement;
        writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);

        if (reader.MoveToFirstAttribute())
        {
            do
            {
                var value = reader.Value;
                if (string.Equals(reader.NamespaceURI, XmlnsNamespace, StringComparison.Ordinal))
                {
                    // Declaración de espacio de nombres: se copia tal cual.
                }
                else if (LiteralCarrierAttributes.Contains(reader.LocalName))
                {
                    if (!SqlLexicalMasker.TryMask(value, session, out var maskedValue))
                    {
                        cause = MaskingFailureCause.PlanAttributeLexicalError;
                        return false;
                    }

                    value = maskedValue!;
                }
                else if (!PreservedAttributes.Contains(reader.LocalName))
                {
                    // Denegación por defecto (H1): no sabemos si este atributo puede llevar un valor de la consulta.
                    cause = MaskingFailureCause.PlanUnknownAttribute;
                    return false;
                }
                else if (value.Contains('\'', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal))
                {
                    // Red de seguridad: un atributo conservado no debería traer una cadena entre comillas.
                    cause = MaskingFailureCause.PlanUnexpectedLiteralCarrier;
                    return false;
                }

                writer.WriteAttributeString(reader.Prefix, reader.LocalName, reader.NamespaceURI, value);
            }
            while (reader.MoveToNextAttribute());

            reader.MoveToElement();
        }

        if (isEmpty)
        {
            writer.WriteEndElement();
        }

        return true;
    }
}
