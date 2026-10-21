using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SensattaReports.ReportBuilder.Dfm;

/// <summary>Erro ao interpretar o texto do .rtm.</summary>
public sealed class DfmParseException(string message) : Exception(message);

/// <summary>
/// Parser do formato de streaming de componente do Delphi (.dfm / .rtm do
/// ReportBuilder). Le linha a linha e devolve a arvore de objetos.
///
/// Esta e a traducao direta do prototipo validado em tools/rtm_parser.py
/// contra o arquivo real resumo-coletas-eua.rtm.
/// </summary>
public sealed partial class DfmParser
{
    private readonly string[] _lines;
    private int _i;

    public DfmParser(string text)
    {
        _lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        _i = 0;
    }

    /// <summary>Atalho: interpreta o texto e devolve a raiz (o TppReport).</summary>
    public static DfmNode Parse(string text) => new DfmParser(text).ParseRoot();

    public DfmNode ParseRoot()
    {
        while (_i < _lines.Length && _lines[_i].Trim().Length == 0) _i++;
        return ParseObject();
    }

    private string? Peek() => _i < _lines.Length ? _lines[_i] : null;

    private DfmNode ParseObject()
    {
        var line = (Peek() ?? string.Empty).Trim();

        DfmNode node;
        var withName = ObjectWithNameRegex().Match(line);
        if (withName.Success)
        {
            node = new DfmNode(withName.Groups[2].Value, withName.Groups[3].Value);
        }
        else
        {
            // cabecalho so com tipo (caso da raiz): "object TppReport"
            var typeOnly = ObjectTypeOnlyRegex().Match(line);
            if (!typeOnly.Success)
                throw new DfmParseException($"Esperava 'object [Nome:] Tipo', achei: {line}");
            node = new DfmNode(typeOnly.Groups[2].Value, typeOnly.Groups[2].Value);
        }
        _i++;

        while (true)
        {
            var raw = Peek();
            if (raw is null) throw new DfmParseException("Fim de arquivo antes de 'end'.");
            var s = raw.Trim();

            if (s == "end") { _i++; break; }
            if (s.Length == 0) { _i++; continue; }
            if (s.StartsWith("object ") || s.StartsWith("inherited "))
            {
                node.Children.Add(ParseObject());
                continue;
            }

            var (key, value) = ParseProperty();
            node.Props[key] = value;
        }

        return node;
    }

    private (string Key, DfmValue Value) ParseProperty()
    {
        var s = _lines[_i].Trim();
        var eq = s.IndexOf('=');
        if (eq < 0) throw new DfmParseException($"Propriedade sem '=': {s}");
        var key = s[..eq].Trim();
        var rest = s[(eq + 1)..].Trim();
        _i++;

        // DFM quebra valores longos (tipicamente strings) para a linha seguinte:
        //   Caption =
        //     'texto longo'
        // Quando nada vem depois do '=', o valor comeca na proxima linha.
        if (rest.Length == 0)
        {
            var next = Peek();
            if (next is not null)
            {
                rest = next.Trim();
                _i++;
            }
        }

        if (rest.StartsWith('{')) return (key, ReadBinary(rest));
        if (rest.StartsWith('<')) return (key, ReadCollection(rest));
        if (rest.StartsWith('\'') || rest.StartsWith('#')) return (key, ReadString(rest));
        return (key, ParseScalar(rest));
    }

    // bloco binario: acumula hex ate achar '}'
    private DfmValue ReadBinary(string first)
    {
        var sb = new StringBuilder(first.TrimStart('{').Trim());
        while (sb.ToString().IndexOf('}') < 0)
        {
            var raw = Peek();
            if (raw is null) throw new DfmParseException("Bloco binario sem '}' de fechamento.");
            sb.Append(raw.Trim());
            _i++;
        }
        var hex = sb.ToString().Replace("}", string.Empty);
        hex = WhitespaceRegex().Replace(hex, string.Empty);
        return DfmValue.OfBinary(Convert.FromHexString(hex));
    }

    // colecao <item ... end>: guarda o texto cru (nao usada nos reports atuais)
    private DfmValue ReadCollection(string first)
    {
        var sb = new StringBuilder(first);
        while (sb.ToString().IndexOf('>') < 0)
        {
            var raw = Peek();
            if (raw is null) throw new DfmParseException("Colecao sem '>' de fechamento.");
            sb.Append(' ').Append(raw.Trim());
            _i++;
        }
        return DfmValue.OfCollection(sb.ToString());
    }

    // string: Delphi quebra strings longas com '+' no fim da linha
    private DfmValue ReadString(string first)
    {
        var parts = new List<string> { first };
        while (parts[^1].TrimEnd().EndsWith('+'))
        {
            parts[^1] = parts[^1].TrimEnd()[..^1]; // remove o '+'
            var raw = Peek();
            if (raw is null) break;
            parts.Add(raw.Trim());
            _i++;
        }
        return DfmValue.OfString(DecodeDelphiString(string.Concat(parts)));
    }

    /// <summary>Decodifica 'texto''aspas'#13#10'mais' numa string .NET.</summary>
    private static string DecodeDelphiString(string tok)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < tok.Length)
        {
            var c = tok[i];
            if (c == '\'')
            {
                i++;
                while (i < tok.Length)
                {
                    if (tok[i] == '\'' && i + 1 < tok.Length && tok[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i += 2;
                    }
                    else if (tok[i] == '\'')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        sb.Append(tok[i]);
                        i++;
                    }
                }
            }
            else if (c == '#')
            {
                var j = i + 1;
                var num = new StringBuilder();
                while (j < tok.Length && char.IsDigit(tok[j])) num.Append(tok[j++]);
                if (num.Length > 0) sb.Append((char)int.Parse(num.ToString(), CultureInfo.InvariantCulture));
                i = j;
            }
            else
            {
                i++;
            }
        }
        return sb.ToString();
    }

    private static DfmValue ParseScalar(string rest)
    {
        if (rest == "True") return DfmValue.OfBool(true);
        if (rest == "False") return DfmValue.OfBool(false);

        if (rest.StartsWith('['))
        {
            var inner = rest.Trim('[', ']').Trim();
            IReadOnlyList<string> items = inner.Length == 0
                ? []
                : inner.Split(',').Select(x => x.Trim()).ToList();
            return DfmValue.OfSet(items);
        }

        if (IntRegex().IsMatch(rest))
            return DfmValue.OfInt(long.Parse(rest, CultureInfo.InvariantCulture));
        if (FloatRegex().IsMatch(rest))
            return DfmValue.OfFloat(double.Parse(rest, CultureInfo.InvariantCulture));

        return DfmValue.OfIdentifier(rest);
    }

    [GeneratedRegex(@"^(object|inherited)\s+(\w+):\s+(\w+)")]
    private static partial Regex ObjectWithNameRegex();

    [GeneratedRegex(@"^(object|inherited)\s+(\w+)\s*$")]
    private static partial Regex ObjectTypeOnlyRegex();

    [GeneratedRegex(@"^-?\d+$")]
    private static partial Regex IntRegex();

    [GeneratedRegex(@"^-?\d+\.\d+$")]
    private static partial Regex FloatRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
