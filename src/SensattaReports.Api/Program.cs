using System.Diagnostics;
using SensattaReports.Api;
using SensattaReports.Api.Services;
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

// --- Catalogo (SIGMA.REPORT) + armazenamento dos arquivos de report -------
// O catalogo so e usado pelo painel ao importar/atualizar. Em dev (sem Oracle)
// usa o DemoReportCatalog, que lista os reports ja presentes como arquivos.
if (temOracle)
    builder.Services.AddSingleton<IReportCatalog>(_ =>
        new OracleReportCatalog(OracleOpts(builder.Configuration)));
else
    builder.Services.AddSingleton<IReportCatalog>(_ =>
        new DemoReportCatalog(ResolveDir(builder)));

builder.Services.AddSingleton(new ReportFileStore(ResolveDir(builder)));

// Limite de relatorios pesados simultaneos (protege CPU/memoria).
builder.Services.AddSingleton(new ReportServiceOptions
{
    MaxConcurrent = builder.Configuration.GetValue("Reports:MaxConcurrent", 3)
});
builder.Services.AddSingleton<ReportService>();

// Log em memoria das chamadas (exposto em GET /api/calls).
builder.Services.AddSingleton<SensattaReports.Api.Services.CallLogService>();

// Documentacao OpenAPI (gera /openapi/v1.json; a pagina /docs consome isso).
builder.Services.AddOpenApi();

// Autenticacao: JWT Bearer para os consumidores (Protheus e painel). So e
// exigido quando Jwt:Secret esta configurado (sem ele, dev aberto).
var jwtOn = Auth.IsEnabled(builder.Configuration);
var authBuilder = builder.Services.AddAuthentication();
if (jwtOn) authBuilder.AddReportsJwt(builder.Configuration);
builder.Services.AddReportsPolicies();

// CORS: permite o painel (outro domonio) chamar a API pelo navegador. As origens
// vem de Cors:Origins (array). Sem origens configuradas, libera qualquer origem
// (pratico em dev; em producao defina as origens do painel).
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (corsOrigins.Length > 0) p.WithOrigins(corsOrigins);
    else p.AllowAnyOrigin();
    p.AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Log de diagnostico: mostra no console qual fonte de dados esta ativa.
app.Logger.LogInformation("Repositorio de reports: {ReportSource}", reportSource);
app.Logger.LogInformation("Fonte de dados ativa: {DataSource}", dataSource);
app.Logger.LogInformation("Autenticacao JWT: {Estado}",
    jwtOn ? "HABILITADA (token obrigatorio)" : "desligada (dev, sem Jwt:Secret)");

// Verificacao de saude (o Protheus/monitoramento pode checar). Sempre publica.
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

// Documentacao da API: /openapi/v1.json (spec) + /docs (pagina interativa).
app.MapOpenApi();
app.MapGet("/docs", () => Results.Content("""
<!doctype html>
<html lang="pt-br">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Sensatta Reports — API</title>
</head>
<body>
  <script id="api-reference" data-url="/openapi/v1.json"></script>
  <script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference"></script>
</body>
</html>
""", "text/html"));

// Emissao de token JWT (fluxo client-credentials) para Protheus e painel.
app.MapTokenEndpoint();

// API HTTP de gerenciamento (JSON) consumida pelo painel externo.
// Exige JWT com role admin quando o JWT esta habilitado.
app.MapManagementApi(jwtOn);

// Geracao de relatorio (GET pratico p/ Protheus via FWRest/HttpGet).
// Ex.: /reports/resumo-coletas-eua/pdf?data_ini=2026-10-01&data_fim=2026-10-31
var pdf = app.MapGet("/reports/{codigo}/pdf", (string codigo, HttpContext ctx, ReportService svc, CallLogService log, CancellationToken ct)
    => Generate(codigo, ReportFormat.Pdf, QueryParams(ctx), ctx, svc, log, ct));

var xlsx = app.MapGet("/reports/{codigo}/xlsx", (string codigo, HttpContext ctx, ReportService svc, CallLogService log, CancellationToken ct)
    => Generate(codigo, ReportFormat.Xlsx, QueryParams(ctx), ctx, svc, log, ct));

// Alternativa por POST com corpo JSON: { "format": "pdf", "parametros": { "data_ini": "..." } }
var post = app.MapPost("/reports/{codigo}", async (string codigo, GerarDto body, HttpContext ctx, ReportService svc, CallLogService log, CancellationToken ct) =>
{
    var fmt = string.Equals(body.Format, "xlsx", StringComparison.OrdinalIgnoreCase)
        ? ReportFormat.Xlsx : ReportFormat.Pdf;
    return await Generate(codigo, fmt, body.Parametros ?? new(), ctx, svc, log, ct);
});

pdf.WithTags("Geração");
xlsx.WithTags("Geração");
post.WithTags("Geração");

// Exige JWT nas rotas de geracao quando habilitado.
if (jwtOn)
{
    pdf.RequireAuthorization(Auth.PolicyReports);
    xlsx.RequireAuthorization(Auth.PolicyReports);
    post.RequireAuthorization(Auth.PolicyReports);
}

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
    IReadOnlyDictionary<string, string?> parametros, HttpContext ctx, ReportService svc, CallLogService log, CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    var query = string.Join("&", parametros
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .Select(kv => $"{kv.Key}={kv.Value}"));

    void Reg(bool ok, int status, string? err)
        => log.Record(new CallEntry(DateTime.Now, codigo, fmt.ToString(), ok, status, sw.ElapsedMilliseconds, query, err));

    try
    {
        var req = new ReportRequest { Codigo = codigo, Format = fmt, Parametros = parametros };
        var output = await svc.GenerateAsync(req, ct);
        Reg(true, 200, null);
        // PDF: inline (abre no navegador e funciona na pré-visualização em iframe);
        // Excel: baixa como arquivo.
        if (fmt == ReportFormat.Pdf)
        {
            ctx.Response.Headers.ContentDisposition = $"inline; filename=\"{output.FileName}\"";
            return Results.File(output.Content, output.ContentType);
        }
        return Results.File(output.Content, output.ContentType, output.FileName);
    }
    catch (ReportNotFoundException e)
    {
        Reg(false, 404, e.Message);
        return Results.NotFound(new { error = e.Message });
    }
    catch (ReportParameterException e)
    {
        Reg(false, 400, e.Message);
        return Results.BadRequest(new { error = e.Message });
    }
    catch (Exception e)
    {
        // loga a excecao completa (tipo + stack + inner) no console do servico,
        // para diagnosticar erros de SQL/Oracle/renderizacao
        Console.Error.WriteLine($"[Generate] {codigo} ({fmt}) falhou: {e}");
        Reg(false, 500, $"{e.GetType().Name}: {e.Message}");
        return Results.Problem(
            detail: e.ToString(),
            title: $"{e.GetType().Name} ao gerar '{codigo}'",
            statusCode: 500);
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

// DTO do corpo do POST /reports/{codigo}
record GerarDto(string? Format, Dictionary<string, string?>? Parametros);
