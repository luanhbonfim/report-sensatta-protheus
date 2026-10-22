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

    private static int Sc(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", args) { UseShellExecute = false });
        p!.WaitForExit();
        return p.ExitCode;
    }
}
