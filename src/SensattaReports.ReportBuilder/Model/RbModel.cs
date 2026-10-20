namespace SensattaReports.ReportBuilder.Model;

/// <summary>Cor RGB simples (o modelo nao depende do PdfSharp).</summary>
public readonly record struct RbColor(byte R, byte G, byte B)
{
    public static readonly RbColor Black = new(0, 0, 0);
    public static readonly RbColor White = new(255, 255, 255);

    /// <summary>
    /// Converte um valor de cor do Delphi: constante (clBlack, clGray...)
    /// ou hexadecimal $00BBGGRR.
    /// </summary>
    public static RbColor Parse(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return Black;
        id = id.Trim();

        if (id.StartsWith('$'))
        {
            // TColor do Delphi: 0x00BBGGRR (byte baixo = R)
            if (uint.TryParse(id[1..], System.Globalization.NumberStyles.HexNumber,
                    null, out var v))
                return new RbColor((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
            return Black;
        }

        return id.ToLowerInvariant() switch
        {
            "clblack" => new RbColor(0, 0, 0),
            "clwhite" => White,
            "clgray" or "cldkgray" => new RbColor(128, 128, 128),
            "clsilver" or "clltgray" => new RbColor(192, 192, 192),
            "clred" => new RbColor(255, 0, 0),
            "clgreen" => new RbColor(0, 128, 0),
            "cllime" => new RbColor(0, 255, 0),
            "clblue" => new RbColor(0, 0, 255),
            "clnavy" => new RbColor(0, 0, 128),
            "clyellow" => new RbColor(255, 255, 0),
            "claqua" => new RbColor(0, 255, 255),
            "clfuchsia" => new RbColor(255, 0, 255),
            "clteal" => new RbColor(0, 128, 128),
            "clolive" => new RbColor(128, 128, 0),
            "clpurple" => new RbColor(128, 0, 128),
            "clmaroon" => new RbColor(128, 0, 0),
            "clwindow" => White,
            "clwindowtext" => Black,
            _ => Black
        };
    }
}

public enum RbAlignment { Left, Center, Right }

/// <summary>Fonte de um elemento de texto.</summary>
public sealed class RbFont
{
    public string Name { get; init; } = "Arial";
    public double SizePt { get; init; } = 10;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public RbColor Color { get; init; } = RbColor.Black;
}

/// <summary>Base de qualquer elemento posicionado (em milimetros).</summary>
public abstract class RbElement
{
    public double LeftMm { get; init; }
    public double TopMm { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
}

/// <summary>Texto fixo (TppLabel).</summary>
public sealed class RbLabel : RbElement
{
    public string Text { get; init; } = string.Empty;
    public RbFont Font { get; init; } = new();
    public RbAlignment Alignment { get; init; } = RbAlignment.Left;
}

/// <summary>Campo ligado a uma coluna do dataset (TppDBText).</summary>
public sealed class RbDbText : RbElement
{
    public string DataField { get; init; } = string.Empty;
    public RbFont Font { get; init; } = new();
    public RbAlignment Alignment { get; init; } = RbAlignment.Left;
}

/// <summary>Linha (TppLine).</summary>
public sealed class RbLine : RbElement
{
    public double WeightPt { get; init; } = 1.0;
    /// <summary>Borda onde a linha e desenhada: lpLeft, lpRight, lpTop, lpBottom ou vazio.</summary>
    public string Position { get; init; } = "";
    /// <summary>true = linha horizontal; false = vertical. Derivado das dimensoes.</summary>
    public bool Horizontal => WidthMm >= HeightMm;
}

/// <summary>Retangulo/forma (TppShape) — usado nas barras de grupo.</summary>
public sealed class RbShape : RbElement
{
    public RbColor BrushColor { get; init; } = RbColor.White;
}

/// <summary>Imagem (TppImage) — guarda os bytes JPEG ja extraidos.</summary>
public sealed class RbImage : RbElement
{
    public byte[] Jpeg { get; init; } = [];
    public bool HasImage => Jpeg.Length > 0;
}

public enum RbBandKind
{
    Title,
    Header,
    GroupHeader,
    Detail,
    GroupFooter,
    Footer,
    Summary,
    Unknown
}

/// <summary>Uma banda com altura e seus elementos.</summary>
public sealed class RbBand
{
    public RbBandKind Kind { get; init; }
    public double HeightMm { get; init; }
    public List<RbElement> Elements { get; } = [];
}

/// <summary>Um grupo: campo de quebra + bandas de cabecalho/rodape.</summary>
public sealed class RbGroup
{
    public string BreakField { get; init; } = string.Empty;
    public RbBand? Header { get; init; }
    public RbBand? Footer { get; init; }
}

/// <summary>O template inteiro, pronto para o renderizador consumir.</summary>
public sealed class RbTemplate
{
    public double PageWidthMm { get; init; } = 210;
    public double PageHeightMm { get; init; } = 297;
    public bool Landscape { get; init; }

    public double MarginLeftMm { get; init; } = 6.35;
    public double MarginTopMm { get; init; } = 6.35;
    public double MarginRightMm { get; init; } = 6.35;
    public double MarginBottomMm { get; init; } = 6.35;

    public string DataPipelineName { get; init; } = string.Empty;

    public RbBand? TitleBand { get; init; }
    public RbBand? HeaderBand { get; init; }
    public RbBand? DetailBand { get; init; }
    public RbBand? FooterBand { get; init; }
    public RbBand? SummaryBand { get; init; }

    /// <summary>Grupos na ordem definida (Group1, Group2, ...).</summary>
    public IReadOnlyList<RbGroup> Groups { get; init; } = [];

    public double ContentWidthMm => PageWidthMm - MarginLeftMm - MarginRightMm;
    public double ContentHeightMm => PageHeightMm - MarginTopMm - MarginBottomMm;
}
