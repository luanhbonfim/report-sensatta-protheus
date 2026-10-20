using SensattaReports.Core;
using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;
using SensattaReports.Data;
using SensattaReports.ReportBuilder.Rendering;

var builder = WebApplication.CreateBuilder(args);

// Permite rodar como Servico do Windows (producao) ou daemon systemd (Linux).
// Ambos sao no-op quando rodando como console em desenvolvimento.
builder.Host.UseWindowsService();
builder.Host.UseSystemd();

// --- Renderizadores: PDF (layout do .rtm) + Excel (dado cru) -------------
builder.Services.AddSingleton<IReportRenderer, PdfReportRenderer>();
builder.Services.AddSingleton<IReportRenderer, ExcelReportExporter>();

// --- Repositorio da definicao do report ----------------------------------
var reportSource = builder.Configuration["Reports:Source"] ?? "File";
if (reportSource.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IReportRepository>(_ =>
        new OracleReportRepository(OracleOpts(builder.Configuration)));
else
    builder.Services.AddSingleton<IReportRepository>(_ =>
        new FileReportRepository(ResolveDir(builder)));

// --- Fonte de dados: Oracle (producao) ou Demo (dev, sem banco) ----------
var dataSource = builder.Configuration["Data:Source"] ?? "Demo";
if (dataSource.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IReportDataSource>(_ =>
        new OracleReportDataSource(OracleOpts(builder.Configuration)));
else
    builder.Services.AddSingleton<IReportDataSource, DemoDataSource>();

builder.Services.AddSingleton<ReportService>();

var app = builder.Build();

// Verificacao de saude (o Protheus/monitoramento pode checar).
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

// Download direto (GET) — pratico para o Protheus via FWRest/HttpGet.
// Ex.: /reports/resumo-coletas-eua/pdf?data_ini=2026-10-01&data_fim=2026-10-31
app.MapGet("/reports/{codigo}/pdf", (string codigo, HttpContext ctx, ReportService svc, CancellationToken ct)
    => Generate(codigo, ReportFormat.Pdf, QueryParams(ctx), svc, ct));

app.MapGet("/reports/{codigo}/xlsx", (string codigo, HttpContext ctx, ReportService svc, CancellationToken ct)
    => Generate(codigo, ReportFormat.Xlsx, QueryParams(ctx), svc, ct));

// Alternativa por POST com corpo JSON: { "format": "pdf", "parametros": { "data_ini": "..." } }
app.MapPost("/reports/{codigo}", async (string codigo, GerarDto body, ReportService svc, CancellationToken ct) =>
{
    var fmt = string.Equals(body.Format, "xlsx", StringComparison.OrdinalIgnoreCase)
        ? ReportFormat.Xlsx : ReportFormat.Pdf;
    return await Generate(codigo, fmt, body.Parametros ?? new(), svc, ct);
});

app.Run();

// ---------------------------------------------------------------------------
// Funcoes auxiliares
// ---------------------------------------------------------------------------
static Dictionary<string, string?> QueryParams(HttpContext ctx)
    => ctx.Request.Query.ToDictionary(
        kv => kv.Key,
        kv => (string?)kv.Value.ToString(),
        StringComparer.OrdinalIgnoreCase);

static async Task<IResult> Generate(string codigo, ReportFormat fmt,
    IReadOnlyDictionary<string, string?> parametros, ReportService svc, CancellationToken ct)
{
    try
    {
        var req = new ReportRequest { Codigo = codigo, Format = fmt, Parametros = parametros };
        var output = await svc.GenerateAsync(req, ct);
        return Results.File(output.Content, output.ContentType, output.FileName);
    }
    catch (ReportNotFoundException e)
    {
        return Results.NotFound(new { error = e.Message });
    }
    catch (ReportParameterException e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
}

static OracleOptions OracleOpts(IConfiguration cfg)
    => new() { ConnectionString = cfg["Oracle:ConnectionString"] ?? string.Empty };

static string ResolveDir(WebApplicationBuilder b)
{
    var dir = b.Configuration["Reports:Directory"] ?? "samples";
    return Path.IsPathRooted(dir)
        ? dir
        : Path.GetFullPath(Path.Combine(b.Environment.ContentRootPath, dir));
}

// DTO do corpo do POST
record GerarDto(string? Format, Dictionary<string, string?>? Parametros);
