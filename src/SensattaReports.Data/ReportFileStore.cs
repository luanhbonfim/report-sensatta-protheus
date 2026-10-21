using System.Text.Json;
using SensattaReports.Core.Models;

namespace SensattaReports.Data;

/// <summary>
/// Grava/consulta os arquivos de um report na pasta do servidor, no formato
/// que o FileReportRepository le: reports/&lt;codigo&gt;/report.{sql,rtm,params.json}.
/// Guarda tambem report.meta.json com o nome amigavel (do catalogo).
/// </summary>
public sealed class ReportFileStore(string baseDir)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public bool Exists(string codigo) => File.Exists(Path.Combine(baseDir, codigo, "report.rtm"));

    /// <summary>Codigos ja importados (subpastas com report.rtm).</summary>
    public IReadOnlyList<string> ListImported()
        => Directory.Exists(baseDir)
            ? Directory.GetDirectories(baseDir)
                .Where(d => File.Exists(Path.Combine(d, "report.rtm")))
                .Select(d => Path.GetFileName(d)!)
                .Where(n => !n.StartsWith('_'))
                .OrderBy(n => n)
                .ToList()
            : [];

    /// <summary>Nome amigavel salvo no meta (ou null).</summary>
    public string? GetName(string codigo)
    {
        var path = Path.Combine(baseDir, codigo, "report.meta.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
        }
        catch { return null; }
    }

    public async Task SaveAsync(string codigo, string nome, string sql, string rtm,
        IReadOnlyList<ReportParameter> parametros, CancellationToken ct = default)
    {
        var dir = Path.Combine(baseDir, codigo);
        Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(Path.Combine(dir, "report.sql"), sql, ct);
        await File.WriteAllTextAsync(Path.Combine(dir, "report.rtm"), rtm, ct);

        var dto = parametros.Select(p => new
        {
            name = p.Name,
            type = p.Type.ToString(),
            required = p.Required,
            label = p.Label
        });
        await File.WriteAllTextAsync(
            Path.Combine(dir, "report.params.json"),
            JsonSerializer.Serialize(dto, JsonOpts), ct);

        await File.WriteAllTextAsync(
            Path.Combine(dir, "report.meta.json"),
            JsonSerializer.Serialize(new { name = nome }, JsonOpts), ct);
    }

    /// <summary>Le os ajustes de layout (report.layout.json) de um report.</summary>
    public IReadOnlyDictionary<string, ElementOverride> GetLayout(string codigo)
    {
        var path = Path.Combine(baseDir, codigo, "report.layout.json");
        if (!File.Exists(path)) return new Dictionary<string, ElementOverride>();
        try { return LayoutOverridesJson.Parse(File.ReadAllText(path)); }
        catch { return new Dictionary<string, ElementOverride>(); }
    }

    /// <summary>Grava os ajustes de layout (report.layout.json) de um report.</summary>
    public async Task SaveLayoutAsync(string codigo,
        IReadOnlyDictionary<string, ElementOverride> overrides, CancellationToken ct = default)
    {
        var dir = Path.Combine(baseDir, codigo);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(
            Path.Combine(dir, "report.layout.json"),
            LayoutOverridesJson.Serialize(overrides), ct);
    }
}
