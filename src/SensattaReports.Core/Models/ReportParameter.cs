namespace SensattaReports.Core.Models;

/// <summary>
/// Tipos de parametro aceitos por um report. Usado para validar e converter
/// o que o Protheus envia antes de jogar no bind do Oracle.
/// </summary>
public enum ReportParameterType
{
    String,
    Integer,
    Decimal,
    Date,
    DateTime
}

/// <summary>
/// Define um parametro variavel do report (ex.: :data_ini, :data_fim).
/// Fica cadastrado junto com a definicao do report.
/// </summary>
public sealed class ReportParameter
{
    /// <summary>Nome do bind no SQL, sem os dois-pontos. Ex.: "data_ini".</summary>
    public required string Name { get; init; }

    /// <summary>Tipo esperado, para validacao/conversao.</summary>
    public ReportParameterType Type { get; init; } = ReportParameterType.String;

    /// <summary>Se o parametro e obrigatorio.</summary>
    public bool Required { get; init; } = true;

    /// <summary>Rotulo amigavel para o painel (fase 2).</summary>
    public string? Label { get; init; }
}
