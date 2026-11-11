using System.Text;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>Convierte nombres PascalCase a snake_case para las columnas (ConnectionId -> connection_id).</summary>
internal static class SnakeCase
{
    public static string Convert(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (char.IsUpper(current))
            {
                var previousIsLower = i > 0 && char.IsLower(name[i - 1]);
                var endsAcronym = i > 0 && char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);

                if (previousIsLower || endsAcronym)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
