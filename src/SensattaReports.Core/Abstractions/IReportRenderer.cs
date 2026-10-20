using SensattaReports.Core.Models;

namespace SensattaReports.Core.Abstractions;

/// <summary>
/// Transforma (definicao + dados) num arquivo. Cada formato (PDF, Excel)
/// tem sua implementacao; o ReportService escolhe pela propriedade Format.
/// </summary>
public interface IReportRenderer
{
    ReportFormat Format { get; }

    ReportOutput Render(ReportDefinition definition, ReportDataSet data);
}
