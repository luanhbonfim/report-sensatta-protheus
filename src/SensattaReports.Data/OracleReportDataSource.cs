using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;

namespace SensattaReports.Data;

/// <summary>Configuracao de conexao com o Oracle do Sensatta.</summary>
public sealed class OracleOptions
{
    public string ConnectionString { get; init; } = string.Empty;
}

/// <summary>
/// Executa o SQL do report no Oracle (read-only) e devolve o resultado.
/// Usa bind por nome (:data_ini) — nunca concatena valor na query.
/// </summary>
public sealed class OracleReportDataSource(OracleOptions options) : IReportDataSource
{
    public async Task<ReportDataSet> ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parametros,
        CancellationToken ct = default)
    {
        await using var cn = new OracleConnection(options.ConnectionString);
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.BindByName = true; // casa :nome do SQL com o nome do parametro

        foreach (var (nome, valor) in parametros)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nome;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        await using var reader = (OracleDataReader)await cmd.ExecuteReaderAsync(ct);

        var cols = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
            cols[i] = reader.GetName(i);

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                row[i] = ReadCell(reader, i);
            rows.Add(row);
        }

        return new ReportDataSet(cols, rows);
    }

    /// <summary>
    /// Le uma celula de forma robusta. O caminho normal (GetValue) atende quase
    /// tudo; colunas NUMBER calculadas (SUM/AVG/janelas sem CAST) vem como
    /// OracleDecimal de precisao alta e estouram a conversao para decimal
    /// ("Specified cast is not valid"). Nesse caso arredondamos o OracleDecimal
    /// para uma precisao que cabe em decimal.
    /// </summary>
    private static object? ReadCell(OracleDataReader reader, int i)
    {
        try
        {
            var v = reader.GetValue(i);
            return v is DBNull ? null : v;
        }
        catch (Exception) // InvalidCast / Overflow em NUMBER sem precisao definida
        {
            var ov = reader.GetOracleValue(i);
            return NormalizeOracle(ov);
        }
    }

    private static object? NormalizeOracle(object? ov) => ov switch
    {
        null => null,
        OracleDecimal d when d.IsNull => null,
        OracleDecimal d => SafeDecimal(d),
        OracleString s => s.IsNull ? null : s.Value,
        OracleClob c => c.IsNull ? null : c.Value,
        OracleDate od => od.IsNull ? null : (object)od.Value,
        OracleTimeStamp ts => ts.IsNull ? null : (object)ts.Value,
        _ => ov.ToString()
    };

    private static object SafeDecimal(OracleDecimal d)
    {
        // arredonda para 28 digitos significativos (limite do System.Decimal)
        try { return OracleDecimal.SetPrecision(d, 28).Value; }
        catch
        {
            try { return (double)d; }
            catch { return d.ToString(); }
        }
    }
}
