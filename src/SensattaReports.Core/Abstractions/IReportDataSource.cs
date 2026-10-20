using SensattaReports.Core.Models;

namespace SensattaReports.Core.Abstractions;

/// <summary>
/// Executa o SQL do report (no Oracle do Sensatta) e devolve o resultado
/// tabular. Os parametros ja chegam convertidos para o tipo certo.
/// </summary>
public interface IReportDataSource
{
    Task<ReportDataSet> ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parametros,
        CancellationToken ct = default);
}
