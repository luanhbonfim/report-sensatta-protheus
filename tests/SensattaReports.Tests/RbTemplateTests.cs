using SensattaReports.ReportBuilder.Dfm;
using SensattaReports.ReportBuilder.Model;
using Xunit;

namespace SensattaReports.Tests;

/// <summary>
/// Testa o parser e o loader contra o .rtm de exemplo (copiado para a saida
/// do build). Garante que a leitura do template continua correta.
/// </summary>
public class RbTemplateTests
{
    private static string SampleRtm()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "reports", "resumo-coletas-eua", "report.rtm");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Parser_le_a_raiz_e_as_bandas()
    {
        var root = DfmParser.Parse(SampleRtm());

        Assert.Equal("TppReport", root.Type);
        Assert.Contains(root.Children, c => c.Type == "TppHeaderBand");
        Assert.Contains(root.Children, c => c.Type == "TppDetailBand");
        Assert.Contains(root.Children, c => c.Type == "TppGroup");
    }

    [Fact]
    public void Loader_le_papel_e_orientacao()
    {
        var tpl = RbTemplateLoader.Load(SampleRtm());

        Assert.True(tpl.Landscape);
        Assert.Equal(297, tpl.PageWidthMm, precision: 0);
        Assert.Equal(210, tpl.PageHeightMm, precision: 0);
    }

    [Fact]
    public void Loader_le_o_grupo_e_a_quebra()
    {
        var tpl = RbTemplateLoader.Load(SampleRtm());

        Assert.Single(tpl.Groups);
        Assert.Equal("CODIGO_PRODUTO", tpl.Groups[0].BreakField);
        Assert.NotNull(tpl.Groups[0].Header);
    }

    [Fact]
    public void Loader_le_os_campos_do_detalhe()
    {
        var tpl = RbTemplateLoader.Load(SampleRtm());

        Assert.NotNull(tpl.DetailBand);
        var campos = tpl.DetailBand!.Elements.OfType<RbDbText>().Select(d => d.DataField).ToList();

        Assert.Contains("NUMERO_CAIXA", campos);
        Assert.Contains("CONTRA_MARCA", campos);
        Assert.Contains("LOTE_EUA", campos);
        Assert.Contains("DATA_GERADA", campos);
        Assert.Contains("DATA_COLETA", campos);
        Assert.Contains("DATA_ULTIMA_PESAGEM", campos);
    }

    [Fact]
    public void Loader_extrai_o_jpeg_da_imagem()
    {
        var tpl = RbTemplateLoader.Load(SampleRtm());
        var img = tpl.HeaderBand!.Elements.OfType<RbImage>().FirstOrDefault();

        Assert.NotNull(img);
        Assert.True(img!.HasImage);
        // JPEG comeca com FFD8
        Assert.Equal(0xFF, img.Jpeg[0]);
        Assert.Equal(0xD8, img.Jpeg[1]);
    }

    [Fact]
    public void Label_do_titulo_tem_fonte_negrito_e_alinhamento()
    {
        var tpl = RbTemplateLoader.Load(SampleRtm());
        var titulo = tpl.HeaderBand!.Elements.OfType<RbLabel>()
            .FirstOrDefault(l => l.Text == "RESUMO DE COLETAS");

        Assert.NotNull(titulo);
        Assert.True(titulo!.Font.Bold);
        Assert.Equal(12, titulo.Font.SizePt, precision: 0);
        Assert.Equal(RbAlignment.Right, titulo.Alignment);
    }

    // ---- Relatorio de Embarque V2: 3 grupos, rodapes com subtotais, memo e variaveis ----

    private static string EmbarqueRtm()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "reports", "romaneio-embarque-v2", "report.rtm");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Embarque_tem_tres_grupos_na_ordem_de_quebra()
    {
        var tpl = RbTemplateLoader.Load(EmbarqueRtm());

        var quebras = tpl.Groups.Select(g => g.BreakField).ToList();
        Assert.Equal(new[] { "SEQUENCIAL_ROMANEIO", "SEQUENCIAL_PEDIDO", "CODIGO_PRODUTO" }, quebras);
    }

    [Fact]
    public void Embarque_grupo_do_pedido_quebra_pagina()
    {
        var tpl = RbTemplateLoader.Load(EmbarqueRtm());

        var pedido = tpl.Groups.Single(g => g.BreakField == "SEQUENCIAL_PEDIDO");
        Assert.True(pedido.NewPage);
        // os demais grupos nao quebram pagina
        Assert.False(tpl.Groups.Single(g => g.BreakField == "CODIGO_PRODUTO").NewPage);
    }

    [Fact]
    public void Embarque_rodape_do_pedido_tem_os_subtotais()
    {
        var tpl = RbTemplateLoader.Load(EmbarqueRtm());

        var footer = tpl.Groups.Single(g => g.BreakField == "SEQUENCIAL_PEDIDO").Footer;
        Assert.NotNull(footer);
        var campos = footer!.Elements.OfType<RbDbText>().Select(d => d.DataField).ToList();

        Assert.Contains("TOTAL_PEDIDO_QTDE_COMPOSICAO", campos);
        Assert.Contains("TOTAL_PEDIDO_PESO_LIQUIDO", campos);
        Assert.Contains("TOTAL_PEDIDO_PESO_BRUTO", campos);
        Assert.Contains("TOTAL_PEDIDO_MEDIA_PESO", campos);
    }

    [Fact]
    public void Embarque_rodape_do_produto_tem_totais_e_e_dinamico()
    {
        var tpl = RbTemplateLoader.Load(EmbarqueRtm());

        var footer = tpl.Groups.Single(g => g.BreakField == "CODIGO_PRODUTO").Footer;
        Assert.NotNull(footer);
        Assert.True(footer!.Dynamic); // PrintHeight = phDynamic
        var campos = footer.Elements.OfType<RbDbText>().Select(d => d.DataField).ToList();

        Assert.Contains("QTDE_COMPOSICAO", campos);
        Assert.Contains("PESO_LIQUIDO", campos);
        Assert.Contains("PESO_BRUTO", campos);
        Assert.Contains("MEDIA_PESO", campos);
    }

    [Fact]
    public void Embarque_detalhe_tem_memo_esticado()
    {
        var tpl = RbTemplateLoader.Load(EmbarqueRtm());

        Assert.NotNull(tpl.DetailBand);
        Assert.True(tpl.DetailBand!.Dynamic);
        var memo = tpl.DetailBand.Elements.OfType<RbDbMemo>().FirstOrDefault();

        Assert.NotNull(memo);
        Assert.Equal("TST", memo!.DataField);
        Assert.True(memo.Stretch);
    }

    [Fact]
    public void Embarque_rodape_tem_data_e_numero_de_pagina()
    {
        var tpl = RbTemplateLoader.Load(EmbarqueRtm());

        Assert.NotNull(tpl.FooterBand);
        var tipos = tpl.FooterBand!.Elements.OfType<RbSystemVariable>().Select(v => v.VarType).ToList();

        Assert.Contains(RbSysVarType.DateTime, tipos);
        Assert.Contains(RbSysVarType.PageSetDesc, tipos);
    }
}
