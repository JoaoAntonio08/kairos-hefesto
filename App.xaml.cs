using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Hefesto;

public partial class App : Application
{
    // Uma única instância: abrir o Hefesto de novo só traz a janela que já está aberta para a frente.
    private static Mutex? _single;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    protected override void OnStartup(StartupEventArgs e)
    {
        // Modo de desenvolvimento: Hefesto.exe --captura <arquivo.png> [NavX] [claro]
        // Abre a janela, vai até a página do item de menu NavX, salva uma imagem dela e fecha. Usado para revisar o visual.
        int ci = Array.IndexOf(e.Args, "--captura");
        if (ci >= 0 && ci + 1 < e.Args.Length)
        {
            base.OnStartup(e);
            Captura.Rodar(e.Args[ci + 1], ci + 2 < e.Args.Length ? e.Args[ci + 2] : null, e.Args.Contains("claro"));
            return;
        }

        bool first;
        try { _single = new Mutex(true, @"Local\Hefesto.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; } // criado por outra instância com outro nível de permissão

        if (!first)
        {
            IntPtr h = FindWindow(null, "Hefesto");
            if (h != IntPtr.Zero)
            {
                if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                SetForegroundWindow(h);
            }
            Shutdown();
            return;
        }
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
