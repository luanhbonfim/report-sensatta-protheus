using System.Globalization;

namespace SensattaReports.Core.Models;

/// <summary>
/// Resultado tabular de um SQL: nomes de coluna + linhas de valores.
/// E o que alimenta tanto o PDF (via DataField) quanto o Excel.
/// </summary>
public sealed class ReportDataSet
{
    private readonly Dictionary<string, int> _index;

    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<object?[]> Rows { get; }

    public ReportDataSet(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        Columns = columns;
        Rows = rows;
        // mapa nome-da-coluna -> indice, sem diferenciar maiusculas/minusculas
        _index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
            _index[columns[i]] = i;
    }

    public bool HasColumn(string name) => _index.ContainsKey(name);

    /// <summary>Valor cru de uma celula por nome de coluna (null se nao existir).</summary>
    public object? GetValue(object?[] row, string column)
        => _index.TryGetValue(column, out var i) && i < row.Length ? row[i] : null;

    /// <summary>
    /// Valor ja formatado como texto para exibir no report.
    /// Datas saem em dd/MM/yyyy (ou com hora se tiver), decimais com 2 casas.
    /// </summary>
    public string GetString(object?[] row, string column)
    {
        var v = GetValue(row, column);
        return Format(v);
    }

    public static string Format(object? v) => v switch
    {
        null => string.Empty,
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : dt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
        decimal dec => dec.ToString("N2", CultureInfo.GetCultureInfo("pt-BR")),
        double dbl => dbl.ToString("N2", CultureInfo.GetCultureInfo("pt-BR")),
        _ => v.ToString() ?? string.Empty
    };
}
