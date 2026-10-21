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

var app = builder.Build();

// Serve o painel (wwwroot/admin.html) e demais arquivos estaticos.
app.UseStaticFiles();

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

// ---------------------------------------------------------------------------
// Painel administrativo: HTML em wwwroot/admin.html + API abaixo.
// IMPORTANTE: proteja no nivel de rede — nao exponha publicamente. Pode
// desligar com Admin:Enabled=false no appsettings.
// ---------------------------------------------------------------------------
if (app.Configuration.GetValue("Admin:Enabled", true))
{
    app.MapGet("/admin", () => Results.Redirect("/admin.html"));

    // lista o catalogo (SIGMA.REPORT) + marca quais ja foram importados
    app.MapGet("/admin/api/catalog", async (IReportCatalog cat, ReportFileStore store, CancellationToken ct) =>
    {
        var nomes = await cat.ListAsync(ct);
        var importados = store.ListImported().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var itens = nomes.Select(n =>
        {
            var codigo = Slug.From(n);
            return new { name = n, codigo, imported = importados.Contains(codigo) };
        });
        return Results.Ok(itens);
    });

    // template (.rtm) de um report do catalogo
    app.MapGet("/admin/api/catalog/template", async (string name, IReportCatalog cat, CancellationToken ct) =>
    {
        var tpl = await cat.GetTemplateAsync(name, ct);
        return tpl is null
            ? Results.NotFound(new { error = "template nao encontrado" })
            : Results.Ok(new { name, template = tpl });
    });

    // detecta os binds do SQL e sugere parametros
    app.MapPost("/admin/api/detect-binds", (DetectDto body) =>
    {
        var binds = BindDetector.Detect(body.Sql);
        return Results.Ok(binds.Select(b => new
        {
            name = b,
            type = BindDetector.GuessType(b).ToString(),
            required = true,
            label = (string?)null
        }));
    });

    // report ja importado (para edicao)
    app.MapGet("/admin/api/reports/{codigo}", async (string codigo, IReportRepository repo, CancellationToken ct) =>
    {
        var def = await repo.GetByCodigoAsync(codigo, ct);
        return def is null
            ? Results.NotFound()
            : Results.Ok(new
            {
                def.Codigo,
                def.Sql,
                parametros = def.Parametros.Select(p => new { name = p.Name, type = p.Type.ToString(), required = p.Required, label = p.Label })
            });
    });

    // importa/atualiza: grava os arquivos em reports/<codigo>/
    app.MapPost("/admin/api/reports", async (ImportDto body, IReportCatalog cat, ReportFileStore store, IReportRepository repo, CancellationToken ct) =>
    {
        var codigo = Slug.From(body.Codigo ?? body.Name);
        if (codigo.Length == 0) return Results.BadRequest(new { error = "codigo/nome invalido" });
        if (string.IsNullOrWhiteSpace(body.Sql)) return Results.BadRequest(new { error = "SQL obrigatorio" });

        // 1) template enviado; 2) do catalogo pelo nome; 3) o que ja esta no disco (edicao)
        var rtm = body.Template;
        if (string.IsNullOrWhiteSpace(rtm) && !string.IsNullOrWhiteSpace(body.Name))
            rtm = await cat.GetTemplateAsync(body.Name, ct);
        if (string.IsNullOrWhiteSpace(rtm))
            rtm = (await repo.GetByCodigoAsync(codigo, ct))?.TemplateRtm;
        if (string.IsNullOrWhiteSpace(rtm)) return Results.BadRequest(new { error = "template (.rtm) nao encontrado" });

        var parametros = (body.Parametros ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new ReportParameter
            {
                Name = p.Name,
                Type = Enum.TryParse<ReportParameterType>(p.Type, true, out var t) ? t : ReportParameterType.String,
                Required = p.Required,
                Label = string.IsNullOrWhiteSpace(p.Label) ? null : p.Label
            })
            .ToList();

        await store.SaveAsync(codigo, body.Sql!, rtm!, parametros, ct);
        return Results.Ok(new { codigo, url = $"/reports/{codigo}/pdf" });
    });
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

// DTOs dos corpos JSON
record GerarDto(string? Format, Dictionary<string, string?>? Parametros);
record DetectDto(string? Sql);
record ParamDto(string Name, string? Type, bool Required, string? Label);
record ImportDto(string? Name, string? Codigo, string? Sql, string? Template, ParamDto[]? Parametros);
