using System.IO;
using System.Diagnostics;
using System.Windows;

namespace Hefesto;

/// <summary>Página do Hefesto feita como UserControl. Mostrar() é chamado sempre que o usuário abre a página no menu.</summary>
public interface IPagina
{
    void Mostrar();
}

/// <summary>Utilidades comuns das páginas novas do Hefesto.</summary>
public static class Ui
{
    public static void AbrirUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Não foi possível abrir:\n" + url + "\n\n" + ex.Message, "Hefesto"); }
    }

    public static void AbrirPasta(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>Tamanho legível: 1,2 GB, 340 MB, 12 KB.</summary>
    public static string Tam(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : v >= 100 ? $"{v:0} {u[i]}" : $"{v:0.#} {u[i]}";
    }

    public static bool Confirmar(string texto, string titulo = "Hefesto")
        => MessageBox.Show(Application.Current.MainWindow, texto, titulo, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static string Versao
    {
        get
        {
            var v = typeof(Ui).Assembly.GetName().Version;
            return v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>Pasta de documentos do Hefesto (relatórios, backups): Documentos\Hefesto\&lt;sub&gt;.</summary>
    public static string PastaDocumentos(string sub)
    {
        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hefesto", sub);
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Pasta de dados locais: %LocalAppData%\Hefesto\&lt;sub&gt;.</summary>
    public static string PastaLocal(string sub)
    {
        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hefesto", sub);
        Directory.CreateDirectory(p);
        return p;
    }
}
