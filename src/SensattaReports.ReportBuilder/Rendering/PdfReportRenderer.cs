using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;
using SensattaReports.ReportBuilder.Model;

namespace SensattaReports.ReportBuilder.Rendering;

/// <summary>
/// Gera o PDF a partir do template .rtm + dados, reproduzindo o layout de
/// bandas do ReportBuilder: cabecalho no topo de cada pagina, cabecalho de
/// grupo na quebra, uma banda de detalhe por linha, com quebra de pagina.
/// </summary>
public sealed class PdfReportRenderer : IReportRenderer
{
    static PdfReportRenderer()
    {
        // registra o resolvedor de fontes uma unica vez
        GlobalFontSettings.FontResolver ??= new SimpleFontResolver();
    }

    private readonly byte[]? _logo;

    /// <summary>
    /// logoPng: imagem (PNG/JPEG) usada no lugar das imagens do .rtm — a logo
    /// generica, desenhada na posicao que cada template define. Se null, usa a
    /// imagem embutida no proprio .rtm.
    /// </summary>
    public PdfReportRenderer(byte[]? logoPng = null) => _logo = logoPng;

    public ReportFormat Format => ReportFormat.Pdf;

    public ReportOutput Render(ReportDefinition definition, ReportDataSet data)
    {
        var tpl = RbTemplateLoader.Load(definition.TemplateRtm);

        using var doc = new PdfDocument();
        doc.Info.Title = definition.Nome;

        var contentHeightMm = tpl.ContentHeightMm;
        XGraphics gfx = null!;
        var cursorY = 0.0;                 // mm a partir do topo da area util
        object?[]? currentGroupRow = null; // linha vigente, p/ reimprimir grupo na quebra de pagina

        PdfPage AddPage()
        {
            var p = doc.AddPage();
            p.Width = XUnit.FromMillimeter(tpl.PageWidthMm);
            p.Height = XUnit.FromMillimeter(tpl.PageHeightMm);
            return p;
        }

        void StartPage()
        {
            gfx?.Dispose();
            gfx = XGraphics.FromPdfPage(AddPage());
            cursorY = 0;
            if (tpl.HeaderBand is { } h)
            {
                DrawBand(gfx, tpl, h, cursorY, null, data);
                cursorY += h.HeightMm;
            }
        }

        void EnsureSpace(double needMm)
        {
            if (cursorY + needMm <= contentHeightMm) return;
            StartPage();
            // reimprime os cabecalhos de grupo vigentes na nova pagina
            if (currentGroupRow is not null)
                foreach (var g in tpl.Groups)
                    if (g.Header is { } gh)
                    {
                        DrawBand(gfx, tpl, gh, cursorY, currentGroupRow, data);
                        cursorY += gh.HeightMm;
                    }
        }

        StartPage();

        var prevKeys = new string?[tpl.Groups.Count];
        var first = true;

        foreach (var row in data.Rows)
        {
            // valores atuais das chaves de grupo
            var keys = new string?[tpl.Groups.Count];
            for (var gi = 0; gi < tpl.Groups.Count; gi++)
                keys[gi] = data.GetString(row, tpl.Groups[gi].BreakField);

            // a partir de qual grupo houve quebra?
            var breakFrom = -1;
            if (first)
                breakFrom = tpl.Groups.Count > 0 ? 0 : -1;
            else
                for (var gi = 0; gi < tpl.Groups.Count; gi++)
                    if (!string.Equals(keys[gi], prevKeys[gi], StringComparison.Ordinal))
                    {
                        breakFrom = gi;
                        break;
                    }

            if (breakFrom >= 0)
            {
                for (var gi = breakFrom; gi < tpl.Groups.Count; gi++)
                    if (tpl.Groups[gi].Header is { } gh)
                    {
                        EnsureSpace(gh.HeightMm);
                        DrawBand(gfx, tpl, gh, cursorY, row, data);
                        cursorY += gh.HeightMm;
                    }
                currentGroupRow = row;
            }

            prevKeys = keys;
            first = false;

            if (tpl.DetailBand is { } d)
            {
                EnsureSpace(d.HeightMm);
                DrawBand(gfx, tpl, d, cursorY, row, data);
                cursorY += d.HeightMm;
            }
        }

        gfx?.Dispose();

        using var ms = new MemoryStream();
        doc.Save(ms);
        return new ReportOutput
        {
            Content = ms.ToArray(),
            ContentType = ReportOutput.ContentTypeFor(ReportFormat.Pdf),
            FileName = definition.NomeArquivo + ReportOutput.ExtensionFor(ReportFormat.Pdf)
        };
    }

    private void DrawBand(XGraphics gfx, RbTemplate tpl, RbBand band,
        double bandTopMm, object?[]? row, ReportDataSet data)
    {
        foreach (var el in band.Elements)
        {
            var xMm = tpl.MarginLeftMm + el.LeftMm;
            var yMm = tpl.MarginTopMm + bandTopMm + el.TopMm;
            DrawElement(gfx, el, xMm, yMm, row, data);
        }
    }

    private void DrawElement(XGraphics gfx, RbElement el,
        double xMm, double yMm, object?[]? row, ReportDataSet data)
    {
        var x = XUnit.FromMillimeter(xMm).Point;
        var y = XUnit.FromMillimeter(yMm).Point;
        var w = XUnit.FromMillimeter(el.WidthMm).Point;
        var h = XUnit.FromMillimeter(el.HeightMm).Point;
        var rect = new XRect(x, y, w, h);

        switch (el)
        {
            case RbShape shape:
                gfx.DrawRectangle(new XSolidBrush(ToXColor(shape.BrushColor)), rect);
                break;

            case RbLine line:
                var pen = new XPen(XColors.Black, Math.Max(0.25, line.WeightPt));
                switch (line.Position)
                {
                    case "lpLeft": gfx.DrawLine(pen, x, y, x, y + h); break;
                    case "lpRight": gfx.DrawLine(pen, x + w, y, x + w, y + h); break;
                    case "lpTop": gfx.DrawLine(pen, x, y, x + w, y); break;
                    case "lpBottom": gfx.DrawLine(pen, x, y + h, x + w, y + h); break;
                    default:
                        if (line.Horizontal) gfx.DrawLine(pen, x, y, x + w, y);     // horizontal no topo
                        else gfx.DrawLine(pen, x, y, x, y + h);                      // vertical a esquerda
                        break;
                }
                break;

            case RbLabel lbl:
                DrawText(gfx, lbl.Text, lbl.Font, lbl.Alignment, rect);
                break;

            case RbDbText db:
                var text = row is not null ? data.GetString(row, db.DataField) : string.Empty;
                DrawText(gfx, text, db.Font, db.Alignment, rect);
                break;

            case RbImage img:
                // prioriza a logo generica; se nao houver, usa a imagem do .rtm
                var imgBytes = _logo ?? (img.HasImage ? img.Jpeg : null);
                if (imgBytes is not null)
                {
                    try
                    {
                        // publiclyVisible: true permite que o PdfSharp acesse o buffer
                        // interno (GetBuffer) ao salvar — sem isso da
                        // UnauthorizedAccessException. O stream NAO e descartado aqui
                        // porque o PdfSharp o le de forma preguicosa no doc.Save.
                        var ms = new MemoryStream(imgBytes, 0, imgBytes.Length,
                            writable: false, publiclyVisible: true);
                        var image = XImage.FromStream(ms);
                        gfx.DrawImage(image, rect);
                    }
                    catch (Exception ex)
                    {
                        // nao derruba o report; registra o motivo no console para diagnostico
                        Console.Error.WriteLine(
                            $"[PdfReportRenderer] Falha ao desenhar imagem ({imgBytes.Length} bytes): {ex.GetType().Name}: {ex.Message}");
                    }
                }
                break;
        }
    }

    private static void DrawText(XGraphics gfx, string text, RbFont f, RbAlignment align, XRect rect)
    {
        if (string.IsNullOrEmpty(text)) return;

        var style = XFontStyleEx.Regular;
        if (f.Bold) style |= XFontStyleEx.Bold;
        if (f.Italic) style |= XFontStyleEx.Italic;
        if (f.Underline) style |= XFontStyleEx.Underline;

        var font = new XFont(f.Name, f.SizePt, style);
        var brush = new XSolidBrush(ToXColor(f.Color));
        var fmt = new XStringFormat
        {
            Alignment = align switch
            {
                RbAlignment.Right => XStringAlignment.Far,
                RbAlignment.Center => XStringAlignment.Center,
                _ => XStringAlignment.Near
            },
            LineAlignment = XLineAlignment.Center
        };

        gfx.DrawString(text, font, brush, rect, fmt);
    }

    private static XColor ToXColor(RbColor c) => XColor.FromArgb(c.R, c.G, c.B);
}
