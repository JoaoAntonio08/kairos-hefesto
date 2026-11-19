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

        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
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
