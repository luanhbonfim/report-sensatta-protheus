namespace SensattaReports.Api.Services;

public enum ToastLevel { Info, Success, Error }

public record Toast(Guid Id, string Message, ToastLevel Level);

/// <summary>
/// Notificacoes "toast" do painel. Scoped por circuito Blazor. Os componentes
/// assinam OnChange e redesenham; cada toast some sozinho apos alguns segundos.
/// </summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = new();
    private readonly Lock _gate = new();

    public event Action? OnChange;

    public IReadOnlyList<Toast> Items
    {
        get { lock (_gate) return _toasts.ToList(); }
    }

    public void Info(string message) => Show(message, ToastLevel.Info);
    public void Success(string message) => Show(message, ToastLevel.Success);
    public void Error(string message) => Show(message, ToastLevel.Error);

    public void Show(string message, ToastLevel level = ToastLevel.Info)
    {
        var toast = new Toast(Guid.NewGuid(), message, level);
        lock (_gate) _toasts.Add(toast);
        OnChange?.Invoke();

        var timer = new System.Timers.Timer(4000) { AutoReset = false };
        timer.Elapsed += (_, _) => { Remove(toast.Id); timer.Dispose(); };
        timer.Start();
    }

    public void Remove(Guid id)
    {
        lock (_gate) _toasts.RemoveAll(t => t.Id == id);
        OnChange?.Invoke();
    }
}
