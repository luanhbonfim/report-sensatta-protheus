namespace SensattaReports.Core;

/// <summary>Ajustes do ReportService.</summary>
public sealed class ReportServiceOptions
{
    /// <summary>
    /// Maximo de relatorios pesados (consulta + render) rodando ao mesmo tempo.
    /// Protege CPU/memoria; o excedente aguarda a vez. Minimo 1.
    /// </summary>
    public int MaxConcurrent { get; init; } = 3;
}
