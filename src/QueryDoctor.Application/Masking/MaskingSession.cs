using System.Globalization;

namespace QueryDoctor.Application.Masking;

/// <summary>
/// Estado de una sola llamada a <see cref="LiteralMasker.Mask"/>: numeración de marcadores y contadores.
/// Vive solo durante la llamada, así que el enmascarador sigue siendo una función pura.
/// </summary>
internal sealed class MaskingSession
{
    private readonly Dictionary<(LiteralKind Kind, string Text), string> _markers = [];
    private readonly int[] _literalCounts = new int[Enum.GetValues<LiteralKind>().Length];
    private int _commentsRemoved;

    /// <summary>
    /// Devuelve el marcador del literal. El mismo tipo y el mismo texto exacto reciben el mismo marcador
    /// dentro de la solicitud (D3): se revela igualdad, nunca el valor.
    /// </summary>
    public string MarkerFor(LiteralKind kind, string tokenText)
    {
        _literalCounts[(int)kind]++;
        var key = (kind, tokenText);
        if (!_markers.TryGetValue(key, out var marker))
        {
            marker = string.Create(
                CultureInfo.InvariantCulture,
                $"@qd_{Prefix(kind)}_{_markers.Count + 1}");
            _markers[key] = marker;
        }

        return marker;
    }

    public void CountCommentRemoved() => _commentsRemoved++;

    public MaskingMetrics ToMetrics() => new(
        _literalCounts[(int)LiteralKind.Text],
        _literalCounts[(int)LiteralKind.Number],
        _literalCounts[(int)LiteralKind.Binary],
        _literalCounts[(int)LiteralKind.Temporal],
        _literalCounts[(int)LiteralKind.UniqueId],
        _commentsRemoved,
        _markers.Count);

    /// <summary>
    /// Instantánea para deshacer el efecto de un componente que terminó en fallo cerrado,
    /// de modo que sus marcadores no consuman numeración del resto.
    /// </summary>
    public Checkpoint Save() => new(new Dictionary<(LiteralKind, string), string>(_markers), (int[])_literalCounts.Clone(), _commentsRemoved);

    public void Restore(Checkpoint checkpoint)
    {
        _markers.Clear();
        foreach (var pair in checkpoint.Markers)
        {
            _markers[pair.Key] = pair.Value;
        }

        Array.Copy(checkpoint.LiteralCounts, _literalCounts, _literalCounts.Length);
        _commentsRemoved = checkpoint.CommentsRemoved;
    }

    private static string Prefix(LiteralKind kind) => kind switch
    {
        LiteralKind.Text => "str",
        LiteralKind.Number => "num",
        LiteralKind.Binary => "bin",
        LiteralKind.Temporal => "date",
        LiteralKind.UniqueId => "guid",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal sealed record Checkpoint(
        IReadOnlyDictionary<(LiteralKind, string), string> Markers,
        int[] LiteralCounts,
        int CommentsRemoved);
}
