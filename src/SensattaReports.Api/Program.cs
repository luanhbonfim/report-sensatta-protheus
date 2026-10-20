using SensattaReports.Core;
using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;
using SensattaReports.Data;
using SensattaReports.ReportBuilder.Rendering;

var builder = WebApplication.CreateBuilder(args);

// Config local (senha do Oracle etc.) que NAO vai para o Git (gitignore).
// Sobrepoe o appsettings.json. Opcional: se nao existir, segue sem erro.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Permite rodar como Servico do Windows (producao) ou daemon systemd (Linux).
// Ambos sao no-op quando rodando como console em desenvolvimento.
builder.Host.UseWindowsService();
builder.Host.UseSystemd();

// --- Renderizadores: PDF (layout do .rtm) + Excel (dado cru) -------------
// Logo generica usada em todos os reports (na posicao que cada .rtm define).
var logoPath = builder.Configuration["Reports:LogoPath"];
if (string.IsNullOrWhiteSpace(logoPath))
    logoPath = Path.Combine(AppContext.BaseDirectory, "reports", "_assets", "logo.png");
var logo = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;

builder.Services.AddSingleton<IReportRenderer>(new PdfReportRenderer(logo));
builder.Services.AddSingleton<IReportRenderer, ExcelReportExporter>();

// --- Repositorio da definicao do report ----------------------------------
var reportSource = builder.Configuration["Reports:Source"] ?? "File";
if (reportSource.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IReportRepository>(_ =>
        new OracleReportRepository(OracleOpts(builder.Configuration)));
else
    builder.Services.AddSingleton<IReportRepository>(_ =>
        new FileReportRepository(ResolveDir(builder)));

// --- Fonte de dados: Oracle ou Demo --------------------------------------
// Se houver connection string do Oracle, usa Oracle automaticamente; senao,
// usa a fonte Demo (dev, sem banco). Da para forcar com Data:Source.
var temOracle = !string.IsNullOrWhiteSpace(builder.Configuration["Oracle:ConnectionString"]);
var dataSource = builder.Configuration["Data:Source"] ?? (temOracle ? "Oracle" : "Demo");
if (dataSource.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IReportDataSource>(_ =>
        new OracleReportDataSource(OracleOpts(builder.Configuration)));
else
    builder.Services.AddSingleton<IReportDataSource, DemoDataSource>();

builder.Services.AddSingleton<ReportService>();

var app = builder.Build();

// Log de diagnostico: mostra no console qual fonte de dados esta ativa.
app.Logger.LogInformation("Repositorio de reports: {ReportSource}", reportSource);
app.Logger.LogInformation("Fonte de dados ativa: {DataSource}", dataSource);

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
    // Caminho absoluto no config -> usa como esta (ex.: pasta externa em producao).
    // Senao, resolve contra a pasta do app (onde os arquivos de reports/ sao
    // copiados no build/publish), valendo igual em dev e em producao.
    var dir = b.Configuration["Reports:Directory"];
    if (!string.IsNullOrWhiteSpace(dir) && Path.IsPathRooted(dir))
        return dir;
    var sub = string.IsNullOrWhiteSpace(dir) ? "reports" : dir;
    return Path.Combine(AppContext.BaseDirectory, sub);
}

// DTO do corpo do POST
record GerarDto(string? Format, Dictionary<string, string?>? Parametros);
