using Oracle.ManagedDataAccess.Client;
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

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var cols = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
            cols[i] = reader.GetName(i);

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var v = reader.GetValue(i);
                row[i] = v is DBNull ? null : v;
            }
            rows.Add(row);
        }

        return new ReportDataSet(cols, rows);
    }
}
