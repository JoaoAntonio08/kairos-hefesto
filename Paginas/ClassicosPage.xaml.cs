using System.Windows;
using System.Windows.Controls;

namespace Hefesto;

/// <summary>Linha da lista de clássicos.</summary>
public sealed class ItemClassico
{
    public Classico C { get; init; } = null!;
    public string? Instalada { get; set; }
    public string? Ultima { get; set; }
    public string? Erro { get; set; }
    public bool Ocupado { get; set; }
    public bool Instalado => Instalada != null;
    public bool PodeInstalar => !Ocupado && !(Instalado && Ultima != null && Ultima == Instalada);
    public string TextoInstalar => !Instalado ? "INSTALAR" : Ultima != null && Ultima == Instalada ? "ATUALIZADO" : "ATUALIZAR";
    public string Situacao
    {
        get
        {
            string s = Instalado ? $"Instalado: {Instalada}" : "Não instalado";
            if (Ultima != null) s += $" · versão mais nova: {Ultima}";
            else if (Erro != null) s += $" · não foi possível verificar a versão ({Erro})";
            if (C.Id == "openrct2" && Jogo != null) s += $" · jogo original encontrado: {Jogo}";
            return s + $" · licença {C.Licenca} · github.com/{C.Repo}";
        }
    }
    public string? Jogo { get; set; }
}

/// <summary>Lançador de emuladores e ports (só baixa das Releases oficiais e abre como processo separado).</summary>
public partial class ClassicosPage : UserControl, IPagina
{
    private List<ItemClassico> _itens = new();
    private CancellationTokenSource? _cts;
    private bool _verificado;

    public ClassicosPage()
    {
        InitializeComponent();
        Recarregar();
    }

    public void Mostrar()
    {
        Recarregar();
        if (!_verificado) { _verificado = true; Verificar_Click(this, new RoutedEventArgs()); }
    }

    private void Recarregar()
    {
        var antigos = _itens.ToDictionary(i => i.C.Id);
        _itens = Classicos.Catalogo.Select(c => new ItemClassico
        {
            C = c,
            Instalada = Classicos.VersaoInstalada(c),
            Ultima = antigos.TryGetValue(c.Id, out var a) ? a.Ultima : null,
            Erro = a?.Erro,
            Jogo = a?.Jogo,
        }).ToList();
        Lista.ItemsSource = _itens;
    }

    private void Atualizar() { Lista.ItemsSource = null; Lista.ItemsSource = _itens; }

    private async void Verificar_Click(object sender, RoutedEventArgs e)
    {
        BtnVerificar.IsEnabled = false;
        Status.Text = "Consultando as páginas de Releases no GitHub…";
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(40)).Token;
        var tarefas = _itens.Select(async i =>
        {
            try { i.Ultima = (await Classicos.UltimaRelease(i.C, ct)).Tag; i.Erro = null; }
            catch (Exception ex) { i.Erro = ex is System.Net.Http.HttpRequestException h && h.StatusCode == System.Net.HttpStatusCode.Forbidden ? "limite da API do GitHub, tente mais tarde" : ex.Message; }
        }).ToList();
        var rct = _itens.First(i => i.C.Id == "openrct2");
        var jogo = await Task.Run(Classicos.JogoRct2);
        rct.Jogo = jogo == null ? null : $"{jogo.Name} ({jogo.Store})";
        await Task.WhenAll(tarefas);
        Atualizar();
        int novas = _itens.Count(i => i.Instalado && i.Ultima != null && i.Ultima != i.Instalada);
        Status.Text = novas > 0 ? $"{novas} atualização(ões) disponível(is)." : "Versões verificadas.";
        BtnVerificar.IsEnabled = true;
    }

    private async void Instalar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ItemClassico i }) return;
        if (!i.Instalado && !Ui.Confirmar($"Baixar e instalar o {i.C.Nome}?\n\nOrigem: página oficial de Releases em github.com/{i.C.Repo}\n" +
                                          $"Destino: {Classicos.Pasta(i.C)}\n\nO pacote é conferido pelo SHA-256 publicado antes de ser extraído.\n\n{i.C.Requer}",
                                          "Clássicos")) return;
        _cts = new CancellationTokenSource();
        i.Ocupado = true; Atualizar();
        BtnCancelar.Visibility = Visibility.Visible;
        var prog = new Progress<(string Etapa, double Fracao)>(p => { Status.Text = p.Etapa; Progresso.ScaleX = p.Fracao; });
        try
        {
            Status.Text = await Classicos.Instalar(i.C, prog, _cts.Token);
        }
        catch (OperationCanceledException) { Status.Text = "Cancelado."; }
        catch (Exception ex) { Status.Text = $"Não foi possível instalar o {i.C.Nome}: {ex.Message}"; }
        finally
        {
            i.Ocupado = false;
            BtnCancelar.Visibility = Visibility.Collapsed;
            Progresso.ScaleX = 1;
            Recarregar();
        }
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Abrir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ItemClassico i }) return;
        try
        {
            if (i.C.Id == "openrct2") Status.Text = Classicos.ConfigurarOpenRct2();
            Classicos.Abrir(i.C);
        }
        catch (Exception ex) { Status.Text = $"Não foi possível abrir o {i.C.Nome}: {ex.Message}"; }
    }

    private void Pasta_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ItemClassico i }) Ui.AbrirPasta(Classicos.Exe(i.C) ?? Classicos.Pasta(i.C));
    }

    private void Site_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ItemClassico i }) Ui.AbrirUrl(i.C.Site);
    }
}
