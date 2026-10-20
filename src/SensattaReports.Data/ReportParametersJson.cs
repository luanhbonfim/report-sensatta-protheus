using System.Text.Json;
using System.Text.Json.Serialization;
using SensattaReports.Core.Models;

namespace SensattaReports.Data;

/// <summary>
/// Le a coluna PARAMETROS (JSON) da tabela REPORT, ou um arquivo .params.json,
/// e devolve a lista de ReportParameter.
///
/// Formato esperado:
/// [ { "name":"data_ini", "type":"Date", "required":true, "label":"Data inicial" } ]
/// </summary>
public static class ReportParametersJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class Dto
    {
        public string Name { get; set; } = string.Empty;
        public ReportParameterType Type { get; set; } = ReportParameterType.String;
        public bool Required { get; set; } = true;
        public string? Label { get; set; }
    }

    public static IReadOnlyList<ReportParameter> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        var dtos = JsonSerializer.Deserialize<List<Dto>>(json, Options) ?? [];
        return dtos.Select(d => new ReportParameter
        {
            Name = d.Name,
            Type = d.Type,
            Required = d.Required,
            Label = d.Label
        }).ToList();
    }
}
