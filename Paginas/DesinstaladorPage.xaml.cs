using System.Windows;
using System.Windows.Controls;

namespace Hefesto;

public sealed class SobraVm
{
    public ItemLimpeza Item { get; init; } = new();
    public bool Selecionada { get; set; } = true;
    public string Tamanho => Ui.Tam(Item.Tamanho);
}

/// <summary>Desinstalador com limpeza de sobras (pastas vão para a Lixeira depois de conferidas).</summary>
public partial class DesinstaladorPage : UserControl, IPagina
{
    private List<ProgramaInstalado> _todos = new();
    private List<SobraVm> _sobras = new();
    private bool _carregado, _ocupado;

    public DesinstaladorPage() => InitializeComponent();

    public void Mostrar()
    {
        if (!_carregado) Recarregar_Click(this, new RoutedEventArgs());
    }

    private async void Recarregar_Click(object sender, RoutedEventArgs e)
    {
        _carregado = true;
        Status.Text = "Lendo a lista de programas…";
        _todos = await Task.Run(Desinstalador.Listar);
        Filtrar();
    }

    private void Busca_TextChanged(object sender, TextChangedEventArgs e) => Filtrar();

    private void Filtrar()
    {
        string q = Busca.Text.Trim();
        var l = q.Length == 0 ? _todos : _todos.Where(p => p.Nome.Contains(q, StringComparison.CurrentCultureIgnoreCase) || p.Editor.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        Lista.ItemsSource = l;
        if (!_ocupado) Status.Text = $"{l.Count:N0} de {_todos.Count:N0} programas" + (q.Length > 0 ? $" com \"{q}\"" : "") + ".";
    }

    private async void Desinstalar_Click(object sender, RoutedEventArgs e)
    {
        if (_ocupado || sender is not FrameworkElement { Tag: ProgramaInstalado p }) return;
        if (!Ui.Confirmar($"Desinstalar {p.Nome}?\n\nO desinstalador do próprio programa vai abrir; siga as telas dele. Depois o Hefesto " +
                          "procura pastas que sobraram e mostra para você decidir.", "Desinstalador")) return;
        _ocupado = true;
        PainelSobras.Visibility = Visibility.Collapsed;
        Status.Text = $"Desinstalando {p.Nome}… (siga a janela do desinstalador)";
        try
        {
            int codigo = await Desinstalador.Executar(p, CancellationToken.None);
            if (await Task.Run(() => Desinstalador.AindaInstalado(p)))
            {
                Status.Text = $"{p.Nome} continua instalado (desinstalação cancelada ou pendente de reinício, código {codigo}). Nenhuma sobra foi procurada.";
                return;
            }
            Status.Text = $"{p.Nome} foi desinstalado. Procurando sobras…";
            var sobras = await Task.Run(() => Desinstalador.Sobras(p));
            _sobras = sobras.Select(s => new SobraVm { Item = s }).ToList();
            ListaSobras.ItemsSource = _sobras;
            PainelSobras.Visibility = Visibility.Visible;
            SobrasTitulo.Text = sobras.Count == 0 ? "Nenhuma sobra encontrada" : $"Sobras de {Desinstalador.NomeBase(p.Nome)}: {Ui.Tam(sobras.Sum(s => s.Tamanho))}";
            SobrasSub.Text = sobras.Count == 0
                ? "O desinstalador limpou tudo o que o Hefesto sabe procurar."
                : "Pastas com o nome do programa que ficaram depois da desinstalação. Confira com MOSTRAR antes; desmarque o que quiser manter (ex.: saves ou configurações que você vai usar de novo).";
            BtnSobras.Visibility = sobras.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            Status.Text = $"{p.Nome} foi desinstalado.";
        }
        catch (Exception ex) { Status.Text = $"Não foi possível desinstalar {p.Nome}: {ex.Message}"; }
        finally
        {
            _ocupado = false;
            _todos = await Task.Run(Desinstalador.Listar);
            string st = Status.Text;
            Filtrar();
            Status.Text = st;
        }
    }

    private async void Sobras_Click(object sender, RoutedEventArgs e)
    {
        var itens = _sobras.Where(s => s.Selecionada).Select(s => s.Item).ToList();
        if (itens.Count == 0) return;
        if (!Ui.Confirmar($"Enviar {itens.Count} pasta(s) ({Ui.Tam(itens.Sum(i => i.Tamanho))}) para a Lixeira?\n\n" +
                          string.Join("\n", itens.Select(i => "• " + i.Caminho)), "Desinstalador")) return;
        BtnSobras.IsEnabled = false;
        try
        {
            var res = await Lixeira.RemoverAsync(itens, true, null, CancellationToken.None);
            long ok = res.Where(r => r.Destino is Destino.Lixeira or Destino.Quarentena).Sum(r => r.Item.Tamanho);
            var falhas = res.Where(r => r.Destino is Destino.Falhou or Destino.Protegido).ToList();
            Status.Text = $"{Ui.Tam(ok)} de sobras foram para a Lixeira." + (falhas.Count > 0 ? " Não removidos: " + string.Join("; ", falhas.Select(f => f.Item.Caminho + " (" + f.Detalhe + ")")) : "");
            PainelSobras.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { Status.Text = "Erro: " + ex.Message; }
        finally { BtnSobras.IsEnabled = true; }
    }

    private void FecharSobras_Click(object sender, RoutedEventArgs e) => PainelSobras.Visibility = Visibility.Collapsed;

    private void Mostrar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string p }) Ui.AbrirPasta(p);
    }
}
