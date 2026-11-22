namespace QueryDoctor.PerfTests;

/// <summary>Argumentos de línea de comandos: un comando y pares <c>--clave valor</c>. Nunca llevan contraseñas.</summary>
public sealed class PerfArgs
{
    private readonly Dictionary<string, string> _values;

    private PerfArgs(string command, Dictionary<string, string> values)
    {
        Command = command;
        _values = values;
    }

    public string Command { get; }

    /// <summary>Lee <c>comando --clave valor ...</c>. Lanza <see cref="ArgumentException"/> si falta un valor o se repite.</summary>
    public static PerfArgs Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Falta el comando (setup | ingest).");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Count; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal) || key.Length < 3)
            {
                throw new ArgumentException($"Se esperaba una opción --clave y llegó '{key}'.");
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Falta el valor de {key}.");
            }

            if (!values.TryAdd(key[2..], args[++i]))
            {
                throw new ArgumentException($"La opción {key} está repetida.");
            }
        }

        return new PerfArgs(args[0].ToLowerInvariant(), values);
    }

    public string Required(string name) =>
        _values.TryGetValue(name, out var value) ? value : throw new ArgumentException($"Falta la opción --{name}.");

    public string Optional(string name, string fallback) => _values.TryGetValue(name, out var value) ? value : fallback;

    public int OptionalInt(string name, int fallback, int min, int max)
    {
        if (!_values.TryGetValue(name, out var raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            || value < min || value > max)
        {
            throw new ArgumentException($"--{name} debe ser un entero entre {min} y {max}.");
        }

        return value;
    }

    /// <summary>Lista de enteros separados por comas (por ejemplo <c>--concurrency 1,5</c>), sin repetidos.</summary>
    public IReadOnlyList<int> OptionalIntList(string name, IReadOnlyList<int> fallback, int min, int max)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        if (!_values.TryGetValue(name, out var raw))
        {
            return fallback;
        }

        var values = new List<int>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
                || value < min || value > max || values.Contains(value))
            {
                throw new ArgumentException($"--{name} debe ser una lista de enteros distintos entre {min} y {max} (por ejemplo 1,5).");
            }

            values.Add(value);
        }

        return values.Count > 0 ? values : throw new ArgumentException($"--{name} está vacía.");
    }

    public Guid RequiredGuid(string name) =>
        Guid.TryParse(Required(name), out var id) ? id : throw new ArgumentException($"--{name} no es un GUID válido.");
}
