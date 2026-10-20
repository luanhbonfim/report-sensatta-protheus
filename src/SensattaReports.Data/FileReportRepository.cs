using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;

namespace SensattaReports.Data;

/// <summary>
/// Repositorio de DESENVOLVIMENTO: le a definicao de um report dos arquivos
/// numa subpasta por report, sem precisar de Oracle. Para o codigo "x" procura:
///   reports/x/report.sql          (o SELECT)
///   reports/x/report.rtm          (o template)
///   reports/x/report.params.json  (opcional, os parametros)
/// </summary>
public sealed class FileReportRepository(string baseDir) : IReportRepository
{
    public async Task<ReportDefinition?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        var dir = Path.Combine(baseDir, codigo);
        var sqlPath = Path.Combine(dir, "report.sql");
        var rtmPath = Path.Combine(dir, "report.rtm");
        if (!File.Exists(sqlPath) || !File.Exists(rtmPath))
            return null;

        var paramsPath = Path.Combine(dir, "report.params.json");
        var paramsJson = File.Exists(paramsPath) ? await File.ReadAllTextAsync(paramsPath, ct) : null;

        return new ReportDefinition
        {
            Codigo = codigo,
            Nome = codigo,
            Sql = await File.ReadAllTextAsync(sqlPath, ct),
            TemplateRtm = await File.ReadAllTextAsync(rtmPath, ct),
            Parametros = ReportParametersJson.Parse(paramsJson)
        };
    }
}
