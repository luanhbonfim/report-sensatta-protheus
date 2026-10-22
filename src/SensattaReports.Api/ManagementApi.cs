using SensattaReports.Api.Services;
using SensattaReports.Core;
using SensattaReports.Core.Abstractions;
using SensattaReports.Core.Models;
using SensattaReports.Data;
using SensattaReports.ReportBuilder.Model;

namespace SensattaReports.Api;

/// <summary>
/// API HTTP de gerenciamento (JSON), consumida por um painel externo.
/// Cobre o que o painel Blazor fazia em processo: catalogo, CRUD de reports,
/// layout (para o editor visual) e log de chamadas. Protegida por X-Api-Key.
/// As rotas de geracao (/reports/{cod}/pdf|xlsx) continuam no Program.cs.
/// </summary>
public static class ManagementApi
{
    public static void MapManagementApi(this WebApplication app, bool requireAuth)
    {
        var api = app.MapGroup("/api").DisableAntiforgery();
        // Quando o JWT esta habilitado, o painel precisa de token com role admin.
        if (requireAuth) api.RequireAuthorization(Auth.PolicyPanel);

        // ---- Catalogo (SIGMA.REPORT): lista nomes e marca os ja importados ----
        api.MapGet("/catalog", async (IReportCatalog catalog, ReportFileStore store) =>
        {
            var nomes = await catalog.ListAsync();
            var imp = store.ListImported().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var itens = nomes.Select(n =>
            {
                var cod = Slug.From(n);
                return new CatalogItemDto(n, cod, imp.Contains(cod));
            });
            return Results.Ok(itens);
        });

        // ---- Reports importados ----
        api.MapGet("/reports", async (ReportFileStore store, IReportRepository repo) =>
        {
            var lista = new List<ReportListItemDto>();
            foreach (var cod in store.ListImported())
            {
                var def = await repo.GetByCodigoAsync(cod);
                lista.Add(new ReportListItemDto(
                    cod,
                    store.GetName(cod) ?? cod,
                    def?.Parametros.Count ?? 0,
                    $"/reports/{cod}/pdf",
                    $"/reports/{cod}/xlsx"));
            }
            return Results.Ok(lista);
        });

        // ---- Definicao completa de um report (SQL + parametros) ----
        api.MapGet("/reports/{codigo}", async (string codigo, IReportRepository repo, ReportFileStore store) =>
        {
            var def = await repo.GetByCodigoAsync(codigo);
            if (def is null) return Results.NotFound(new { error = "Report não encontrado." });
            return Results.Ok(new ReportDetailDto(
                codigo,
                store.GetName(codigo) ?? codigo,
                def.Sql,
                def.Parametros.Select(ToParamDto).ToList()));
        });

        // ---- Salvar/importar um report (SQL + parametros). O .rtm vem do
        //      catalogo (na importacao) ou e mantido (na atualizacao). ----
        api.MapPost("/reports/{codigo}", async (string codigo, SaveReportDto body,
            IReportCatalog catalog, IReportRepository repo, ReportFileStore store) =>
        {
            if (string.IsNullOrWhiteSpace(body.Sql))
                return Results.BadRequest(new { error = "Informe o SQL." });

            var nome = string.IsNullOrWhiteSpace(body.Nome) ? codigo : body.Nome!;

            // .rtm: tenta o catalogo pelo nome; senao mantem o ja salvo.
            var rtm = await catalog.GetTemplateAsync(nome);
            if (string.IsNullOrWhiteSpace(rtm))
                rtm = (await repo.GetByCodigoAsync(codigo))?.TemplateRtm;
            if (string.IsNullOrWhiteSpace(rtm))
                return Results.BadRequest(new { error = "Template (.rtm) não encontrado no catálogo nem salvo." });

            var ps = (body.Parametros ?? new())
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new ReportParameter
                {
                    Name = p.Name,
                    Type = Enum.TryParse<ReportParameterType>(p.Type, true, out var t) ? t : ReportParameterType.String,
                    Required = p.Required,
                    Label = string.IsNullOrWhiteSpace(p.Label) ? null : p.Label
                })
                .ToList();

            await store.SaveAsync(codigo, nome, body.Sql, rtm!, ps);
            return Results.Ok(new { codigo, nome, endpointPdf = $"/reports/{codigo}/pdf" });
        });

        // ---- Layout do report (bandas + campos + ajustes) para o editor ----
        api.MapGet("/reports/{codigo}/layout", async (string codigo, IReportRepository repo, ReportFileStore store) =>
        {
            var def = await repo.GetByCodigoAsync(codigo);
            if (def is null) return Results.NotFound(new { error = "Report não encontrado." });

            var tpl = RbTemplateLoader.Load(def.TemplateRtm);
            var bands = Secoes(tpl).Select(s => new LayoutBandDto(
                s.Band.Kind.ToString(),
                s.Label,
                s.Band.HeightMm,
                s.Band.Elements.Select(ToElementDto).ToList())).ToList();

            var overrides = store.GetLayout(codigo).ToDictionary(kv => kv.Key, kv => ToOverrideDto(kv.Value));

            return Results.Ok(new LayoutDto(
                tpl.PageWidthMm, tpl.PageHeightMm, tpl.ContentWidthMm,
                tpl.MarginLeftMm, tpl.MarginTopMm,
                bands, overrides));
        });

        // ---- Salvar os ajustes de layout (report.layout.json) ----
        api.MapPut("/reports/{codigo}/layout", async (string codigo, SaveLayoutDto body, ReportFileStore store) =>
        {
            var ovr = (body.Overrides ?? new()).ToDictionary(
                kv => kv.Key,
                kv => new ElementOverride
                {
                    LeftMm = kv.Value.LeftMm,
                    TopMm = kv.Value.TopMm,
                    WidthMm = kv.Value.WidthMm,
                    HeightMm = kv.Value.HeightMm,
                    Hidden = kv.Value.Hidden
                },
                StringComparer.OrdinalIgnoreCase);
            await store.SaveLayoutAsync(codigo, ovr);
            return Results.Ok(new { codigo, campos = ovr.Count });
        });

        // ---- Log das chamadas do Protheus + indicadores ----
        api.MapGet("/calls", (CallLogService calls, int? take) =>
        {
            var recent = calls.Recent(take ?? 200).Select(c => new CallDto(
                c.When, c.Codigo, c.Format, c.Ok, c.Status, c.Ms, c.Query, c.Error));
            return Results.Ok(new CallsDto(calls.Total, calls.Hoje, calls.Erros, recent.ToList()));
        });
    }

    // ---- helpers ----
    private record Secao(string Label, RbBand Band);

    private static IEnumerable<Secao> Secoes(RbTemplate tpl)
    {
        if (tpl.TitleBand is { } t) yield return new("Título", t);
        if (tpl.HeaderBand is { } h) yield return new("Cabeçalho da página", h);
        foreach (var g in tpl.Groups)
            if (g.Header is { } gh) yield return new($"Grupo \"{g.BreakField}\" — cabeçalho", gh);
        if (tpl.DetailBand is { } d) yield return new("Detalhe", d);
        foreach (var g in tpl.Groups)
            if (g.Footer is { Elements.Count: > 0 } gf) yield return new($"Grupo \"{g.BreakField}\" — rodapé", gf);
        if (tpl.FooterBand is { } f) yield return new("Rodapé da página", f);
        if (tpl.SummaryBand is { } s) yield return new("Resumo", s);
    }

    private static ParamDto ToParamDto(ReportParameter p)
        => new(p.Name, p.Type.ToString(), p.Required, p.Label);

    private static OverrideDto ToOverrideDto(ElementOverride o)
        => new(o.LeftMm, o.TopMm, o.WidthMm, o.HeightMm, o.Hidden);

    private static LayoutElementDto ToElementDto(RbElement el) => new(
        el.Name,
        el.GetType().Name.Replace("Rb", ""),
        el.LeftMm, el.TopMm, el.WidthMm, el.HeightMm,
        (el as RbLabel)?.Text,
        el switch { RbDbText d => d.DataField, RbDbMemo m => m.DataField, _ => null },
        (el as RbSystemVariable)?.VarType.ToString());
}

// ---- DTOs ----
public record CatalogItemDto(string Name, string Codigo, bool Imported);
public record ReportListItemDto(string Codigo, string Nome, int Params, string EndpointPdf, string EndpointXlsx);
public record ParamDto(string Name, string Type, bool Required, string? Label);
public record ReportDetailDto(string Codigo, string Nome, string Sql, List<ParamDto> Parametros);
public record SaveReportDto(string? Nome, string Sql, List<ParamDto>? Parametros);
public record OverrideDto(double? LeftMm, double? TopMm, double? WidthMm, double? HeightMm, bool Hidden);
public record LayoutElementDto(string Name, string Type, double LeftMm, double TopMm, double WidthMm, double HeightMm, string? Text, string? Field, string? VarType);
public record LayoutBandDto(string Kind, string Label, double HeightMm, List<LayoutElementDto> Elements);
public record LayoutDto(double PageWidthMm, double PageHeightMm, double ContentWidthMm, double MarginLeftMm, double MarginTopMm, List<LayoutBandDto> Bands, Dictionary<string, OverrideDto> Overrides);
public record SaveLayoutDto(Dictionary<string, OverrideDto>? Overrides);
public record CallDto(DateTime When, string Codigo, string Format, bool Ok, int Status, long Ms, string? Query, string? Error);
public record CallsDto(int Total, int Hoje, int Erros, List<CallDto> Recent);
