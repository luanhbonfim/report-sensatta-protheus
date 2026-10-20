using ClosedXML.Excel;
using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;

namespace SensattaReports.ReportBuilder.Rendering;

/// <summary>
/// Exporta o resultado do SQL como planilha .xlsx (dado cru, sem o layout
/// do .rtm): primeira linha com os nomes das colunas, demais com os dados.
/// </summary>
public sealed class ExcelReportExporter : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Xlsx;

    public ReportOutput Render(ReportDefinition definition, ReportDataSet data)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName(definition.Nome));

        // cabecalho
        for (var c = 0; c < data.Columns.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = data.Columns[c];
            cell.Style.Font.Bold = true;
        }

        // linhas
        for (var r = 0; r < data.Rows.Count; r++)
        {
            var row = data.Rows[r];
            for (var c = 0; c < data.Columns.Count; c++)
                SetCell(ws.Cell(r + 2, c + 1), c < row.Length ? row[c] : null);
        }

        if (data.Rows.Count > 0)
            ws.Range(1, 1, 1, data.Columns.Count).SetAutoFilter();
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return new ReportOutput
        {
            Content = ms.ToArray(),
            ContentType = ReportOutput.ContentTypeFor(ReportFormat.Xlsx),
            FileName = definition.NomeArquivo + ReportOutput.ExtensionFor(ReportFormat.Xlsx)
        };
    }

    private static void SetCell(IXLCell cell, object? v)
    {
        switch (v)
        {
            case null:
                break;
            case DateTime dt:
                cell.Value = dt;
                cell.Style.DateFormat.Format = dt.TimeOfDay == TimeSpan.Zero
                    ? "dd/mm/yyyy"
                    : "dd/mm/yyyy hh:mm:ss";
                break;
            case bool b:
                cell.Value = b;
                break;
            case decimal dec:
                cell.Value = dec;
                break;
            case double dbl:
                cell.Value = dbl;
                break;
            case float fl:
                cell.Value = fl;
                break;
            case short or int or long or byte or sbyte or ushort or uint or ulong:
                cell.Value = Convert.ToDouble(v);
                break;
            default:
                cell.Value = v.ToString();
                break;
        }
    }

    /// <summary>Nome de aba valido no Excel (max 31 chars, sem caracteres proibidos).</summary>
    private static string SheetName(string nome)
    {
        var limpo = new string(nome.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
        if (string.IsNullOrWhiteSpace(limpo)) limpo = "Report";
        return limpo.Length > 31 ? limpo[..31] : limpo;
    }
}
