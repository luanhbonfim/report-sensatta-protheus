using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;
using SensattaReports.ReportBuilder.Model;

namespace SensattaReports.ReportBuilder.Rendering;

/// <summary>
/// Gera o PDF a partir do template .rtm + dados, reproduzindo o layout de
/// bandas do ReportBuilder: cabecalho no topo de cada pagina, cabecalhos de
/// grupo na quebra, uma banda de detalhe por linha, rodapes de grupo com
/// subtotais, memos multi-linha que fluem entre paginas, e rodape fixo no pe
/// da pagina com data/hora e numero de pagina.
/// </summary>
public sealed class PdfReportRenderer : IReportRenderer
{
    static PdfReportRenderer()
    {
        GlobalFontSettings.FontResolver ??= new SimpleFontResolver();
    }

    private readonly byte[]? _logo;

    /// <summary>
    /// logoPng: imagem (PNG/JPEG) usada no lugar das imagens do .rtm. Se null,
    /// usa a imagem embutida no proprio .rtm.
    /// </summary>
    public PdfReportRenderer(byte[]? logoPng = null) => _logo = logoPng;

    public ReportFormat Format => ReportFormat.Pdf;

    /// <summary>Numero da pagina atual / total — para as variaveis de sistema do rodape.</summary>
    private readonly record struct PageInfo(int Number, int Total);

    /// <summary>Posicao/tamanho efetivos de um elemento (mm) apos aplicar os ajustes do painel.</summary>
    private readonly record struct Box(double Left, double Top, double Width, double Height, bool Hidden);

    // ajustes de layout vigentes (por nome de campo), setados em Render
    private IReadOnlyDictionary<string, ElementOverride> _ovr = new Dictionary<string, ElementOverride>();

    /// <summary>Aplica o override (se houver) sobre a posicao/tamanho do elemento.</summary>
    private Box Eff(RbElement el)
    {
        if (el.Name.Length > 0 && _ovr.TryGetValue(el.Name, out var o))
            return new Box(
                o.LeftMm ?? el.LeftMm,
                o.TopMm ?? el.TopMm,
                o.WidthMm ?? el.WidthMm,
                o.HeightMm ?? el.HeightMm,
                o.Hidden);
        return new Box(el.LeftMm, el.TopMm, el.WidthMm, el.HeightMm, false);
    }

    public ReportOutput Render(ReportDefinition definition, ReportDataSet data)
    {
        _ovr = definition.LayoutOverrides ?? new Dictionary<string, ElementOverride>();
        var tpl = RbTemplateLoader.Load(definition.TemplateRtm);

        using var doc = new PdfDocument();
        doc.Info.Title = definition.Nome;

        // o rodape de pagina ocupa um espaco reservado no pe de cada pagina
        var footerHeightMm = tpl.FooterBand?.HeightMm ?? 0;
        var contentHeightMm = tpl.ContentHeightMm - footerHeightMm;

        XGraphics gfx = null!;
        var cursorY = 0.0;                 // mm a partir do topo da area util
        object?[]? currentGroupRow = null; // linha vigente, p/ reimprimir grupos na quebra de pagina

        PdfPage AddPage()
        {
            var p = doc.AddPage();
            p.Width = XUnit.FromMillimeter(tpl.PageWidthMm);
            p.Height = XUnit.FromMillimeter(tpl.PageHeightMm);
            return p;
        }

        // abre uma nova pagina e imprime a banda de cabecalho (topo)
        void StartPage()
        {
            gfx?.Dispose();
            gfx = XGraphics.FromPdfPage(AddPage());
            cursorY = 0;
            if (tpl.HeaderBand is { } h)
            {
                DrawBand(gfx, tpl, h, cursorY, null, data, default);
                cursorY += h.HeightMm;
            }
        }

        // quebra de pagina reimprimindo os cabecalhos de grupo vigentes
        void ReprintGroupHeaders()
        {
            if (currentGroupRow is null) return;
            foreach (var g in tpl.Groups)
                if (g.Header is { } gh)
                {
                    DrawBand(gfx, tpl, gh, cursorY, currentGroupRow, data, default);
                    cursorY += gh.HeightMm;
                }
        }

        // garante espaco; se nao couber, nova pagina (reimprimindo grupos, opcional)
        void EnsureSpace(double needMm, bool reprintGroups)
        {
            if (cursorY + needMm <= contentHeightMm) return;
            StartPage();
            if (reprintGroups) ReprintGroupHeaders();
        }

        StartPage();

        var prevKeys = new string?[tpl.Groups.Count];
        var first = true;
        object?[]? lastRow = null;

        foreach (var row in data.Rows)
        {
            var keys = new string?[tpl.Groups.Count];
            for (var gi = 0; gi < tpl.Groups.Count; gi++)
                keys[gi] = data.GetString(row, tpl.Groups[gi].BreakField);

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
                // 1. fecha os grupos que terminaram: rodapes, do mais interno p/ o mais externo
                if (!first)
                    for (var gi = tpl.Groups.Count - 1; gi >= breakFrom; gi--)
                        EmitGroupFooter(tpl.Groups[gi], lastRow);

                // 2. quebra de pagina se algum grupo que vai (re)abrir tem NewPage
                var wantNewPage = !first &&
                    Enumerable.Range(breakFrom, tpl.Groups.Count - breakFrom)
                              .Any(gi => tpl.Groups[gi].NewPage);
                if (wantNewPage)
                {
                    currentGroupRow = null; // pagina nova: nao reimprime grupos antigos
                    StartPage();
                }

                // 3. abre os novos grupos: cabecalhos, do mais externo p/ o mais interno
                for (var gi = breakFrom; gi < tpl.Groups.Count; gi++)
                    if (tpl.Groups[gi].Header is { } gh)
                    {
                        EnsureSpace(gh.HeightMm, reprintGroups: true);
                        DrawBand(gfx, tpl, gh, cursorY, row, data, default);
                        cursorY += gh.HeightMm;
                    }
                currentGroupRow = row;
            }

            prevKeys = keys;
            first = false;
            lastRow = row;

            if (tpl.DetailBand is { } d)
                EmitDetail(d, row);
        }

        // rodapes finais dos grupos ainda abertos (do mais interno p/ o mais externo)
        if (lastRow is not null)
            for (var gi = tpl.Groups.Count - 1; gi >= 0; gi--)
                EmitGroupFooter(tpl.Groups[gi], lastRow);

        gfx?.Dispose();

        // segunda passada: desenha o rodape de pagina no pe de cada pagina,
        // agora que o total de paginas e conhecido (p/ "Pagina X de Y")
        if (tpl.FooterBand is { } footer)
        {
            var total = doc.PageCount;
            var footerTopMm = tpl.PageHeightMm - tpl.MarginBottomMm - footerHeightMm;
            for (var i = 0; i < total; i++)
            {
                using var fg = XGraphics.FromPdfPage(doc.Pages[i], XGraphicsPdfPageOptions.Append);
                // DrawBand soma MarginTop + bandTop; passamos o topo absoluto descontando a margem
                DrawBand(fg, tpl, footer, footerTopMm - tpl.MarginTopMm, null, data,
                    new PageInfo(i + 1, total));
            }
        }

        using var ms = new MemoryStream();
        doc.Save(ms);
        return new ReportOutput
        {
            Content = ms.ToArray(),
            ContentType = ReportOutput.ContentTypeFor(ReportFormat.Pdf),
            FileName = definition.NomeArquivo + ReportOutput.ExtensionFor(ReportFormat.Pdf)
        };

        // ---- helpers locais (fecham sobre gfx/cursorY/...) ----

        void EmitGroupFooter(RbGroup g, object?[]? row)
        {
            if (g.Footer is not { } f || f.Elements.Count == 0) return;
            var h = BandHeight(gfx, tpl, f, row, data);
            if (h <= 0) return;
            EnsureSpace(h, reprintGroups: false);
            DrawBand(gfx, tpl, f, cursorY, row, data, default);
            cursorY += h;
        }

        void EmitDetail(RbBand d, object?[] row)
        {
            var memo = d.Elements.OfType<RbDbMemo>().FirstOrDefault(m => m.Stretch);
            if (memo is null)
            {
                var h = BandHeight(gfx, tpl, d, row, data);
                EnsureSpace(h, reprintGroups: true);
                DrawBand(gfx, tpl, d, cursorY, row, data, default);
                cursorY += h;
                return;
            }

            // detalhe com memo esticado: parte estatica desenhada uma vez,
            // e as linhas do memo fluindo entre paginas
            var mBox = Eff(memo);
            var memoText = data.GetString(row, memo.DataField);
            var font = BuildFont(memo.Font);
            var widthPt = XUnit.FromMillimeter(mBox.Width).Point;
            var lines = mBox.Hidden ? new List<string>() : WrapLines(gfx, memoText, font, widthPt, memo.CharWrap);
            var lineHmm = XUnit.FromPoint(font.GetHeight()).Millimeter;

            // altura da parte nao-memo (labels, descricao) acima/ao lado do memo
            var staticBottom = d.Elements.Where(e => e is not RbDbMemo)
                                         .Select(e => { var b = Eff(e); return b.Hidden ? 0 : b.Top + b.Height; })
                                         .DefaultIfEmpty(0).Max();

            // garante espaco p/ a parte estatica + inicio do memo (ao menos 1 linha)
            EnsureSpace(Math.Max(staticBottom, mBox.Top + lineHmm), reprintGroups: true);

            // elementos fixos do detalhe (uma vez, no inicio da ocorrencia)
            foreach (var el in d.Elements)
                if (el is not RbDbMemo)
                {
                    var e = Eff(el);
                    if (e.Hidden) continue;
                    var xMm = tpl.MarginLeftMm + e.Left;
                    var yMm = tpl.MarginTopMm + cursorY + e.Top;
                    DrawElement(gfx, el, xMm, yMm, e.Width, e.Height, row, data, default);
                }

            var startY = cursorY;
            var memoY = cursorY + mBox.Top;
            var memoXmm = tpl.MarginLeftMm + mBox.Left;
            foreach (var ln in lines)
            {
                if (memoY + lineHmm > contentHeightMm)
                {
                    StartPage();
                    ReprintGroupHeaders();
                    memoY = cursorY;
                }
                if (ln.Length > 0)
                {
                    var rectMm = new XRect(
                        XUnit.FromMillimeter(memoXmm).Point,
                        XUnit.FromMillimeter(tpl.MarginTopMm + memoY).Point,
                        widthPt,
                        XUnit.FromMillimeter(lineHmm).Point);
                    DrawText(gfx, ln, memo.Font, memo.Alignment, rectMm);
                }
                memoY += lineHmm;
            }

            cursorY = Math.Max(memoY, startY + staticBottom);
        }
    }

    /// <summary>
    /// Altura efetiva de uma banda. Bandas fixas usam a altura de projeto.
    /// Bandas dinamicas sem memo esticado tambem (o layout e fixo); so quando
    /// ha um memo esticado a altura e medida pelo conteudo.
    /// </summary>
    private double BandHeight(XGraphics gfx, RbTemplate tpl, RbBand band,
        object?[]? row, ReportDataSet data)
    {
        var hasStretchMemo = band.Elements.Any(e => e is RbDbMemo { Stretch: true });
        if (!band.Dynamic || !hasStretchMemo) return band.HeightMm;

        var h = 0.0;
        foreach (var el in band.Elements)
        {
            var box = Eff(el);
            if (box.Hidden) continue;
            double bottom;
            if (el is RbDbMemo memo && row is not null)
            {
                var text = data.GetString(row, memo.DataField);
                var font = BuildFont(memo.Font);
                var widthPt = XUnit.FromMillimeter(box.Width).Point;
                var lines = WrapLines(gfx, text, font, widthPt, memo.CharWrap);
                var lineHmm = XUnit.FromPoint(font.GetHeight()).Millimeter;
                bottom = box.Top + Math.Max(lines.Count, 1) * lineHmm;
            }
            else
            {
                bottom = box.Top + box.Height;
            }
            h = Math.Max(h, bottom);
        }
        return h;
    }

    private void DrawBand(XGraphics gfx, RbTemplate tpl, RbBand band,
        double bandTopMm, object?[]? row, ReportDataSet data, PageInfo page)
    {
        foreach (var el in band.Elements)
        {
            var e = Eff(el);
            if (e.Hidden) continue;
            var xMm = tpl.MarginLeftMm + e.Left;
            var yMm = tpl.MarginTopMm + bandTopMm + e.Top;
            DrawElement(gfx, el, xMm, yMm, e.Width, e.Height, row, data, page);
        }
    }

    private void DrawElement(XGraphics gfx, RbElement el,
        double xMm, double yMm, double wMm, double hMm, object?[]? row, ReportDataSet data, PageInfo page)
    {
        var x = XUnit.FromMillimeter(xMm).Point;
        var y = XUnit.FromMillimeter(yMm).Point;
        var w = XUnit.FromMillimeter(wMm).Point;
        var h = XUnit.FromMillimeter(hMm).Point;
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
                        if (line.Horizontal) gfx.DrawLine(pen, x, y, x + w, y);
                        else gfx.DrawLine(pen, x, y, x, y + h);
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

            case RbSystemVariable sv:
                DrawText(gfx, SystemVarText(sv.VarType, page), sv.Font, sv.Alignment, rect);
                break;

            case RbDbMemo memo:
                // memos esticados sao tratados no fluxo do detalhe (EmitDetail);
                // aqui atende memos de altura fixa (raro) desenhando as linhas que couberem
                var mtext = row is not null ? data.GetString(row, memo.DataField) : string.Empty;
                if (!string.IsNullOrEmpty(mtext))
                {
                    var font = BuildFont(memo.Font);
                    var lines = WrapLines(gfx, mtext, font, w, memo.CharWrap);
                    var lineHpt = font.GetHeight();
                    var yy = y;
                    foreach (var ln in lines)
                    {
                        if (yy + lineHpt > y + h && h > 0) break;
                        if (ln.Length > 0)
                            DrawText(gfx, ln, memo.Font, memo.Alignment,
                                new XRect(x, yy, w, lineHpt));
                        yy += lineHpt;
                    }
                }
                break;

            case RbImage img:
                var imgBytes = _logo ?? (img.HasImage ? img.Jpeg : null);
                if (imgBytes is not null)
                {
                    try
                    {
                        var ms = new MemoryStream(imgBytes, 0, imgBytes.Length,
                            writable: false, publiclyVisible: true);
                        var image = XImage.FromStream(ms);
                        gfx.DrawImage(image, rect);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"[PdfReportRenderer] Falha ao desenhar imagem ({imgBytes.Length} bytes): {ex.GetType().Name}: {ex.Message}");
                    }
                }
                break;
        }
    }

    private static string SystemVarText(RbSysVarType type, PageInfo page) => type switch
    {
        RbSysVarType.DateTime => DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
        RbSysVarType.Date => DateTime.Now.ToString("dd/MM/yyyy"),
        RbSysVarType.Time => DateTime.Now.ToString("HH:mm:ss"),
        RbSysVarType.PageNo => page.Number.ToString(),
        RbSysVarType.PageCount => page.Total.ToString(),
        RbSysVarType.PageSetDesc => $"Página {page.Number} de {page.Total}",
        _ => string.Empty
    };

    /// <summary>
    /// Quebra o texto em linhas respeitando a largura e os \n. Faz word-wrap
    /// normal; quando um "token" sozinho nao cabe na largura (ex.: o memo de
    /// itens "#001-29,836#002-..." que nao tem espacos), quebra no nivel do
    /// caractere, igual ao ReportBuilder.
    /// </summary>
    private static List<string> WrapLines(XGraphics gfx, string text, XFont font,
        double maxWidthPt, bool charWrap)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var para in text.Split('\n'))
        {
            if (para.Length == 0) { result.Add(string.Empty); continue; }

            var line = string.Empty;
            foreach (var word in para.Split(' '))
            {
                var trial = line.Length == 0 ? word : line + " " + word;
                if (gfx.MeasureString(trial, font).Width <= maxWidthPt)
                {
                    line = trial; // cabe na linha atual
                    continue;
                }

                // nao cabe: fecha a linha corrente
                if (line.Length > 0) { result.Add(line); line = string.Empty; }

                if (gfx.MeasureString(word, font).Width <= maxWidthPt)
                {
                    line = word; // o token cabe sozinho numa linha
                }
                else
                {
                    // token grande demais: quebra em pedacos que caibam
                    var chunks = BreakLongToken(gfx, word, font, maxWidthPt);
                    for (var k = 0; k < chunks.Count - 1; k++) result.Add(chunks[k]);
                    line = chunks[^1];
                }
            }
            result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// Parte um token sem espacos em pedacos que caibam na largura (char-wrap),
    /// usando busca binaria no comprimento para medir poucas vezes.
    /// </summary>
    private static List<string> BreakLongToken(XGraphics gfx, string token, XFont font,
        double maxWidthPt)
    {
        var chunks = new List<string>();
        var start = 0;
        while (start < token.Length)
        {
            var lo = 1;
            var hi = token.Length - start;
            var best = 1; // garante progresso mesmo se 1 char ja estourar
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (gfx.MeasureString(token.Substring(start, mid), font).Width <= maxWidthPt)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            chunks.Add(token.Substring(start, best));
            start += best;
        }
        return chunks;
    }

    private static XFont BuildFont(RbFont f)
    {
        var style = XFontStyleEx.Regular;
        if (f.Bold) style |= XFontStyleEx.Bold;
        if (f.Italic) style |= XFontStyleEx.Italic;
        if (f.Underline) style |= XFontStyleEx.Underline;
        return new XFont(f.Name, f.SizePt, style);
    }

    private static void DrawText(XGraphics gfx, string text, RbFont f, RbAlignment align, XRect rect)
    {
        if (string.IsNullOrEmpty(text)) return;

        var font = BuildFont(f);
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
