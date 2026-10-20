using System.Globalization;

namespace SensattaReports.ReportBuilder.Dfm;

public enum DfmValueKind
{
    String,      // 'texto'
    Integer,     // 34131
    Float,       // 0.75
    Boolean,     // True / False
    Identifier,  // poLandscape, clBlack, DEFAULT_CHARSET
    Set,         // [fsBold, fsItalic]
    Binary,      // { 0A54... }
    Collection   // < item ... end > (guardado como texto cru)
}

/// <summary>
/// Um valor de propriedade do DFM. Guarda o tipo e o conteudo, com
/// acessores convenientes para o loader do modelo usar.
/// </summary>
public sealed class DfmValue
{
    public DfmValueKind Kind { get; init; }
    private string? _text;          // String / Identifier / Collection
    private long _int;
    private double _float;
    private bool _bool;
    private IReadOnlyList<string>? _set;
    private byte[]? _binary;

    public static DfmValue OfString(string s) => new() { Kind = DfmValueKind.String, _text = s };
    public static DfmValue OfIdentifier(string s) => new() { Kind = DfmValueKind.Identifier, _text = s };
    public static DfmValue OfCollection(string s) => new() { Kind = DfmValueKind.Collection, _text = s };
    public static DfmValue OfInt(long v) => new() { Kind = DfmValueKind.Integer, _int = v };
    public static DfmValue OfFloat(double v) => new() { Kind = DfmValueKind.Float, _float = v };
    public static DfmValue OfBool(bool v) => new() { Kind = DfmValueKind.Boolean, _bool = v };
    public static DfmValue OfSet(IReadOnlyList<string> v) => new() { Kind = DfmValueKind.Set, _set = v };
    public static DfmValue OfBinary(byte[] v) => new() { Kind = DfmValueKind.Binary, _binary = v };

    public string AsString() => _text ?? string.Empty;

    public long AsInt() => Kind switch
    {
        DfmValueKind.Integer => _int,
        DfmValueKind.Float => (long)_float,
        _ => long.TryParse(_text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0
    };

    public double AsDouble() => Kind switch
    {
        DfmValueKind.Float => _float,
        DfmValueKind.Integer => _int,
        _ => double.TryParse(_text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0
    };

    public bool AsBool() => Kind == DfmValueKind.Boolean ? _bool
        : string.Equals(_text, "True", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> AsSet() => _set ?? [];

    public byte[] AsBinary() => _binary ?? [];

    public bool Contains(string flag) =>
        _set is not null && _set.Any(x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));
}
