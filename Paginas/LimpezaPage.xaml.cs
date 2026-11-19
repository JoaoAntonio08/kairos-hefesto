using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Hefesto;

/// <summary>Limpeza rápida: regras fixas, modo simulação, envio para Lixeira/quarentena e relatório HTML.</summary>
public partial class LimpezaPage : UserControl, IPagina
{
    private List<ResultadoRegra> _resultados = new();
    private ContextoVarredura? _ctx;
    private CancellationTokenSource? _cts;
    private bool _ocupado;

    public LimpezaPage()
    {
        InitializeComponent();
        var s = AppSettings.Atual;
        DiasTemp.Text = s.LimpezaDiasTemp.ToString();
        DiasInst.Text = s.LimpezaDiasInstaladores.ToString();
    }

    public void Mostrar() { }

    private static int Num(TextBox t, int padrao) => int.TryParse(t.Text.Trim(), out int v) && v >= 0 && v <= 3650 ? v : padrao;

    private void Ocupado(bool sim, string? status = null)
    {
        _ocupado = sim;
        BtnAnalisar.IsEnabled = !sim;
        BtnLimpar.IsEnabled = !sim && !Simulacao.IsChecked.GetValueOrDefault() && _resultados.Any(r => r.Itens.Count > 0);
        BtnCancelar.Visibility = sim ? Visibility.Visible : Visibility.Collapsed;
        if (status != null) Status.Text = status;
    }

    private void Simulacao_Changed(object sender, RoutedEventArgs e)
    {
        if (BtnLimpar == null) return;
        BtnLimpar.IsEnabled = !_ocupado && !Simulacao.IsChecked.GetValueOrDefault() && _resultados.Any(r => r.Itens.Count > 0);
    }

    private async void Analisar_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Atual;
        s.LimpezaDiasTemp = Num(DiasTemp, 7);
        s.LimpezaDiasInstaladores = Num(DiasInst, 30);
        DiasTemp.Text = s.LimpezaDiasTemp.ToString();
        DiasInst.Text = s.LimpezaDiasInstaladores.ToString();
        try { s.Save(); } catch { }

        _cts = new CancellationTokenSource();
        _ctx = new ContextoVarredura { DiasTemp = s.LimpezaDiasTemp, DiasInstaladores = s.LimpezaDiasInstaladores, Ct = _cts.Token };
        Ocupado(true, "Analisando… (pastas grandes podem levar alguns segundos)");
        Progresso.ScaleX = 0.15;
        try
        {
            _resultados = await LimpezaRapida.Analisar(LimpezaRapida.Regras(), _ctx);
            foreach (var r in _resultados)
                if (s.LimpezaRegrasDesligadas.Contains(r.Regra.Id)) r.Selecionada = false;
            foreach (var r in _resultados) r.PropertyChanged += (_, _) => AtualizarTotal();
            ListaRegras.ItemsSource = _resultados;
            AtualizarTotal();
            MostrarSeu();
            string rel = RelatorioLimpeza.Gerar("Limpeza rápida · análise", _resultados, _ctx, null, Simulacao.IsChecked == true);
            BtnRelatorio.IsEnabled = true;
            Status.Text = (_ctx.Avisos.IsEmpty ? "" : string.Join("  ", _ctx.Avisos) + "  ")
                          + (Simulacao.IsChecked == true
                              ? "Modo simulação: nada foi alterado. Desligue a simulação para poder limpar."
                              : "Confira as regras marcadas e clique em LIMPAR SELECIONADOS.")
                          + $"  Relatório salvo em {rel}";
        }
        catch (Exception ex) { Status.Text = "Erro na análise: " + ex.Message; }
        finally { Progresso.ScaleX = 1; Ocupado(false); }
    }

    private void AtualizarTotal()
    {
        var sel = _resultados.Where(r => r.Selecionada).ToList();
        long total = sel.Sum(r => r.Total);
        int qtd = sel.Sum(r => r.Itens.Count);
        Destaque.Visibility = Visibility.Visible;
        TotalTexto.Text = Ui.Tam(total);
        TotalSub.Text = qtd == 0 ? "nada para limpar nas regras marcadas" : $"podem ser liberados em {qtd:N0} itens nas regras marcadas";
        var s = AppSettings.Atual;
        // Lembra as regras que o usuário desmarcou (só as que tinham o que limpar).
        s.LimpezaRegrasDesligadas = _resultados.Where(r => !r.Selecionada && r.Itens.Count > 0).Select(r => r.Regra.Id).ToList();
        Simulacao_Changed(this, new RoutedEventArgs());
    }

    private void MostrarSeu()
    {
        if (_ctx == null) return;
        var seus = _ctx.PodeSerSeu.Values.Where(p => p.Classe != "gerado")
            .OrderBy(p => p.Classe == "seu" ? 0 : 1).ThenByDescending(p => p.Tamanho).ToList();
        PainelSeu.Visibility = seus.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SeuSub.Text = $"{seus.Count:N0} arquivos de trabalho (vídeo, documento, código, projeto) estão em pastas temporárias ou em Downloads. " +
                      "A limpeza não mexe neles; confira e guarde numa pasta sua o que for importante." +
                      (seus.Count > 40 ? " Aqui aparecem os 40 primeiros; a lista completa está no relatório." : "");
        ListaSeu.ItemsSource = seus.Take(40).ToList();
    }

    private async void Limpar_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx == null || Simulacao.IsChecked == true) return;
        var sel = _resultados.Where(r => r.Selecionada && r.Itens.Count > 0).ToList();
        var itens = sel.SelectMany(r => r.Itens).ToList();
        if (itens.Count == 0) return;
        long total = itens.Sum(i => i.Tamanho);
        if (!Ui.Confirmar($"Enviar {itens.Count:N0} itens ({Ui.Tam(total)}) para a Lixeira?\n\n" +
                          string.Join("\n", sel.Select(r => $"• {r.Nome}: {r.TotalTexto}")) +
                          "\n\nItens maiores que a Lixeira vão para a quarentena do Hefesto. Dá para desfazer pela Lixeira do Windows " +
                          "ou pelo botão QUARENTENA.", "Limpeza rápida")) return;

        _cts = new CancellationTokenSource();
        Ocupado(true, "Limpando…");
        var prog = new Progress<(int Feitos, int Total)>(p =>
        {
            Progresso.ScaleX = p.Total == 0 ? 1 : (double)p.Feitos / p.Total;
            Status.Text = $"Limpando… {p.Feitos:N0} de {p.Total:N0}";
        });
        try
        {
            var res = await Lixeira.RemoverAsync(itens, false, prog, _cts.Token);
            string rel = RelatorioLimpeza.Gerar("Limpeza rápida", _resultados, _ctx, res, false);
            long lix = res.Where(r => r.Destino == Destino.Lixeira).Sum(r => r.Item.Tamanho);
            long qua = res.Where(r => r.Destino == Destino.Quarentena).Sum(r => r.Item.Tamanho);
            int falhas = res.Count(r => r.Destino is Destino.Falhou or Destino.Protegido);
            TotalTexto.Text = Ui.Tam(lix + qua);
            TotalSub.Text = $"liberados · {Ui.Tam(lix)} na Lixeira" + (qua > 0 ? $" · {Ui.Tam(qua)} na quarentena" : "")
                            + (falhas > 0 ? $" · {falhas:N0} itens em uso ficaram" : "");
            foreach (var r in _resultados) r.Itens = new();
            ListaRegras.ItemsSource = null;
            ListaRegras.ItemsSource = _resultados;
            Status.Text = $"Pronto. Relatório salvo em {rel}";
            Ui.AbrirUrl(rel);
        }
        catch (Exception ex) { Status.Text = "Erro na limpeza: " + ex.Message; }
        finally { Ocupado(false); Progresso.ScaleX = 1; }
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        if (RelatorioLimpeza.Ultimo is string r && File.Exists(r)) Ui.AbrirUrl(r);
    }

    private void Mostrar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string p }) Ui.AbrirPasta(p);
    }

    private async void Quarentena_Click(object sender, RoutedEventArgs e)
    {
        PainelQuarentena.Visibility = Visibility.Visible;
        QuarentenaSub.Text = "Lendo…";
        var lista = await Task.Run(Lixeira.ListarQuarentena);
        ListaQuarentena.ItemsSource = lista;
        QuarentenaSub.Text = lista.Count == 0
            ? "A quarentena está vazia. Ela só recebe itens que não cabem na Lixeira do Windows."
            : $"{lista.Count:N0} itens ({Ui.Tam(lista.Sum(q => q.Tamanho))}) em pastas \"Hefesto Quarentena\" na raiz de cada disco. " +
              "RESTAURAR devolve o item ao lugar original. Quando tiver certeza de que não precisa, apague a pasta pelo Explorador.";
    }

    private void Restaurar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: EntradaQuarentena q }) return;
        string? erro = Lixeira.Restaurar(q);
        if (erro != null) MessageBox.Show(Window.GetWindow(this), "Não foi possível restaurar:\n" + erro, "Quarentena");
        Quarentena_Click(sender, e);
    }
}
