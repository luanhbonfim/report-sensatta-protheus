using Oracle.ManagedDataAccess.Client;
using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;

namespace SensattaReports.Data;

/// <summary>
/// Le a definicao do report da tabela REPORT no Oracle.
///
/// Tabela esperada (ver README / script):
///   REPORT(CODIGO, NOME, SQL_TEXT CLOB, TEMPLATE_RTM CLOB, PARAMETROS CLOB)
/// </summary>
public sealed class OracleReportRepository(OracleOptions options) : IReportRepository
{
    public async Task<ReportDefinition?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        const string sql = """
            SELECT NOME, SQL_TEXT, TEMPLATE_RTM, PARAMETROS
              FROM REPORT
             WHERE CODIGO = :codigo
            """;

        await using var cn = new OracleConnection(options.ConnectionString);
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.BindByName = true;
        var p = cmd.CreateParameter();
        p.ParameterName = "codigo";
        p.Value = codigo;
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var nome = reader.GetString(0);
        var sqlText = reader.GetString(1);
        var rtm = reader.GetString(2);
        var paramsJson = reader.IsDBNull(3) ? null : reader.GetString(3);

        return new ReportDefinition
        {
            Codigo = codigo,
            Nome = nome,
            Sql = sqlText,
            TemplateRtm = rtm,
            Parametros = ReportParametersJson.Parse(paramsJson)
        };
    }
}
