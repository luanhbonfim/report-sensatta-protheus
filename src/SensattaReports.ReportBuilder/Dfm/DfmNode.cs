namespace SensattaReports.ReportBuilder.Dfm;

/// <summary>
/// Um no da arvore DFM: um "object" do .rtm (o report, uma banda, um
/// elemento). Tem tipo, nome, propriedades e filhos.
/// </summary>
public sealed class DfmNode(string name, string type)
{
    public string Name { get; } = name;   // ex.: ppHeaderBand1
    public string Type { get; } = type;   // ex.: TppHeaderBand

    public Dictionary<string, DfmValue> Props { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<DfmNode> Children { get; } = [];

    // ---- acessores de propriedade com valor padrao --------------------
    public DfmValue? Get(string key) => Props.GetValueOrDefault(key);

    public string GetString(string key, string def = "") =>
        Props.TryGetValue(key, out var v) ? v.AsString() : def;

    public long GetInt(string key, long def = 0) =>
        Props.TryGetValue(key, out var v) ? v.AsInt() : def;

    public double GetDouble(string key, double def = 0) =>
        Props.TryGetValue(key, out var v) ? v.AsDouble() : def;

    public bool GetBool(string key, bool def = false) =>
        Props.TryGetValue(key, out var v) ? v.AsBool() : def;

    public bool HasFlag(string key, string flag) =>
        Props.TryGetValue(key, out var v) && v.Contains(flag);

    /// <summary>Filhos de um determinado tipo DFM.</summary>
    public IEnumerable<DfmNode> ChildrenOfType(string type) =>
        Children.Where(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase));
}
