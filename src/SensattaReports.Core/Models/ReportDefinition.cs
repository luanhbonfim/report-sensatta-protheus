namespace SensattaReports.Core.Models;

/// <summary>
/// A definicao de um report, como fica cadastrada na tabela REPORT.
/// Reune o SQL (de onde vem os dados) e o template .rtm (como desenhar).
/// </summary>
public sealed class ReportDefinition
{
    /// <summary>Codigo unico usado na URL. Ex.: "resumo-coletas-eua".</summary>
    public required string Codigo { get; init; }

    /// <summary>Nome amigavel. Ex.: "Resumo de Coletas - EUA".</summary>
    public required string Nome { get; init; }

    /// <summary>O SELECT, com binds nomeados (:data_ini, :data_fim).</summary>
    public required string Sql { get; init; }

    /// <summary>O conteudo do template .rtm (texto do ReportBuilder).</summary>
    public required string TemplateRtm { get; init; }

    /// <summary>Parametros que o report aceita.</summary>
    public IReadOnlyList<ReportParameter> Parametros { get; init; } = [];

    /// <summary>Nome-base do arquivo gerado (sem extensao).</summary>
    public string NomeArquivo => Codigo;
}
