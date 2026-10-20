using System.Globalization;
using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;

namespace SensattaReports.Core;

/// <summary>
/// Orquestra a geracao de um report:
/// 1) busca a definicao  2) valida/converte os parametros
/// 3) roda o SQL no Oracle  4) renderiza no formato pedido.
/// </summary>
public sealed class ReportService(
    IReportRepository repository,
    IReportDataSource dataSource,
    IEnumerable<IReportRenderer> renderers)
{
    private readonly IReadOnlyDictionary<ReportFormat, IReportRenderer> _renderers =
        renderers.ToDictionary(r => r.Format);

    public async Task<ReportOutput> GenerateAsync(ReportRequest request, CancellationToken ct = default)
    {
        var def = await repository.GetByCodigoAsync(request.Codigo, ct)
                  ?? throw new ReportNotFoundException(request.Codigo);

        var bind = BuildParameters(def, request.Parametros);

        var data = await dataSource.ExecuteAsync(def.Sql, bind, ct);

        if (!_renderers.TryGetValue(request.Format, out var renderer))
            throw new ReportParameterException($"Formato '{request.Format}' nao suportado.");

        return renderer.Render(def, data);
    }

    /// <summary>
    /// Valida os valores recebidos contra os parametros declarados e
    /// converte para o tipo certo. So passa adiante parametros declarados
    /// (evita injetar binds arbitrarios).
    /// </summary>
    private static Dictionary<string, object?> BuildParameters(
        ReportDefinition def,
        IReadOnlyDictionary<string, string?> recebidos)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in def.Parametros)
        {
            recebidos.TryGetValue(p.Name, out var raw);
            var vazio = string.IsNullOrWhiteSpace(raw);

            if (vazio)
            {
                if (p.Required)
                    throw new ReportParameterException($"Parametro obrigatorio ausente: '{p.Name}'.");
                result[p.Name] = null;
                continue;
            }

            result[p.Name] = Convert(p, raw!);
        }

        return result;
    }

    private static object Convert(ReportParameter p, string raw)
    {
        try
        {
            return p.Type switch
            {
                ReportParameterType.Integer => long.Parse(raw, CultureInfo.InvariantCulture),
                ReportParameterType.Decimal => decimal.Parse(raw, NumberStyles.Any, CultureInfo.InvariantCulture),
                ReportParameterType.Date or ReportParameterType.DateTime => ParseDate(raw),
                _ => raw
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new ReportParameterException(
                $"Valor invalido para '{p.Name}' (esperado {p.Type}): '{raw}'.");
        }
    }

    /// <summary>Aceita ISO (2026-01-31) e formato BR (31/01/2026[ HH:mm:ss]).</summary>
    private static DateTime ParseDate(string raw)
    {
        string[] formatos =
        [
            "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss",
            "dd/MM/yyyy", "dd/MM/yyyy HH:mm:ss"
        ];
        if (DateTime.TryParseExact(raw, formatos, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
            return dt;
        return DateTime.Parse(raw, CultureInfo.InvariantCulture);
    }
}
