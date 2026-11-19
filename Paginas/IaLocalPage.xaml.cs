using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Hefesto;

/// <summary>IA local (Ollama) para explicar relatórios.</summary>
public partial class IaLocalPage : UserControl, IPagina
{
    private CancellationTokenSource? _cts;

    public IaLocalPage() => InitializeComponent();

    public async void Mostrar()
    {
        await Verificar();
        if (Entrada.Text.Length == 0 && RelatorioLimpeza.UltimoResumo is string r) Entrada.Text = r;
    }

    private async Task Verificar()
    {
        BtnInstalar.Visibility = BtnIniciar.Visibility = BtnModelo.Visibility = Modelos.Visibility = Visibility.Collapsed;
        var modelos = await Ollama.Modelos();
        if (modelos == null)
        {
            if (Ollama.Exe() == null)
            {
                Estado.Text = "Ollama não instalado";
                EstadoSub.Text = "O Ollama é um programa gratuito e de código aberto que roda modelos de IA no próprio PC. O Hefesto instala pelo winget (Gerenciador de Pacotes do Windows), da fonte oficial.";
                BtnInstalar.Visibility = Visibility.Visible;
            }
            else
            {
                Estado.Text = "Ollama parado";
                EstadoSub.Text = "O Ollama está instalado, mas não está rodando.";
                BtnIniciar.Visibility = Visibility.Visible;
            }
            BtnExplicar.IsEnabled = false;
            return;
        }
        if (modelos.Count == 0)
        {
            Estado.Text = "Nenhum modelo baixado";
            EstadoSub.Text = $"Sugestão: {Ollama.ModeloSugerido} (cerca de 2 GB, bom em português e roda até sem placa de vídeo dedicada).";
            BtnModelo.Visibility = Visibility.Visible;
            BtnExplicar.IsEnabled = false;
            return;
        }
        Estado.Text = "Pronto";
        EstadoSub.Text = $"Ollama rodando com {modelos.Count} modelo(s).";
        Modelos.ItemsSource = modelos;
        Modelos.SelectedItem = modelos.Contains(AppSettings.Atual.OllamaModelo ?? "") ? AppSettings.Atual.OllamaModelo
                             : modelos.FirstOrDefault(m => m.StartsWith("qwen")) ?? modelos[0];
        Modelos.Visibility = Visibility.Visible;
        BtnModelo.Visibility = modelos.Any(m => m.StartsWith(Ollama.ModeloSugerido)) ? Visibility.Collapsed : Visibility.Visible;
        BtnExplicar.IsEnabled = true;
    }

    private async void Verificar_Click(object sender, RoutedEventArgs e) => await Verificar();

    private void Modelos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Modelos.SelectedItem is string m) { AppSettings.Atual.OllamaModelo = m; try { AppSettings.Atual.Save(); } catch { } }
    }

    private async void Instalar_Click(object sender, RoutedEventArgs e)
    {
        if (!AppInstaller.WingetAvailable) { Status.Text = "O winget não está disponível neste Windows. Instale o Ollama pelo site oficial ollama.com."; Ui.AbrirUrl("https://ollama.com/download"); return; }
        if (!Ui.Confirmar("Instalar o Ollama pelo winget (pacote Ollama.Ollama, fonte oficial)?\n\nO download tem cerca de 1 GB.", "IA local")) return;
        BtnInstalar.IsEnabled = false;
        Status.Text = "Instalando o Ollama pelo winget… (pode levar alguns minutos)";
        try
        {
            var (ok, msg) = await AppInstaller.InstallAsync(new InstallableApp { Name = "Ollama", WingetId = "Ollama.Ollama" }, CancellationToken.None);
            Status.Text = ok ? "Ollama instalado." : "Não foi possível instalar: " + msg;
            if (ok) await Ollama.Iniciar();
        }
        catch (Exception ex) { Status.Text = "Erro: " + ex.Message; }
        finally { BtnInstalar.IsEnabled = true; await Verificar(); }
    }

    private async void Iniciar_Click(object sender, RoutedEventArgs e)
    {
        Status.Text = "Iniciando o Ollama…";
        Status.Text = await Ollama.Iniciar() ? "Ollama iniciado." : "O Ollama não respondeu. Tente abrir pelo menu Iniciar.";
        await Verificar();
    }

    private async void BaixarModelo_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.Confirmar($"Baixar o modelo {Ollama.ModeloSugerido} pelo Ollama (cerca de 2 GB)?", "IA local")) return;
        _cts = new CancellationTokenSource();
        BtnModelo.IsEnabled = false;
        BtnParar.Visibility = Visibility.Visible;
        var prog = new Progress<(string St, double F)>(p => Status.Text = p.F >= 0 ? $"{p.St} {p.F:P0}" : p.St);
        try { await Ollama.Baixar(Ollama.ModeloSugerido, prog, _cts.Token); Status.Text = "Modelo baixado."; }
        catch (OperationCanceledException) { Status.Text = "Download interrompido."; }
        catch (Exception ex) { Status.Text = "Erro ao baixar o modelo: " + ex.Message; }
        finally { BtnModelo.IsEnabled = true; BtnParar.Visibility = Visibility.Collapsed; await Verificar(); }
    }

    private void UltimaLimpeza_Click(object sender, RoutedEventArgs e)
    {
        if (RelatorioLimpeza.UltimoResumo is string r) Entrada.Text = r;
        else
        {
            var ultimo = UltimoRelatorio();
            Entrada.Text = ultimo != null ? Ollama.TextoDeHtml(File.ReadAllText(ultimo)) : "";
            Status.Text = ultimo == null ? "Nenhum relatório encontrado. Rode uma análise na Limpeza rápida antes." : "Carregado: " + ultimo;
        }
    }

    private static string? UltimoRelatorio()
    {
        string pasta = Ui.PastaDocumentos("Relatórios");
        return Directory.Exists(pasta) ? Directory.GetFiles(pasta, "*.html").OrderByDescending(File.GetLastWriteTime).FirstOrDefault() : null;
    }

    private void AbrirRelatorio_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Relatório para explicar", Filter = "Relatórios (*.html;*.txt)|*.html;*.htm;*.txt|Todos|*.*",
            InitialDirectory = Ui.PastaDocumentos("Relatórios"),
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        string t = File.ReadAllText(dlg.FileName);
        Entrada.Text = dlg.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? (t.Length > 12000 ? t[..12000] : t) : Ollama.TextoDeHtml(t);
        Status.Text = "Carregado: " + dlg.FileName;
    }

    private async void Explicar_Click(object sender, RoutedEventArgs e)
    {
        if (Modelos.SelectedItem is not string modelo || Entrada.Text.Trim().Length == 0) return;
        _cts = new CancellationTokenSource();
        BtnExplicar.IsEnabled = false;
        BtnParar.Visibility = Visibility.Visible;
        CaixaResposta.Visibility = Visibility.Visible;
        Resposta.Text = "";
        Status.Text = $"Pensando com {modelo}… (a primeira resposta demora mais, enquanto o modelo carrega)";
        string pedido = "Explique este conteúdo do Hefesto para mim. Diga o que significa, o que vale a pena fazer e o que exige cuidado:\n\n" + Entrada.Text.Trim();
        var prog = new Progress<string>(p => Resposta.AppendText(p));
        try { await Ollama.Gerar(modelo, pedido, prog, _cts.Token); Status.Text = "Pronto."; }
        catch (OperationCanceledException) { Status.Text = "Parado."; }
        catch (Exception ex) { Status.Text = "Erro: " + ex.Message; }
        finally { BtnExplicar.IsEnabled = true; BtnParar.Visibility = Visibility.Collapsed; }
    }

    private void Parar_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
}
