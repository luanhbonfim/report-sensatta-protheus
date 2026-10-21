using System.Text.Json;
using SensattaReports.Core.Models;

namespace SensattaReports.Data;

/// <summary>
/// Le/grava report.layout.json — os ajustes de layout feitos no editor visual.
/// Formato: um objeto com uma entrada por campo (UserName do .rtm):
///
/// {
///   "DBText26": { "leftMm": 37.3, "topMm": 4.7, "widthMm": 7.6, "heightMm": 3.7 },
///   "Label11":  { "hidden": true }
/// }
///
/// Cada medida e opcional (null = mantem o valor do .rtm).
/// </summary>
public static class LayoutOverridesJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private sealed class Dto
    {
        public double? LeftMm { get; set; }
        public double? TopMm { get; set; }
        public double? WidthMm { get; set; }
        public double? HeightMm { get; set; }
        public bool Hidden { get; set; }
    }

    public static IReadOnlyDictionary<string, ElementOverride> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, ElementOverride>();

        var dtos = JsonSerializer.Deserialize<Dictionary<string, Dto>>(json, Options)
                   ?? new Dictionary<string, Dto>();

        var result = new Dictionary<string, ElementOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, d) in dtos)
            result[name] = new ElementOverride
            {
                LeftMm = d.LeftMm,
                TopMm = d.TopMm,
                WidthMm = d.WidthMm,
                HeightMm = d.HeightMm,
                Hidden = d.Hidden
            };
        return result;
    }

    public static string Serialize(IReadOnlyDictionary<string, ElementOverride> overrides)
    {
        var dtos = overrides.ToDictionary(
            kv => kv.Key,
            kv => new Dto
            {
                LeftMm = kv.Value.LeftMm,
                TopMm = kv.Value.TopMm,
                WidthMm = kv.Value.WidthMm,
                HeightMm = kv.Value.HeightMm,
                Hidden = kv.Value.Hidden
            });
        return JsonSerializer.Serialize(dtos, Options);
    }
}
