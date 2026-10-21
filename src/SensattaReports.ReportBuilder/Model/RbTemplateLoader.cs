using SensattaReports.ReportBuilder.Dfm;

namespace SensattaReports.ReportBuilder.Model;

/// <summary>
/// Converte a arvore crua do DFM (DfmNode) no modelo tipado (RbTemplate),
/// pronto para o renderizador.
/// </summary>
public static class RbTemplateLoader
{
    public static RbTemplate Load(string rtmText)
        => FromNode(DfmParser.Parse(rtmText));

    public static RbTemplate FromNode(DfmNode report)
    {
        var orientation = report.GetString("PrinterSetup.Orientation");
        var landscape = orientation.Equals("poLandscape", StringComparison.OrdinalIgnoreCase);

        var bands = new Dictionary<RbBandKind, RbBand>();
        foreach (var child in report.Children)
        {
            var kind = BandKind(child.Type);
            if (kind != RbBandKind.Unknown)
                bands[kind] = LoadBand(child, kind);
        }

        var groups = new List<RbGroup>();
        foreach (var g in report.ChildrenOfType("TppGroup"))
        {
            RbBand? header = null, footer = null;
            foreach (var b in g.Children)
            {
                var kind = BandKind(b.Type);
                if (kind == RbBandKind.GroupHeader) header = LoadBand(b, kind);
                else if (kind == RbBandKind.GroupFooter) footer = LoadBand(b, kind);
            }
            groups.Add(new RbGroup
            {
                BreakField = g.GetString("BreakName"),
                Header = header,
                Footer = footer,
                NewPage = g.GetBool("NewPage")
            });
        }

        return new RbTemplate
        {
            PageWidthMm = Mm(report.GetInt("PrinterSetup.mmPaperWidth", 210000)),
            PageHeightMm = Mm(report.GetInt("PrinterSetup.mmPaperHeight", 297000)),
            Landscape = landscape,
            MarginLeftMm = Mm(report.GetInt("PrinterSetup.mmMarginLeft", 6350)),
            MarginTopMm = Mm(report.GetInt("PrinterSetup.mmMarginTop", 6350)),
            MarginRightMm = Mm(report.GetInt("PrinterSetup.mmMarginRight", 6350)),
            MarginBottomMm = Mm(report.GetInt("PrinterSetup.mmMarginBottom", 6350)),
            DataPipelineName = report.GetString("DataPipelineName"),
            TitleBand = bands.GetValueOrDefault(RbBandKind.Title),
            HeaderBand = bands.GetValueOrDefault(RbBandKind.Header),
            DetailBand = bands.GetValueOrDefault(RbBandKind.Detail),
            FooterBand = bands.GetValueOrDefault(RbBandKind.Footer),
            SummaryBand = bands.GetValueOrDefault(RbBandKind.Summary),
            Groups = groups
        };
    }

    private static RbBand LoadBand(DfmNode band, RbBandKind kind)
    {
        var result = new RbBand
        {
            Kind = kind,
            HeightMm = Mm(band.GetInt("mmHeight")),
            Dynamic = band.GetString("PrintHeight").Equals("phDynamic", StringComparison.OrdinalIgnoreCase)
        };
        foreach (var el in band.Children)
        {
            var element = LoadElement(el);
            if (element is not null) result.Elements.Add(element);
        }
        return result;
    }

    private static RbElement? LoadElement(DfmNode n)
    {
        double L() => Mm(n.GetInt("mmLeft"));
        double T() => Mm(n.GetInt("mmTop"));
        double W() => Mm(n.GetInt("mmWidth"));
        double H() => Mm(n.GetInt("mmHeight"));

        switch (n.Type)
        {
            case "TppLabel":
                return new RbLabel
                {
                    Name = n.GetString("UserName"),
                    LeftMm = L(), TopMm = T(), WidthMm = W(), HeightMm = H(),
                    Text = n.GetString("Caption"),
                    Font = LoadFont(n),
                    Alignment = Align(n.GetString("TextAlignment"))
                };

            case "TppDBText":
                return new RbDbText
                {
                    Name = n.GetString("UserName"),
                    LeftMm = L(), TopMm = T(), WidthMm = W(), HeightMm = H(),
                    DataField = n.GetString("DataField"),
                    Font = LoadFont(n),
                    Alignment = Align(n.GetString("TextAlignment"))
                };

            case "TppDBMemo":
                return new RbDbMemo
                {
                    Name = n.GetString("UserName"),
                    LeftMm = L(), TopMm = T(), WidthMm = W(), HeightMm = H(),
                    DataField = n.GetString("DataField"),
                    Font = LoadFont(n),
                    Alignment = Align(n.GetString("TextAlignment")),
                    Stretch = n.GetBool("Stretch"),
                    CharWrap = n.GetBool("CharWrap")
                };

            case "TppSystemVariable":
                return new RbSystemVariable
                {
                    Name = n.GetString("UserName"),
                    LeftMm = L(), TopMm = T(), WidthMm = W(), HeightMm = H(),
                    VarType = SysVar(n.GetString("VarType")),
                    Font = LoadFont(n),
                    Alignment = Align(n.GetString("TextAlignment"))
                };

            case "TppLine":
                return new RbLine
                {
                    Name = n.GetString("UserName"),
                    LeftMm = L(), TopMm = T(), WidthMm = W(), HeightMm = H(),
                    WeightPt = n.GetDouble("Weight", 1.0),
                    Position = n.GetString("Position")
                };

            case "TppShape":
                return new RbShape
                {
                    Name = n.GetString("UserName"),
                    LeftMm = L(), TopMm = T(), WidthMm = W(), HeightMm = H(),
                    BrushColor = RbColor.Parse(n.GetString("Brush.Color", "clWhite"))
                };

            case "TppImage":
                var blob = n.Get("Picture.Data")?.AsBinary() ?? [];
                return new RbImage
                {
                    Name = n.GetString("UserName"),
                    LeftMm = L(), TopMm = T(), WidthMm = W(), HeightMm = H(),
                    Jpeg = DelphiPicture.ExtractJpeg(blob)
                };

            default:
                return null; // elemento ainda nao suportado: ignora
        }
    }

    private static RbFont LoadFont(DfmNode n) => new()
    {
        Name = n.GetString("Font.Name", "Arial"),
        SizePt = n.GetDouble("Font.Size", 10),
        Bold = n.HasFlag("Font.Style", "fsBold"),
        Italic = n.HasFlag("Font.Style", "fsItalic"),
        Underline = n.HasFlag("Font.Style", "fsUnderline"),
        Color = RbColor.Parse(n.GetString("Font.Color", "clBlack"))
    };

    private static RbSysVarType SysVar(string vt) => vt switch
    {
        "vtDateTime" => RbSysVarType.DateTime,
        "vtDate" or "vtPrintDate" => RbSysVarType.Date,
        "vtTime" => RbSysVarType.Time,
        "vtPageNo" or "vtPageNoDesc" => RbSysVarType.PageNo,
        "vtPageCount" or "vtPageCountDesc" => RbSysVarType.PageCount,
        "vtPageSetDesc" or "vtPageSet" => RbSysVarType.PageSetDesc,
        _ => RbSysVarType.Other
    };

    private static RbAlignment Align(string ta) => ta switch
    {
        "taRightJustified" => RbAlignment.Right,
        "taCentered" => RbAlignment.Center,
        _ => RbAlignment.Left
    };

    private static RbBandKind BandKind(string type) => type switch
    {
        "TppTitleBand" => RbBandKind.Title,
        "TppHeaderBand" => RbBandKind.Header,
        "TppGroupHeaderBand" => RbBandKind.GroupHeader,
        "TppDetailBand" => RbBandKind.Detail,
        "TppGroupFooterBand" => RbBandKind.GroupFooter,
        "TppFooterBand" => RbBandKind.Footer,
        "TppSummaryBand" => RbBandKind.Summary,
        _ => RbBandKind.Unknown
    };

    /// <summary>ReportBuilder guarda em milesimos de mm.</summary>
    private static double Mm(long milesimos) => milesimos / 1000.0;
}
