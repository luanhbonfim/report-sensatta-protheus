namespace SensattaReports.Core.Models;

/// <summary>Formato de saida pedido pelo chamador.</summary>
public enum ReportFormat
{
    Pdf,
    Xlsx
}

/// <summary>
/// Pedido de geracao de um report: qual report, em que formato e com
/// quais valores de parametro (vindos do Protheus).
/// </summary>
public sealed class ReportRequest
{
    public required string Codigo { get; init; }
    public ReportFormat Format { get; init; } = ReportFormat.Pdf;

    /// <summary>Valores crus dos parametros, por nome (ex.: "data_ini" -> "2026-01-01").</summary>
    public IReadOnlyDictionary<string, string?> Parametros { get; init; }
        = new Dictionary<string, string?>();
}

/// <summary>Resultado final: os bytes do arquivo + metadados para a resposta HTTP.</summary>
public sealed class ReportOutput
{
    public required byte[] Content { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }

    public static string ContentTypeFor(ReportFormat f) => f switch
    {
        ReportFormat.Pdf => "application/pdf",
        ReportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };

    public static string ExtensionFor(ReportFormat f) => f switch
    {
        ReportFormat.Pdf => ".pdf",
        ReportFormat.Xlsx => ".xlsx",
        _ => ".bin"
    };
}
