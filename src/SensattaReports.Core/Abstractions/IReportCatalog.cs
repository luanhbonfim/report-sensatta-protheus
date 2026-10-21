namespace SensattaReports.Core.Abstractions;

/// <summary>
/// Catalogo "mestre" de reports (a tabela SIGMA.REPORT do Sensatta): lista os
/// reports disponiveis e entrega o TEMPLATE (.rtm) de um deles. Usado apenas na
/// importacao/atualizacao pelo painel — nao no atendimento das requisicoes.
/// </summary>
public interface IReportCatalog
{
    /// <summary>Nomes dos reports disponiveis no catalogo.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);

    /// <summary>O texto do .rtm (coluna TEMPLATE) de um report; null se nao existir.</summary>
    Task<string?> GetTemplateAsync(string name, CancellationToken ct = default);
}
