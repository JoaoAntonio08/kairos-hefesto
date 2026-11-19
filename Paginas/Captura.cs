using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hefesto;

/// <summary>Modo de desenvolvimento (--captura): renderiza a janela principal numa página e salva como PNG.</summary>
public static class Captura
{
    public static void Rodar(string arquivo, string? nav, bool claro)
    {
        var w = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, ShowActivated = false };
        w.Show();
        if (claro) ThemeManager.Apply(true);
        if (nav != null && w.FindName(nav) is RadioButton rb) rb.IsChecked = true;

        // "acao:<Metodo_Click>" aciona um botão da página aberta (só ações sem efeito permanente, ex.: Analisar em simulação).
        string? acao = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("acao:"))?[5..];
        int espera = 4;
        if (acao != null && nav != null && w.FindName(nav) is RadioButton { Tag: string pg } && w.FindName(pg) is FrameworkElement pagina)
        {
            var m = pagina.GetType().GetMethod(acao, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            w.Dispatcher.BeginInvoke(() => m?.Invoke(pagina, new object[] { pagina, new RoutedEventArgs() }), DispatcherPriority.ApplicationIdle);
            espera = 25;
        }

        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(espera) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            try
            {
                var root = (FrameworkElement)w.Content;
                int pw = (int)Math.Ceiling(root.ActualWidth), ph = (int)Math.Ceiling(root.ActualHeight);
                var bmp = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen())
                    dc.DrawRectangle((Brush)Application.Current.Resources["Bg"], null, new Rect(0, 0, pw, ph));
                bmp.Render(bg);
                bmp.Render(root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(arquivo);
                enc.Save(fs);
            }
            catch (Exception ex) { File.WriteAllText(arquivo + ".erro.txt", ex.ToString()); }
            Application.Current.Shutdown();
        };
        t.Start();
    }
}
