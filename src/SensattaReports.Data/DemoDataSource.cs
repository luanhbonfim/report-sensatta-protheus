using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;

namespace SensattaReports.Data;

/// <summary>
/// Fonte de dados de DESENVOLVIMENTO: devolve linhas fixas (ignora o SQL),
/// com as mesmas colunas que o report de exemplo espera. Serve para ver o
/// PDF/Excel saindo sem precisar de Oracle.
///
/// Colunas: NUMERO_CAIXA, CONTRA_MARCA, LOTE_EUA, DATA_GERADA, DATA_COLETA,
///          DATA_ULTIMA_PESAGEM, PRODUTO, CODIGO_PRODUTO
/// </summary>
public sealed class DemoDataSource : IReportDataSource
{
    public Task<ReportDataSet> ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parametros,
        CancellationToken ct = default)
    {
        string[] cols =
        [
            "NUMERO_CAIXA", "CONTRA_MARCA", "LOTE_EUA",
            "DATA_GERADA", "DATA_COLETA", "DATA_ULTIMA_PESAGEM",
            "PRODUTO", "CODIGO_PRODUTO"
        ];

        var baseDate = new DateTime(2026, 10, 15, 8, 30, 0);
        var rows = new List<object?[]>();

        // Produto A - dois lotes
        AddProduct(rows, "1001 - DIANTEIRO BOVINO", "1001", "1510260101",
            ["CX0001", "CX0002", "CX0003"], baseDate);
        AddProduct(rows, "1001 - DIANTEIRO BOVINO", "1001", "1510260102",
            ["CX0004", "CX0005"], baseDate.AddHours(1));

        // Produto B
        AddProduct(rows, "2002 - TRASEIRO BOVINO", "2002", "1510260201",
            ["CX0010", "CX0011", "CX0012", "CX0013"], baseDate.AddHours(3));

        return Task.FromResult(new ReportDataSet(cols, rows));
    }

    private static void AddProduct(List<object?[]> rows, string produto, string codigo,
        string lote, string[] caixas, DateTime t)
    {
        var i = 0;
        foreach (var caixa in caixas)
        {
            var gerada = t.AddMinutes(i * 5);
            var coleta = gerada.AddMinutes(20);
            var pesagem = coleta.AddMinutes(10).ToString("dd/MM/yyyy HH:mm:ss");
            rows.Add([caixa, $"MARK-{caixa}", lote, gerada, coleta, pesagem, produto, codigo]);
            i++;
        }
    }
}
