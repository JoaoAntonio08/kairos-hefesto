using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Hefesto;

/// <summary>Limpeza profunda: mapa do disco, lixo no disco inteiro (MFT) e regras do winapp2.</summary>
public partial class LimpezaProfundaPage : UserControl, IPagina
{
    private CancellationTokenSource? _cts;
    private bool _ocupado, _discosCarregados;
    private string? _disco;
    private MapaDisco.Resultado? _mapa;
    private NoMapa? _atual;
    private bool _mostrandoMaiores;

    private List<ResultadoRegra> _lixo = new(), _w2 = new();
    private ContextoVarredura? _ctxLixo, _ctxW2;

    public LimpezaProfundaPage() => InitializeComponent();

    public void Mostrar()
    {
        if (_discosCarregados) return;
        _discosCarregados = true;
        Discos.Children.Clear();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                string rot = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : d.VolumeLabel + " · ";
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("Chip"), GroupName = "ProfDisco", Tag = d.RootDirectory.FullName,
                    Content = $"{d.Name.TrimEnd('\\')}  {rot}{Ui.Tam(d.TotalSize - d.TotalFreeSpace)} de {Ui.Tam(d.TotalSize)} · {d.DriveFormat}",
                };
                rb.Checked += (s, _) => _disco = (string)((RadioButton)s).Tag;
                if (Discos.Children.Count == 0) { rb.IsChecked = true; _disco = d.RootDirectory.FullName; }
                Discos.Children.Add(rb);
            }
            catch { }
        }
    }

    // ---------------- comum ----------------

    private void Aba_Checked(object sender, RoutedEventArgs e)
    {
        if (PainelMapa == null) return;
        PainelMapa.Visibility = AbaMapa.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PainelLixo.Visibility = AbaLixo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PainelW2.Visibility = AbaW2.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (!_ocupado)
            Status.Text = AbaMapa.IsChecked == true ? "Escolha um disco e clique em MAPEAR para ver o que ocupa espaço."
                        : AbaLixo.IsChecked == true ? "Clique em ANALISAR para procurar lixo em todos os discos."
                        : $"Clique em ANALISAR. {(Winapp2.VersaoLocal() is string v ? "Regras baixadas: versão " + v + "." : "As regras serão baixadas do repositório oficial na primeira vez.")}";
    }

    private bool Simulando => Simulacao.IsChecked == true;

    private void Simulacao_Changed(object sender, RoutedEventArgs e) => AtualizarBotoes();

    private void AtualizarBotoes()
    {
        if (BtnMapear == null) return;
        BtnMapear.IsEnabled = BtnLixo.IsEnabled = BtnW2.IsEnabled = BtnW2Baixar.IsEnabled = !_ocupado;
        BtnLimparLixo.IsEnabled = !_ocupado && !Simulando && _lixo.Any(r => r.Selecionada && r.Itens.Count > 0);
        BtnLimparW2.IsEnabled = !_ocupado && !Simulando && _w2.Any(r => r.Selecionada && r.Itens.Count > 0);
        BtnCancelar.Visibility = _ocupado ? Visibility.Visible : Visibility.Collapsed;
        BtnRelatorio.Visibility = RelatorioLimpeza.Ultimo != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Ocupado(bool sim, string? status = null)
    {
        _ocupado = sim;
        if (sim) _cts = new CancellationTokenSource();
        if (status != null) Status.Text = status;
        AtualizarBotoes();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        if (RelatorioLimpeza.Ultimo is string r && File.Exists(r)) Ui.AbrirUrl(r);
    }

    // ---------------- mapa do disco ----------------

    private async void Mapear_Click(object sender, RoutedEventArgs e)
    {
        if (_disco == null) return;
        Ocupado(true, $"Mapeando {_disco}…");
        Progresso.ScaleX = 0.1;
        var prog = new Progress<long>(p => Status.Text = $"Mapeando {_disco}… {p:N0} pastas lidas");
        try
        {
            string raiz = _disco;
            var ct = _cts!.Token;
            _mapa = await Task.Run(() => MapaDisco.Mapear(raiz, prog, ct));
            _atual = _mapa.Raiz;
            Exibir();
            BtnMaiores.IsEnabled = true;
            Status.Text = (ct.IsCancellationRequested ? "Cancelado: o mapa está incompleto. " : "")
                          + $"{_mapa.Pastas:N0} pastas e {_mapa.Raiz.Arquivos:N0} arquivos em {_mapa.Tempo.TotalSeconds:N1} s. "
                          + "Clique numa pasta para entrar nela. Pastas protegidas do sistema ou sem permissão aparecem menores do que são.";
        }
        catch (Exception ex) { Status.Text = "Erro ao mapear: " + ex.Message; }
        finally { Progresso.ScaleX = 1; Ocupado(false); }
    }

    private void Exibir()
    {
        if (_atual == null) return;
        _mostrandoMaiores = false;
        MapaCaminho.Text = _atual.Caminho;
        MapaSub.Text = $"{Ui.Tam(_atual.Tamanho)} · {_atual.Arquivos:N0} arquivos";
        ListaMapa.ItemsSource = MapaDisco.Exibir(_atual);
        BtnSubir.IsEnabled = _atual.Pai != null;
    }

    private void Abrir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NoMapa n } && n.EhPasta && n.Filhos.Count + (n.ArquivosSoltos > 0 ? 1 : 0) > 0)
        {
            _atual = n;
            Exibir();
        }
    }

    private void Subir_Click(object sender, RoutedEventArgs e)
    {
        if (_mostrandoMaiores) { Exibir(); return; }
        if (_atual?.Pai != null) { _atual = _atual.Pai; Exibir(); }
    }

    private void Maiores_Click(object sender, RoutedEventArgs e)
    {
        if (_mapa == null) return;
        _mostrandoMaiores = true;
        MapaCaminho.Text = "Maiores arquivos de " + _mapa.Raiz.Caminho;
        MapaSub.Text = "Arquivos com 50 MB ou mais. Confira com calma: muitos são jogos, vídeos e projetos seus.";
        double max = Math.Max(1, _mapa.MaioresArquivos.FirstOrDefault()?.Tamanho ?? 1);
        foreach (var a in _mapa.MaioresArquivos) a.Fracao = a.Tamanho / max;
        ListaMapa.ItemsSource = _mapa.MaioresArquivos.ToList();
        BtnSubir.IsEnabled = true;
    }

    private void MostrarNo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NoMapa n }) Ui.AbrirPasta(n.Caminho);
    }

    private async void LixeiraNo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: NoMapa n } || _ocupado) return;
        if (n.Nome == "(arquivos nesta pasta)")
        {
            MessageBox.Show(Window.GetWindow(this), "Abra a pasta com MOSTRAR e escolha os arquivos no Explorador.", "Limpeza profunda");
            return;
        }
        string? motivo = Protecao.Motivo(n.Caminho) ?? ContemSistema(n.Caminho);
        if (motivo != null)
        {
            MessageBox.Show(Window.GetWindow(this), $"{n.Caminho}\n\nEste item está protegido: {motivo}.", "Limpeza profunda");
            return;
        }
        if (Simulando)
        {
            Status.Text = $"Modo simulação: {n.Nome} ({n.TamanhoTexto}) iria para a Lixeira. Desligue a simulação para enviar de verdade.";
            return;
        }
        if (!Ui.Confirmar($"Enviar para a Lixeira?\n\n{n.Caminho}\n{n.TamanhoTexto}" + (n.EhPasta ? $" · {n.Arquivos:N0} arquivos" : "") +
                          "\n\nSe for maior que a Lixeira, vai para a quarentena do Hefesto.", "Limpeza profunda")) return;

        Ocupado(true, "Enviando para a Lixeira…");
        try
        {
            var item = new ItemLimpeza { Caminho = n.Caminho, EhPasta = n.EhPasta, Tamanho = n.Tamanho, Modificado = DateTime.Now, Regra = "mapa" };
            var res = await Lixeira.RemoverAsync(new[] { item }, false, null, _cts!.Token);
            var r = res.FirstOrDefault();
            if (r != null && r.Destino is Destino.Lixeira or Destino.Quarentena)
            {
                // Tira do mapa e desconta o tamanho dos pais.
                for (var p = n.Pai; p != null; p = p.Pai) { p.Tamanho -= n.Tamanho; if (n.EhPasta) p.Arquivos -= n.Arquivos; else p.Arquivos--; }
                n.Pai?.Filhos.Remove(n);
                _mapa?.MaioresArquivos.Remove(n);
                Status.Text = $"{n.Nome} ({n.TamanhoTexto}) foi para a {(r.Destino == Destino.Lixeira ? "Lixeira" : "quarentena")}.";
                if (_mostrandoMaiores) Maiores_Click(sender, e); else Exibir();
            }
            else Status.Text = $"Não foi possível remover {n.Nome}: {r?.Detalhe}";
        }
        catch (Exception ex) { Status.Text = "Erro: " + ex.Message; }
        finally { Ocupado(false); }
    }

    /// <summary>Barra pastas que contêm o perfil, o Windows ou os programas (ex.: C:\Users, a raiz de um disco).</summary>
    private static string? ContemSistema(string caminho)
    {
        string semBarra = caminho.TrimEnd('\\'), c = semBarra + "\\";
        if (string.Equals(Path.GetPathRoot(caminho)?.TrimEnd('\\'), semBarra, StringComparison.OrdinalIgnoreCase)) return "é a raiz do disco";
        var essenciais = new[]
        {
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.MyDocuments,
        }.Select(Environment.GetFolderPath).Append(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? "");
        foreach (var e in essenciais)
            if (e.Length > 3 && (e.TrimEnd('\\') + "\\").StartsWith(c, StringComparison.OrdinalIgnoreCase))
                return "contém pastas do sistema ou do seu perfil (" + e + ")";
        if (caminho.Contains("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) return "é a Lixeira; esvazie pela própria Lixeira do Windows";
        if (caminho.Contains("System Volume Information", StringComparison.OrdinalIgnoreCase)) return "pasta do sistema (pontos de restauração)";
        return null;
    }

    // ---------------- lixo no disco inteiro ----------------

    private async void AnalisarLixo_Click(object sender, RoutedEventArgs e)
    {
        Ocupado(true, "Procurando lixo em todos os discos…");
        Progresso.ScaleX = 0.1;
        _ctxLixo = new ContextoVarredura { Ct = _cts!.Token };
        try
        {
            var discos = LixoGlobal.Discos();
            int i = 0;
            var st = new Progress<string>(s => { Status.Text = s; Progresso.ScaleX = Math.Min(0.95, 0.1 + 0.85 * (i++ / (double)Math.Max(1, discos.Count))); });
            _lixo = await LixoGlobal.Analisar(discos, _ctxLixo, st);
            foreach (var r in _lixo) r.PropertyChanged += (_, _) => TotalLixo();
            ListaLixo.ItemsSource = _lixo;
            TotalLixo();
            string rel = RelatorioLimpeza.Gerar("Limpeza profunda · lixo no disco inteiro · análise", _lixo, _ctxLixo, null, Simulando);
            Status.Text = (_ctxLixo.Avisos.IsEmpty ? "" : string.Join("  ", _ctxLixo.Avisos) + "  ")
                          + (Simulando ? "Modo simulação: nada foi alterado. " : "Confira e clique em LIMPAR SELECIONADOS. ")
                          + $"Relatório: {rel}";
        }
        catch (Exception ex) { Status.Text = "Erro na análise: " + ex.Message; }
        finally { Progresso.ScaleX = 1; Ocupado(false); }
    }

    private void TotalLixo()
    {
        LixoTotal.Visibility = Visibility.Visible;
        LixoTotal.Text = Ui.Tam(_lixo.Where(r => r.Selecionada).Sum(r => r.Total));
        AtualizarBotoes();
    }

    private async void LimparLixo_Click(object sender, RoutedEventArgs e)
    {
        if (_ctxLixo != null && await Limpar("Limpeza profunda · lixo no disco inteiro", _lixo, _ctxLixo))
        {
            ListaLixo.ItemsSource = null; ListaLixo.ItemsSource = _lixo; TotalLixo();
        }
    }

    // ---------------- winapp2 ----------------

    private async void BaixarW2_Click(object sender, RoutedEventArgs e)
    {
        Ocupado(true, "Baixando o winapp2.ini do repositório oficial…");
        try { Status.Text = await Winapp2.Atualizar(true, _cts!.Token) + $" · versão {Winapp2.VersaoLocal()}"; }
        catch (Exception ex) { Status.Text = "Não foi possível baixar as regras: " + ex.Message; }
        finally { Ocupado(false); }
    }

    private async void AnalisarW2_Click(object sender, RoutedEventArgs e)
    {
        Ocupado(true, "Conferindo as regras do winapp2…");
        Progresso.ScaleX = 0.05;
        _ctxW2 = new ContextoVarredura { Ct = _cts!.Token };
        try
        {
            string baixa;
            try { baixa = await Winapp2.Atualizar(false, _cts.Token); }
            catch (Exception ex)
            {
                if (!File.Exists(Winapp2.Arquivo)) throw new Exception("não foi possível baixar as regras (" + ex.Message + ")");
                baixa = "sem internet: usando as regras já baixadas";
            }
            var prog = new Progress<(int Feitos, int Total)>(p =>
            {
                Progresso.ScaleX = p.Total == 0 ? 1 : (double)p.Feitos / p.Total;
                Status.Text = $"Verificando regras do winapp2… {p.Feitos:N0} de {p.Total:N0}";
            });
            _w2 = await Winapp2.Analisar(_ctxW2, prog);
            foreach (var r in _w2) r.PropertyChanged += (_, _) => TotalW2();
            ListaW2.ItemsSource = _w2;
            TotalW2();
            string rel = RelatorioLimpeza.Gerar("Limpeza profunda · winapp2 · análise", _w2, _ctxW2, null, Simulando);
            Status.Text = $"{baixa} (versão {Winapp2.VersaoLocal()}). {_w2.Count:N0} regras se aplicam a este PC e têm o que limpar; todas vêm desmarcadas. "
                          + (Simulando ? "Modo simulação ligado. " : "") + $"Relatório: {rel}";
        }
        catch (Exception ex) { Status.Text = "Erro: " + ex.Message; }
        finally { Progresso.ScaleX = 1; Ocupado(false); }
    }

    private void TotalW2()
    {
        W2Total.Visibility = Visibility.Visible;
        var sel = _w2.Where(r => r.Selecionada).ToList();
        W2Total.Text = sel.Count == 0 ? $"{Ui.Tam(_w2.Sum(r => r.Total))} encontrados" : Ui.Tam(sel.Sum(r => r.Total)) + " marcados";
        AtualizarBotoes();
    }

    private async void LimparW2_Click(object sender, RoutedEventArgs e)
    {
        if (_ctxW2 != null && await Limpar("Limpeza profunda · winapp2", _w2, _ctxW2))
        {
            ListaW2.ItemsSource = null; ListaW2.ItemsSource = _w2; TotalW2();
        }
    }

    // ---------------- limpeza (comum às duas listas) ----------------

    private async Task<bool> Limpar(string titulo, List<ResultadoRegra> regras, ContextoVarredura ctx)
    {
        if (Simulando) return false;
        var sel = regras.Where(r => r.Selecionada && r.Itens.Count > 0).ToList();
        var itens = sel.SelectMany(r => r.Itens).GroupBy(i => i.Caminho, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (itens.Count == 0) return false;
        string avisos = string.Join("\n", sel.Where(r => r.Descricao.Contains("ATENÇÃO")).Select(r => "⚠ " + r.Nome));
        if (!Ui.Confirmar($"Enviar {itens.Count:N0} itens ({Ui.Tam(itens.Sum(i => i.Tamanho))}) para a Lixeira?\n\n" +
                          string.Join("\n", sel.Take(25).Select(r => $"• {r.Nome}: {r.TotalTexto}")) + (sel.Count > 25 ? $"\n… e mais {sel.Count - 25}" : "") +
                          (avisos.Length > 0 ? "\n\nRegras com aviso do winapp2:\n" + avisos : "") +
                          "\n\nItens maiores que a Lixeira vão para a quarentena do Hefesto.", "Limpeza profunda")) return false;

        Ocupado(true, "Limpando…");
        var prog = new Progress<(int Feitos, int Total)>(p =>
        {
            Progresso.ScaleX = p.Total == 0 ? 1 : (double)p.Feitos / p.Total;
            Status.Text = $"Limpando… {p.Feitos:N0} de {p.Total:N0}";
        });
        try
        {
            var res = await Lixeira.RemoverAsync(itens, false, prog, _cts!.Token);
            string rel = RelatorioLimpeza.Gerar(titulo, regras, ctx, res, false);
            long liberado = res.Where(r => r.Destino is Destino.Lixeira or Destino.Quarentena).Sum(r => r.Item.Tamanho);
            int falhas = res.Count(r => r.Destino is Destino.Falhou or Destino.Protegido);
            foreach (var r in sel) r.Itens = new();
            Status.Text = $"Pronto: {Ui.Tam(liberado)} liberados" + (falhas > 0 ? $", {falhas:N0} itens em uso ficaram" : "") + $". Relatório: {rel}";
            Ui.AbrirUrl(rel);
            return true;
        }
        catch (Exception ex) { Status.Text = "Erro na limpeza: " + ex.Message; return false; }
        finally { Progresso.ScaleX = 1; Ocupado(false); }
    }
}
