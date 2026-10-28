using System.Net;
using System.Text;
using SensattaReports.Core;
using SensattaReports.Core.Models;
using SensattaReports.Data;

namespace SensattaReports.Api;

/// <summary>
/// "Modo TV": paginas HTML somente-leitura para pendurar num monitor/TV. Mostram
/// os dados de um report numa tabela grande, com tema escuro, atualizacao
/// automatica e rolagem automatica quando nao cabe na tela.
///
/// Rotas (liberadas na rede interna — NAO exigem JWT, so leem/rodam o SELECT):
///   GET /tv                 -> indice com os reports disponiveis
///   GET /tv/{codigo}        -> tela do report (passe os parametros na query)
///
/// Parametros de controle da tela (separados dos parametros do report):
///   ?refresh=N   segundos entre atualizacoes (padrao 60; 0 = nao atualiza)
///   ?scroll=0    desliga a rolagem automatica (padrao ligada quando transborda)
/// </summary>
public static class TvEndpoints
{
    // Chaves reservadas: controlam a tela, nao sao parametros do report.
    private static readonly HashSet<string> Reservadas =
        new(StringComparer.OrdinalIgnoreCase) { "refresh", "scroll" };

    public static void MapTvMode(this WebApplication app)
    {
        var tv = app.MapGroup("/tv").WithTags("Modo TV").ExcludeFromDescription();

        // ---- Indice: lista os reports com link para a tela de cada um ----
        tv.MapGet("", (ReportFileStore store) =>
        {
            var itens = store.ListImported()
                .Select(cod => (Codigo: cod, Nome: store.GetName(cod) ?? cod))
                .OrderBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return Results.Content(IndexHtml(itens), "text/html; charset=utf-8");
        });

        // ---- Tela de um report ----
        tv.MapGet("/{codigo}", async (string codigo, HttpContext ctx, ReportService svc, CancellationToken ct) =>
        {
            // segundos entre atualizacoes (0 = desliga); padrao 60, minimo 5
            var refresh = 60;
            if (int.TryParse(ctx.Request.Query["refresh"], out var r))
                refresh = r <= 0 ? 0 : Math.Max(5, r);
            var autoScroll = ctx.Request.Query["scroll"] != "0";

            // parametros do report = tudo na query menos as chaves reservadas
            var parametros = ctx.Request.Query
                .Where(kv => !Reservadas.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);

            try
            {
                var req = new ReportRequest { Codigo = codigo, Parametros = parametros };
                var result = await svc.GetDataAsync(req, ct);
                var html = ScreenHtml(result.Definition, result.Data, parametros, refresh, autoScroll);
                return Results.Content(html, "text/html; charset=utf-8");
            }
            catch (ReportNotFoundException)
            {
                return Results.Content(ErrorHtml("Report não encontrado",
                    $"Não existe um report com o código “{WebUtility.HtmlEncode(codigo)}”.", refresh),
                    "text/html; charset=utf-8", null, 404);
            }
            catch (ReportParameterException e)
            {
                return Results.Content(ErrorHtml("Parâmetro inválido", WebUtility.HtmlEncode(e.Message), refresh),
                    "text/html; charset=utf-8", null, 400);
            }
            catch (Exception e)
            {
                // detalhe completo no log do servico; na tela so uma mensagem curta
                Console.Error.WriteLine($"[TV] {codigo} falhou: {e}");
                return Results.Content(ErrorHtml("Erro ao carregar os dados",
                    "Não foi possível montar a tela agora. A página tenta de novo automaticamente.", refresh),
                    "text/html; charset=utf-8", null, 500);
            }
        });
    }

    // ---------------------------------------------------------------------
    // Montagem do HTML
    // ---------------------------------------------------------------------

    private static string ScreenHtml(
        ReportDefinition def, ReportDataSet data,
        IReadOnlyDictionary<string, string?> parametros, int refresh, bool autoScroll)
    {
        var cols = data.Columns;
        // alinhamento por coluna: numero -> direita
        var numeric = new bool[cols.Count];
        for (var c = 0; c < cols.Count; c++)
        {
            var temValor = false; var soNumero = true;
            foreach (var row in data.Rows)
            {
                var v = data.GetValue(row, cols[c]);
                if (v is null) continue;
                temValor = true;
                if (v is not (decimal or double or float or int or long or short or byte)) { soNumero = false; break; }
            }
            numeric[c] = temValor && soNumero;
        }

        var sb = new StringBuilder();
        sb.Append("<table><thead><tr>");
        foreach (var col in cols)
            sb.Append("<th>").Append(WebUtility.HtmlEncode(col)).Append("</th>");
        sb.Append("</tr></thead><tbody>");

        if (data.Rows.Count == 0)
        {
            sb.Append("<tr><td class=\"empty\" colspan=\"").Append(Math.Max(1, cols.Count))
              .Append("\">Sem dados para os parâmetros informados.</td></tr>");
        }
        else
        {
            foreach (var row in data.Rows)
            {
                sb.Append("<tr>");
                for (var c = 0; c < cols.Count; c++)
                {
                    var txt = ReportDataSet.Format(data.GetValue(row, cols[c]));
                    sb.Append(numeric[c] ? "<td class=\"num\">" : "<td>")
                      .Append(WebUtility.HtmlEncode(txt)).Append("</td>");
                }
                sb.Append("</tr>");
            }
        }
        sb.Append("</tbody></table>");

        var chips = new StringBuilder();
        foreach (var kv in parametros)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            chips.Append("<span class=\"chip\"><b>")
                 .Append(WebUtility.HtmlEncode(kv.Key)).Append("</b> ")
                 .Append(WebUtility.HtmlEncode(kv.Value)).Append("</span>");
        }

        var titulo = WebUtility.HtmlEncode(def.Nome);
        var totalLinhas = data.Rows.Count;

        return Page(titulo, refresh, $$"""
          <header>
            <div class="title">
              <h1>{{titulo}}</h1>
              <div class="chips">{{chips}}</div>
            </div>
            <div class="meta">
              <div class="clock" id="clock"></div>
              <div class="sub">
                <span>{{totalLinhas}} linha(s)</span>
                <span id="updated"></span>
                <span id="next"></span>
              </div>
            </div>
          </header>
          <main id="scroll"><div id="content">{{sb}}</div></main>
        """, autoScroll);
    }

    private static string IndexHtml(List<(string Codigo, string Nome)> itens)
    {
        var lista = new StringBuilder();
        if (itens.Count == 0)
            lista.Append("<p class=\"empty\">Nenhum report importado ainda.</p>");
        else
        {
            lista.Append("<ul class=\"cards\">");
            foreach (var it in itens)
                lista.Append("<li><a href=\"/tv/").Append(WebUtility.HtmlEncode(it.Codigo)).Append("\">")
                     .Append("<span class=\"nome\">").Append(WebUtility.HtmlEncode(it.Nome)).Append("</span>")
                     .Append("<span class=\"cod\">").Append(WebUtility.HtmlEncode(it.Codigo)).Append("</span>")
                     .Append("</a></li>");
            lista.Append("</ul>");
        }

        return Page("Modo TV — Sensatta Reports", 0, $$"""
          <header>
            <div class="title"><h1>Modo TV</h1>
              <div class="chips"><span class="chip">visualização somente-leitura</span></div>
            </div>
            <div class="meta"><div class="clock" id="clock"></div></div>
          </header>
          <main id="scroll"><div id="content">
            <p class="hint">Escolha um report. Os parâmetros vão na URL
              (ex.: <code>/tv/romaneio-embarque-v2?romaneio_inicial=1&amp;romaneio_final=10</code>).
              Controle a tela com <code>?refresh=N</code> (segundos) e <code>?scroll=0</code>.</p>
            {{lista}}
          </div></main>
        """, autoScroll: false);
    }

    private static string ErrorHtml(string titulo, string msg, int refresh)
        => Page(WebUtility.HtmlEncode(titulo), refresh, $$"""
          <header><div class="title"><h1>{{WebUtility.HtmlEncode(titulo)}}</h1></div>
            <div class="meta"><div class="clock" id="clock"></div></div></header>
          <main id="scroll"><div id="content"><p class="erro">{{msg}}</p></div></main>
        """, autoScroll: false);

    /// <summary>Casca comum: tema escuro, fontes grandes, relogio, auto-refresh e auto-scroll.</summary>
    private static string Page(string titulo, int refresh, string body, bool autoScroll)
    {
        var refreshMs = refresh > 0 ? refresh * 1000 : 0;
        return $$"""
<!doctype html>
<html lang="pt-br">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>{{titulo}}</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  html, body { height: 100%; margin: 0; }
  body {
    background: #0b0f14; color: #e8eef6;
    font-family: system-ui, "Segoe UI", Roboto, Arial, sans-serif;
    display: flex; flex-direction: column; height: 100vh; overflow: hidden;
  }
  header {
    display: flex; align-items: flex-start; justify-content: space-between;
    gap: 1rem; padding: clamp(.6rem, 1.5vw, 1.4rem) clamp(.8rem, 2vw, 2rem);
    background: #111826; border-bottom: 3px solid #1f6feb; flex: none;
  }
  header h1 { margin: 0; font-size: clamp(1.4rem, 2.6vw, 2.6rem); font-weight: 700; }
  .chips { margin-top: .5rem; display: flex; flex-wrap: wrap; gap: .4rem; }
  .chip {
    background: #1b2838; border: 1px solid #2b3b52; border-radius: 999px;
    padding: .2rem .7rem; font-size: clamp(.8rem, 1.3vw, 1.1rem); color: #b7c6da;
  }
  .chip b { color: #e8eef6; }
  .meta { text-align: right; white-space: nowrap; }
  .clock { font-size: clamp(1.4rem, 2.6vw, 2.6rem); font-variant-numeric: tabular-nums; font-weight: 700; }
  .sub { margin-top: .4rem; display: flex; gap: 1rem; justify-content: flex-end;
         color: #9fb0c6; font-size: clamp(.8rem, 1.3vw, 1.1rem); }
  main { flex: 1; overflow: hidden; position: relative; }
  #content { padding: 0 clamp(.5rem, 1.5vw, 1.5rem); }
  table { width: 100%; border-collapse: collapse; }
  thead th {
    position: sticky; top: 0; z-index: 2; background: #16202e; color: #cfe0f5;
    text-align: left; font-size: clamp(.9rem, 1.5vw, 1.4rem);
    padding: clamp(.4rem, 1vw, .9rem) clamp(.5rem, 1vw, 1rem);
    border-bottom: 2px solid #2b3b52;
  }
  tbody td {
    font-size: clamp(.85rem, 1.5vw, 1.4rem);
    padding: clamp(.35rem, .9vw, .8rem) clamp(.5rem, 1vw, 1rem);
    border-bottom: 1px solid #1c2735;
  }
  tbody tr:nth-child(even) { background: #0f1621; }
  td.num { text-align: right; font-variant-numeric: tabular-nums; }
  td.empty, .empty { text-align: center; color: #9fb0c6; padding: 2rem; font-size: 1.3rem; }
  .erro { color: #ffb4a9; font-size: clamp(1rem, 2vw, 1.6rem); padding: 1.5rem; }
  .hint { color: #9fb0c6; font-size: clamp(.9rem, 1.4vw, 1.2rem); padding: .5rem 0 1rem; }
  code { background: #16202e; padding: .1rem .4rem; border-radius: 4px; color: #cfe0f5; }
  .cards { list-style: none; padding: 0; margin: .5rem 0; display: grid;
           grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: .8rem; }
  .cards a { display: flex; flex-direction: column; gap: .3rem; text-decoration: none;
             background: #111826; border: 1px solid #2b3b52; border-radius: 10px;
             padding: 1rem 1.2rem; color: #e8eef6; }
  .cards a:hover { border-color: #1f6feb; }
  .cards .nome { font-size: 1.2rem; font-weight: 600; }
  .cards .cod { color: #9fb0c6; font-size: .95rem; }
</style>
</head>
<body>
{{body}}
<script>
  var REFRESH_MS = {{refreshMs}};
  var AUTO_SCROLL = {{(autoScroll ? "true" : "false")}};

  function pad(n){ return (n < 10 ? "0" : "") + n; }
  function tick(){
    var d = new Date();
    var c = document.getElementById("clock");
    if (c) c.textContent = pad(d.getHours()) + ":" + pad(d.getMinutes()) + ":" + pad(d.getSeconds());
  }
  setInterval(tick, 1000); tick();

  var upd = document.getElementById("updated");
  if (upd){ var n = new Date(); upd.textContent = "Atualizado " + pad(n.getHours()) + ":" + pad(n.getMinutes()); }

  // auto-refresh: recarrega a pagina (re-roda o SELECT no servidor)
  if (REFRESH_MS > 0){
    var left = REFRESH_MS / 1000;
    var nx = document.getElementById("next");
    function cd(){ if (nx) nx.textContent = "atualiza em " + left + "s"; left--; }
    cd(); setInterval(cd, 1000);
    setTimeout(function(){ location.reload(); }, REFRESH_MS);
  }

  // auto-scroll: quando o conteudo nao cabe, rola devagar e volta ao topo
  (function(){
    if (!AUTO_SCROLL) return;
    var box = document.getElementById("scroll");
    if (!box) return;
    function start(){
      if (box.scrollHeight <= box.clientHeight + 4) return; // cabe: nao rola
      var dir = 1, paused = 4000;
      setTimeout(function loop(){
        var step = function(){
          box.scrollTop += dir;
          var fim = box.scrollTop + box.clientHeight >= box.scrollHeight - 1;
          var topo = box.scrollTop <= 0;
          if ((dir > 0 && fim) || (dir < 0 && topo)){
            dir = -dir;
            setTimeout(function(){ requestAnimationFrame(step); }, paused);
          } else {
            setTimeout(function(){ requestAnimationFrame(step); }, 40);
          }
        };
        requestAnimationFrame(step);
      }, paused);
    }
    window.addEventListener("load", start);
  })();
</script>
</body>
</html>
""";
    }
}
