using System.Diagnostics;

namespace SensattaReports.Api;

/// <summary>
/// Permite o proprio .exe instalar/remover/controlar o Servico do Windows via
/// sc.exe. Acionado quando o exe roda com um argumento, ANTES de subir a API:
///
///   SensattaReports.Api.exe install     (cria o servico, auto-start, e inicia)
///   SensattaReports.Api.exe uninstall   (para e remove)
///   SensattaReports.Api.exe start | stop | restart
///
/// Rode num prompt como Administrador. So faz sentido no Windows.
/// </summary>
public static class ServiceInstaller
{
    public const string ServiceName = "SensattaReports";

    public static int Run(string cmd)
    {
        var exe = Environment.ProcessPath
                  ?? Path.Combine(AppContext.BaseDirectory, "SensattaReports.Api.exe");

        switch (cmd)
        {
            case "install":
                Sc($"create {ServiceName} binPath= \"{exe}\" start= auto");
                Sc($"description {ServiceName} \"Servico de relatorios Sensatta -> Protheus\"");
                // auto-reinicia em caso de falha (3 tentativas, 5s cada)
                Sc($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000");
                LockSecrets();
                Console.WriteLine($"Servico '{ServiceName}' instalado. Iniciando...");
                return Sc($"start {ServiceName}");

            case "uninstall":
                Sc($"stop {ServiceName}");
                Console.WriteLine($"Removendo o servico '{ServiceName}'...");
                return Sc($"delete {ServiceName}");

            case "start":
                return Sc($"start {ServiceName}");

            case "stop":
                return Sc($"stop {ServiceName}");

            case "restart":
                Sc($"stop {ServiceName}");
                Thread.Sleep(3000);
                return Sc($"start {ServiceName}");

            default:
                Console.WriteLine("Comandos: install | uninstall | start | stop | restart");
                return 1;
        }
    }

    /// <summary>
    /// Trava a permissao do appsettings.Local.json (segredos): so SYSTEM (a conta
    /// do servico) e o grupo Administradores podem ler. Usa SIDs conhecidos para
    /// funcionar em Windows de qualquer idioma (*S-1-5-18 = SYSTEM,
    /// *S-1-5-32-544 = Administradores).
    /// </summary>
    private static void LockSecrets()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");
        if (!File.Exists(local)) return;
        Exec("icacls", $"\"{local}\" /inheritance:r /grant:r *S-1-5-18:R *S-1-5-32-544:F");
        Console.WriteLine("Permissao do appsettings.Local.json travada (SYSTEM + Administradores).");
    }

    private static int Sc(string args) => Exec("sc.exe", args);

    private static int Exec(string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false });
        p!.WaitForExit();
        return p.ExitCode;
    }
}
