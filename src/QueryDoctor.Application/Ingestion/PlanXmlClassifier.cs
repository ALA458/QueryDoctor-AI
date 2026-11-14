using System.Xml;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Ingestion;

/// <summary>Resultado de clasificar un plan al ingerirlo.</summary>
/// <param name="Status">Pending (lo analizará el motor) o NotAnalyzable.</param>
/// <param name="Cause">Causa cuando no es analizable.</param>
public sealed record PlanClassification(PlanAnalysisStatus Status, PlanNotAnalyzableCause? Cause)
{
    public static PlanClassification Pending { get; } = new(PlanAnalysisStatus.Pending, null);

    public static PlanClassification NotAnalyzable(PlanNotAnalyzableCause cause) => new(PlanAnalysisStatus.NotAnalyzable, cause);
}

/// <summary>
/// Decide si un plan recuperado se puede analizar (RF-ING-09, CA-HU03.3, hallazgo F8). No interpreta el plan (eso es
/// PlanParser, S1-12): solo comprueba que haya XML bien formado con raíz ShowPlanXML. Sin DTD ni resolución externa.
/// Nunca lanza por el contenido: un plan malo queda NotAnalyzable con su causa y la ingesta sigue.
/// </summary>
public static class PlanXmlClassifier
{
    /// <summary>Tope de caracteres (32 M): el plan de 140 niveles del laboratorio (ESC06) tiene unos 305 K.</summary>
    public const long DefaultMaxCharacters = 32L * 1024 * 1024;

    private const string ShowplanRoot = "ShowPlanXML";

    /// <param name="found">Query Store devolvió la fila del plan.</param>
    /// <param name="planXml">query_plan leído como nvarchar(max), nunca como xml (F8).</param>
    /// <param name="maxCharacters">Tope de tamaño.</param>
    public static PlanClassification Classify(bool found, string? planXml, long maxCharacters = DefaultMaxCharacters)
    {
        if (!found)
        {
            return PlanClassification.NotAnalyzable(PlanNotAnalyzableCause.PlanNotFound);
        }

        if (planXml is null)
        {
            return PlanClassification.NotAnalyzable(PlanNotAnalyzableCause.NullXml);
        }

        if (string.IsNullOrWhiteSpace(planXml))
        {
            return PlanClassification.NotAnalyzable(PlanNotAnalyzableCause.EmptyXml);
        }

        if (planXml.Length > maxCharacters)
        {
            return PlanClassification.NotAnalyzable(PlanNotAnalyzableCause.TooLarge);
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxCharacters,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };

        try
        {
            using var stringReader = new StringReader(planXml);
            using var reader = XmlReader.Create(stringReader, settings);

            string? root = null;
            while (reader.Read())
            {
                if (root is null && reader.NodeType == XmlNodeType.Element)
                {
                    root = reader.LocalName;
                }
            }

            return string.Equals(root, ShowplanRoot, StringComparison.Ordinal)
                ? PlanClassification.Pending
                : PlanClassification.NotAnalyzable(PlanNotAnalyzableCause.NotShowplan);
        }
        catch (XmlException)
        {
            // Truncado o inválido (RF-ING-09). La excepción no se registra: su mensaje cita fragmentos del plan.
            return PlanClassification.NotAnalyzable(PlanNotAnalyzableCause.MalformedXml);
        }
    }
}
