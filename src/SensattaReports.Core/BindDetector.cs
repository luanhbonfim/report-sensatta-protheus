using System.Text.RegularExpressions;
using SensattaReports.Core.Models;

namespace SensattaReports.Core;

/// <summary>
/// Detecta os binds nomeados de um SQL (:data_ini, :filial, ...) para montar
/// a lista de parametros do report automaticamente.
/// </summary>
public static partial class BindDetector
{
    // : seguido de identificador, sem ser precedido por outro ':' (evita ::cast)
    [GeneratedRegex(@"(?<!:):(\w+)")]
    private static partial Regex BindRegex();

    public static IReadOnlyList<string> Detect(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (Match m in BindRegex().Matches(sql))
        {
            var name = m.Groups[1].Value;
            if (seen.Add(name)) result.Add(name);
        }
        return result;
    }

    /// <summary>Palpite de tipo pelo nome (o usuario confirma no painel).</summary>
    public static ReportParameterType GuessType(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("data") || n.Contains("date") || n.StartsWith("dt"))
            return ReportParameterType.Date;
        return ReportParameterType.String;
    }
}
