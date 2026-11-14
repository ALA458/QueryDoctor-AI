using System.Text;
using System.Xml;
using QueryDoctor.Application.Engine.Parsing;

namespace QueryDoctor.Application.Masking;

/// <summary>
/// Copia del Showplan XML con los valores literales sustituidos por marcadores (RNF-SEG-05: "valores de
/// parámetros y predicados del plan"). Se lee con <see cref="XmlReader"/> con DTD prohibido, sin resolver y con
/// los mismos límites que <see cref="ShowplanXmlParser"/> (S1-12). Cualquier problema deja el plan entero no
/// enviable (D6): nunca se entrega una copia a medio enmascarar.
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
    };

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
                if (LiteralCarrierAttributes.Contains(reader.LocalName))
                {
                    if (!SqlLexicalMasker.TryMask(value, session, out var maskedValue))
                    {
                        cause = MaskingFailureCause.PlanAttributeLexicalError;
                        return false;
                    }

                    value = maskedValue!;
                }
                else if (value.Contains('\'', StringComparison.Ordinal))
                {
                    // Red de seguridad: un atributo no catalogado con apóstrofo puede llevar una cadena literal.
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
