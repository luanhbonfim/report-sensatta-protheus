namespace SensattaReports.Core;

/// <summary>Report nao encontrado no repositorio (codigo invalido).</summary>
public sealed class ReportNotFoundException(string codigo)
    : Exception($"Report '{codigo}' nao encontrado.")
{
    public string Codigo { get; } = codigo;
}

/// <summary>Parametro obrigatorio ausente ou com valor invalido.</summary>
public sealed class ReportParameterException(string message) : Exception(message);
