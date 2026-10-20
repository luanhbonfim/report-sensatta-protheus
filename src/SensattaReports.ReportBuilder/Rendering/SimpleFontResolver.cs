using PdfSharp.Fonts;

namespace SensattaReports.ReportBuilder.Rendering;

/// <summary>
/// Resolvedor de fontes do PdfSharp que funciona em Windows e Linux.
/// Mapeia qualquer familia "sans" (Arial e afins) para a primeira fonte
/// equivalente encontrada no disco (Arial no Windows; Liberation/DejaVu
/// no Linux). Assim o PDF sai com texto em qualquer uma das plataformas.
///
/// No Ubuntu, para ter Arial "de verdade": sudo apt install ttf-mscorefonts-installer
/// (senao, Liberation Sans ja e metricamente compativel).
/// </summary>
public sealed class SimpleFontResolver : IFontResolver
{
    private static readonly Dictionary<string, byte[]> Cache = new();
    private static readonly Lock Gate = new();

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
        => new(Face(bold, italic));

    public byte[]? GetFont(string faceName)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(faceName, out var cached)) return cached;
            var path = Candidates(faceName).FirstOrDefault(File.Exists);
            var bytes = path is not null ? File.ReadAllBytes(path) : null;
            if (bytes is not null) Cache[faceName] = bytes;
            return bytes;
        }
    }

    private static string Face(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true) => "sans-bolditalic",
        (true, false) => "sans-bold",
        (false, true) => "sans-italic",
        _ => "sans-regular"
    };

    private static IEnumerable<string> Candidates(string face)
    {
        // listas por estilo + fallback para regular no fim
        string[] regular =
        [
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/msttcorefonts/Arial.ttf",
            @"C:\Windows\Fonts\arial.ttf"
        ];
        string[] bold =
        [
            "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "/usr/share/fonts/truetype/msttcorefonts/Arial_Bold.ttf",
            @"C:\Windows\Fonts\arialbd.ttf"
        ];
        string[] italic =
        [
            "/usr/share/fonts/truetype/liberation/LiberationSans-Italic.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Oblique.ttf",
            "/usr/share/fonts/truetype/msttcorefonts/Arial_Italic.ttf",
            @"C:\Windows\Fonts\ariali.ttf"
        ];
        string[] boldItalic =
        [
            "/usr/share/fonts/truetype/liberation/LiberationSans-BoldItalic.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-BoldOblique.ttf",
            "/usr/share/fonts/truetype/msttcorefonts/Arial_Bold_Italic.ttf",
            @"C:\Windows\Fonts\arialbi.ttf"
        ];

        return face switch
        {
            "sans-bold" => [.. bold, .. regular],
            "sans-italic" => [.. italic, .. regular],
            "sans-bolditalic" => [.. boldItalic, .. bold, .. regular],
            _ => regular
        };
    }
}
