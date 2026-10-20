using SensattaReports.Core.Models;

namespace SensattaReports.Core.Abstractions;

/// <summary>
/// De onde vem a definicao do report (a tabela REPORT, ou arquivos em dev).
/// </summary>
public interface IReportRepository
{
    /// <summary>Busca a definicao pelo codigo; null se nao existir.</summary>
    Task<ReportDefinition?> GetByCodigoAsync(string codigo, CancellationToken ct = default);
}
