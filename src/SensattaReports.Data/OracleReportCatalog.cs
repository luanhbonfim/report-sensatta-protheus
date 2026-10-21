using Oracle.ManagedDataAccess.Client;
using SensattaReports.Core.Abstractions;

namespace SensattaReports.Data;

/// <summary>
/// Catalogo de reports lido da tabela SIGMA.REPORT (NAME, TEMPLATE).
/// </summary>
public sealed class OracleReportCatalog(OracleOptions options) : IReportCatalog
{
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        await using var cn = new OracleConnection(options.ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT NAME FROM SIGMA.REPORT ORDER BY NAME";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<string>();
        while (await reader.ReadAsync(ct))
            if (!reader.IsDBNull(0))
                list.Add(reader.GetString(0));
        return list;
    }

    public async Task<string?> GetTemplateAsync(string name, CancellationToken ct = default)
    {
        await using var cn = new OracleConnection(options.ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT TEMPLATE FROM SIGMA.REPORT WHERE NAME = :name";
        cmd.BindByName = true;
        cmd.InitialLOBFetchSize = -1; // traz o CLOB inteiro de uma vez

        var p = cmd.CreateParameter();
        p.ParameterName = "name";
        p.Value = name;
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0)) return null;
        return reader.GetString(0);
    }
}
