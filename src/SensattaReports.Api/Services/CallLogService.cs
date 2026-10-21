namespace SensattaReports.Api.Services;

public record CallEntry(
    DateTime When, string Codigo, string Format, bool Ok, int Status, long Ms, string? Query, string? Error);

/// <summary>
/// Registro em memoria das ultimas chamadas aos relatorios (as que o Protheus
/// faz). Singleton; mantem um buffer das N mais recentes. Reinicia com o servico.
/// </summary>
public sealed class CallLogService
{
    private const int Max = 500;
    private readonly LinkedList<CallEntry> _entries = new();
    private readonly Lock _gate = new();

    public void Record(CallEntry e)
    {
        lock (_gate)
        {
            _entries.AddFirst(e);
            while (_entries.Count > Max) _entries.RemoveLast();
        }
    }

    public IReadOnlyList<CallEntry> Recent(int take = 100)
    {
        lock (_gate) return _entries.Take(take).ToList();
    }

    public int Total { get { lock (_gate) return _entries.Count; } }
    public int Hoje { get { lock (_gate) return _entries.Count(e => e.When.Date == DateTime.Now.Date); } }
    public int Erros { get { lock (_gate) return _entries.Count(e => !e.Ok); } }
}
