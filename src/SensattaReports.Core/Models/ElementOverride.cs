namespace SensattaReports.Core.Models;

/// <summary>
/// Ajuste de layout de um campo do .rtm, feito pelo editor visual do painel.
/// Guardado por nome do componente (UserName) em report.layout.json e aplicado
/// na renderizacao, sem alterar o .rtm original. Cada medida e opcional (em mm):
/// null = mantem o valor do template.
/// </summary>
public sealed class ElementOverride
{
    public double? LeftMm { get; init; }
    public double? TopMm { get; init; }
    public double? WidthMm { get; init; }
    public double? HeightMm { get; init; }
    /// <summary>true = oculta o campo na renderizacao.</summary>
    public bool Hidden { get; init; }
}
