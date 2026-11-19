using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Hefesto;

/// <summary>Microsoft Defender: estado (só leitura) e verificações pelo MpCmdRun.</summary>
public partial class DefenderPage : UserControl, IPagina
{
    private CancellationTokenSource? _cts;
    private readonly StringBuilder _saida = new();

    public DefenderPage() => InitializeComponent();

    public async void Mostrar() => await LerEstado();

    private async Task LerEstado()
    {
        var s = await Task.Run(Defender.Status);
        if (!s.Disponivel)
        {
            Estado.Text = "Indisponível";
            EstadoSub.Text = $"Não foi possível ler o Microsoft Defender ({s.Erro}). Se você usa outro antivírus, o Defender fica em segundo plano e é ele quem protege o PC.";
            Ameacas.Text = "";
            return;
        }
        bool ok = s.Antivirus && s.TempoReal;
        Estado.Text = ok ? "Protegido" : "Proteção desligada";
        Estado.Foreground = (System.Windows.Media.Brush)FindResource(ok ? "Fg" : "Accent");
        var linhas = new List<string>
        {
            "Proteção em tempo real: " + (s.TempoReal ? "ligada" : "DESLIGADA (ligue em Segurança do Windows)"),
            "Proteção contra adulteração: " + (s.Adulteracao ? "ligada" : "desligada"),
            $"Definições: {s.Assinaturas}" + (s.AssinaturasData is DateTime d ? $", de {d:dd/MM/yyyy HH:mm}" + ((DateTime.Now - d).TotalDays > 3 ? " (desatualizadas)" : "") : ""),
            "Última verificação rápida: " + (s.UltimaRapida?.ToString("dd/MM/yyyy HH:mm") ?? "nunca"),
            "Última verificação completa: " + (s.UltimaCompleta?.ToString("dd/MM/yyyy HH:mm") ?? "nunca"),
        };
        EstadoSub.Text = string.Join("\n", linhas);
        Ameacas.Text = s.Ameacas.Count == 0 ? "Nenhuma ameaça detectada no histórico do Defender." : string.Join("\n", s.Ameacas);
    }

    private void Ocupado(bool sim)
    {
        BtnRapida.IsEnabled = BtnAtualizar.IsEnabled = BtnPasta.IsEnabled = BtnArquivo.IsEnabled = BtnCompleta.IsEnabled = !sim;
        BtnCancelar.Visibility = sim ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task Rodar(string titulo, Func<IProgress<string>, CancellationToken, Task<int>> acao)
    {
        _cts = new CancellationTokenSource();
        Ocupado(true);
        _saida.Clear();
        CaixaSaida.Visibility = Visibility.Visible;
        Saida.Text = "";
        Status.Text = titulo + "…";
        var prog = new Progress<string>(l => { _saida.AppendLine(l); Saida.Text = _saida.ToString(); });
        try
        {
            int c = await acao(prog, _cts.Token);
            Status.Text = c switch
            {
                0 => titulo + ": concluído, nenhuma ameaça encontrada.",
                2 => titulo + ": AMEAÇA ENCONTRADA. O Defender já tratou conforme a configuração dele; veja em Segurança do Windows.",
                _ => $"{titulo}: terminou com código {c}. Veja a saída acima.",
            };
        }
        catch (OperationCanceledException) { Status.Text = titulo + ": cancelado."; }
        catch (Exception ex) { Status.Text = titulo + ": " + ex.Message; }
        finally { Ocupado(false); await LerEstado(); }
    }

    private async void Rapida_Click(object sender, RoutedEventArgs e) => await Rodar("Verificação rápida", Defender.VerificacaoRapida);
    private async void Atualizar_Click(object sender, RoutedEventArgs e) => await Rodar("Atualização das definições", Defender.AtualizarAssinaturas);

    private async void Completa_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.Confirmar("A verificação completa olha todos os arquivos e pode levar mais de uma hora, deixando o PC mais lento. Começar?", "Verificação de vírus")) return;
        await Rodar("Verificação completa", Defender.VerificacaoCompleta);
    }

    private async void Pasta_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta para verificar" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        await Rodar("Verificação de " + dlg.FolderName, (p, ct) => Defender.Verificar(dlg.FolderName, p, ct));
    }

    private async void Arquivo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Arquivo para verificar" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        await Rodar("Verificação de " + dlg.FileName, (p, ct) => Defender.Verificar(dlg.FileName, p, ct));
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Seguranca_Click(object sender, RoutedEventArgs e) => Ui.AbrirUrl("windowsdefender:");
}
