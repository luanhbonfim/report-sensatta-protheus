using SensattaReports.Core.Abstractions;

namespace SensattaReports.Data;

/// <summary>
/// Catalogo de DESENVOLVIMENTO (sem Oracle): lista os reports que ja existem
/// como arquivos na pasta, usando o proprio report.rtm como "template". Serve
/// para exercitar o painel em dev.
/// </summary>
public sealed class DemoReportCatalog(string baseDir) : IReportCatalog
{
    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> list = Directory.Exists(baseDir)
            ? Directory.GetDirectories(baseDir)
                .Where(d => File.Exists(Path.Combine(d, "report.rtm")))
                .Select(d => Path.GetFileName(d)!)
                .Where(n => !n.StartsWith('_'))
                .OrderBy(n => n)
                .ToList()
            : [];
        return Task.FromResult(list);
    }

    public Task<string?> GetTemplateAsync(string name, CancellationToken ct = default)
    {
        var path = Path.Combine(baseDir, name, "report.rtm");
        return Task.FromResult(File.Exists(path) ? File.ReadAllText(path) : null);
    }
}
