using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SensattaReports.Api.Components;
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

// Blazor Server — o painel /admin.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<SensattaReports.Api.Services.ToastService>();
builder.Services.AddSingleton<SensattaReports.Api.Services.CallLogService>();

// Autenticacao por cookie para o painel. Credenciais em Admin:User/Admin:Password.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login.html";
        o.AccessDeniedPath = "/login.html";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Arquivos estaticos (CSS do painel etc.) + antiforgery exigido pelo Blazor.
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Log de diagnostico: mostra no console qual fonte de dados esta ativa.
app.Logger.LogInformation("Repositorio de reports: {ReportSource}", reportSource);
app.Logger.LogInformation("Fonte de dados ativa: {DataSource}", dataSource);

// Verificacao de saude (o Protheus/monitoramento pode checar).
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

// Download direto (GET) — pratico para o Protheus via FWRest/HttpGet.
// Ex.: /reports/resumo-coletas-eua/pdf?data_ini=2026-10-01&data_fim=2026-10-31
app.MapGet("/reports/{codigo}/pdf", (string codigo, HttpContext ctx, ReportService svc, CallLogService log, CancellationToken ct)
    => Generate(codigo, ReportFormat.Pdf, QueryParams(ctx), svc, log, ct));

app.MapGet("/reports/{codigo}/xlsx", (string codigo, HttpContext ctx, ReportService svc, CallLogService log, CancellationToken ct)
    => Generate(codigo, ReportFormat.Xlsx, QueryParams(ctx), svc, log, ct));

// Alternativa por POST com corpo JSON: { "format": "pdf", "parametros": { "data_ini": "..." } }
app.MapPost("/reports/{codigo}", async (string codigo, GerarDto body, ReportService svc, CallLogService log, CancellationToken ct) =>
{
    var fmt = string.Equals(body.Format, "xlsx", StringComparison.OrdinalIgnoreCase)
        ? ReportFormat.Xlsx : ReportFormat.Pdf;
    return await Generate(codigo, fmt, body.Parametros ?? new(), svc, log, ct);
});

// ---------------------------------------------------------------------------
// Painel administrativo em Blazor Server (/admin). Os componentes chamam os
// servicos (IReportCatalog, ReportFileStore, ReportService) diretamente.
// IMPORTANTE: proteja /admin no nivel de rede. Desligavel com Admin:Enabled=false.
// ---------------------------------------------------------------------------
if (app.Configuration.GetValue("Admin:Enabled", true))
{
    // Login/logout por cookie. Credenciais em Admin:User / Admin:Password (config).
    app.MapPost("/auth/login", async (HttpContext http, IConfiguration cfg) =>
    {
        var form = await http.Request.ReadFormAsync();
        var user = form["user"].ToString();
        var pass = form["password"].ToString();
        var returnUrl = form["returnUrl"].ToString();

        var okUser = cfg["Admin:User"] ?? "admin";
        var okPass = cfg["Admin:Password"] ?? "admin";

        if (user == okUser && pass == okPass)
        {
            var id = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, user)],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id));

            // so redireciona para caminho local (evita open redirect)
            var dest = returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/admin";
            return Results.Redirect(dest);
        }
        return Results.Redirect("/login.html?erro=1");
    }).DisableAntiforgery();

    app.MapGet("/auth/logout", async (HttpContext http) =>
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login.html");
    });

    // o painel exige login
    app.MapRazorComponents<App>().AddInteractiveServerRenderMode().RequireAuthorization();
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
    IReadOnlyDictionary<string, string?> parametros, ReportService svc, CallLogService log, CancellationToken ct)
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
        Reg(false, 500, e.Message);
        return Results.Problem(e.Message);
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
