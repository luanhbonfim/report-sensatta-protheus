using System.Globalization;
using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;

namespace SensattaReports.Core;

/// <summary>Dados crus de um report (definicao + resultado do SQL).</summary>
public sealed record ReportData(ReportDefinition Definition, ReportDataSet Data);

/// <summary>
/// Orquestra a geracao de um report:
/// 1) busca a definicao  2) valida/converte os parametros
/// 3) roda o SQL no Oracle  4) renderiza no formato pedido.
/// </summary>
public sealed class ReportService
{
    private readonly IReportRepository _repository;
    private readonly IReportDataSource _dataSource;
    private readonly IReadOnlyDictionary<ReportFormat, IReportRenderer> _renderers;
    private readonly SemaphoreSlim _gate;

    public ReportService(
        IReportRepository repository,
        IReportDataSource dataSource,
        IEnumerable<IReportRenderer> renderers,
        ReportServiceOptions? options = null)
    {
        _repository = repository;
        _dataSource = dataSource;
        _renderers = renderers.ToDictionary(r => r.Format);
        var max = Math.Max(1, options?.MaxConcurrent ?? 3);
        _gate = new SemaphoreSlim(max, max);
    }

    public async Task<ReportOutput> GenerateAsync(ReportRequest request, CancellationToken ct = default)
    {
        // Resolucao e validacao sao leves -> fora do limitador.
        var def = await _repository.GetByCodigoAsync(request.Codigo, ct)
                  ?? throw new ReportNotFoundException(request.Codigo);

        var bind = BuildParameters(def, request.Parametros);

        if (!_renderers.TryGetValue(request.Format, out var renderer))
            throw new ReportParameterException($"Formato '{request.Format}' nao suportado.");

        // Parte pesada (consulta + render): limita a concorrencia. O excedente
        // aguarda a vez aqui, protegendo CPU e memoria do servidor.
        await _gate.WaitAsync(ct);
        try
        {
            var data = await _dataSource.ExecuteAsync(def.Sql, bind, ct);
            // expoe os parametros ao layout como campos (ex.: P_ROMANEIO_INICIAL)
            data.Extras = BuildExtras(def, request.Parametros);
            return renderer.Render(def, data);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Executa o report e devolve os DADOS CRUS (colunas + linhas), sem
    /// renderizar em PDF/Excel. Usado pelo "modo TV" (visualizacao em tela).
    /// Mesma validacao de parametros e mesmo limitador de concorrencia.
    /// </summary>
    public async Task<ReportData> GetDataAsync(ReportRequest request, CancellationToken ct = default)
    {
        var def = await _repository.GetByCodigoAsync(request.Codigo, ct)
                  ?? throw new ReportNotFoundException(request.Codigo);

        var bind = BuildParameters(def, request.Parametros);

        await _gate.WaitAsync(ct);
        try
        {
            var data = await _dataSource.ExecuteAsync(def.Sql, bind, ct);
            data.Extras = BuildExtras(def, request.Parametros);
            return new ReportData(def, data);
        }
        finally
        {
            _gate.Release();
        }
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

    /// <summary>
    /// Monta os campos extras a partir dos parametros recebidos, para o layout
    /// poder exibi-los. Cada parametro vira ate tres campos: o nome como veio,
    /// em MAIUSCULAS e com prefixo "P_" (convencao do ReportBuilder/Sensatta).
    /// So inclui parametros declarados e preenchidos.
    /// </summary>
    private static Dictionary<string, object?> BuildExtras(
        ReportDefinition def,
        IReadOnlyDictionary<string, string?> recebidos)
    {
        var extras = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in def.Parametros)
        {
            if (!recebidos.TryGetValue(p.Name, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;
            extras[p.Name] = raw;
            extras[p.Name.ToUpperInvariant()] = raw;
            extras["P_" + p.Name.ToUpperInvariant()] = raw;
        }
        return extras;
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
