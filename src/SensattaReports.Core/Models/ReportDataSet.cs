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

    /// <summary>
    /// Campos extras constantes (mesmo valor em todas as linhas), consultados
    /// quando a coluna nao existe no resultado do SQL. Usado para expor os
    /// parametros da requisicao ao layout (ex.: P_ROMANEIO_INICIAL no cabecalho).
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Extras { get; set; }

    public bool HasColumn(string name) => _index.ContainsKey(name)
        || (Extras?.ContainsKey(name) ?? false);

    /// <summary>Valor cru de uma celula por nome de coluna (null se nao existir).</summary>
    public object? GetValue(object?[] row, string column)
    {
        if (_index.TryGetValue(column, out var i) && i < row.Length) return row[i];
        if (Extras is not null && Extras.TryGetValue(column, out var v)) return v;
        return null;
    }

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
