using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;

namespace SensattaReports.Data;

/// <summary>
/// Repositorio de DESENVOLVIMENTO: le a definicao de um report de arquivos
/// numa pasta, sem precisar de Oracle. Para o codigo "x" procura:
///   x.sql          (o SELECT)
///   x.rtm          (o template)
///   x.params.json  (opcional, os parametros)
/// </summary>
public sealed class FileReportRepository(string baseDir) : IReportRepository
{
    public async Task<ReportDefinition?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        var sqlPath = Path.Combine(baseDir, codigo + ".sql");
        var rtmPath = Path.Combine(baseDir, codigo + ".rtm");
        if (!File.Exists(sqlPath) || !File.Exists(rtmPath))
            return null;

        var paramsPath = Path.Combine(baseDir, codigo + ".params.json");
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
