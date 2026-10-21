using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SensattaReports.Core;

/// <summary>
/// Gera um "codigo" seguro (minusculas, sem acento, com hifens) a partir do
/// nome do report. Vira o nome da subpasta e o trecho da URL — por isso so
/// aceita [a-z0-9-], evitando path traversal.
/// </summary>
public static partial class Slug
{
    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnumRegex();

    public static string From(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // remove acentos
        var norm = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in norm)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);

        var ascii = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        return NonAlnumRegex().Replace(ascii, "-").Trim('-');
    }
}
