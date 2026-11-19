using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Hefesto;

/// <summary>Backup de saves em .zip (Documentos\Hefesto\Backups de saves).</summary>
public partial class BackupSavesPage : UserControl, IPagina
{
    private List<FonteSave> _fontes = new();
    private bool _carregado;

    public BackupSavesPage() => InitializeComponent();

    public void Mostrar()
    {
        if (!_carregado) Procurar_Click(this, new RoutedEventArgs());
    }

    private async void Procurar_Click(object sender, RoutedEventArgs e)
    {
        _carregado = true;
        Status.Text = "Procurando saves…";
        var extras = AppSettings.Atual.BackupPastasExtras.ToList();
        _fontes = await Task.Run(() => BackupSaves.Fontes(extras));
        foreach (var f in _fontes) f.PropertyChanged += (_, _) => AtualizarTotal();
        Lista.ItemsSource = _fontes;
        AtualizarTotal();
        int zips = Directory.Exists(BackupSaves.PastaBackups) ? Directory.GetFiles(BackupSaves.PastaBackups, "saves-*.zip").Length : 0;
        Status.Text = _fontes.Count == 0 ? "Nenhuma pasta de saves encontrada. Use ADICIONAR PASTA."
                    : $"{_fontes.Count} pastas de saves encontradas." + (zips > 0 ? $" Você já tem {zips} backup(s) em {BackupSaves.PastaBackups}." : "");
    }

    private void AtualizarTotal()
    {
        var sel = _fontes.Where(f => f.Selecionada).ToList();
        Total.Text = Ui.Tam(sel.Sum(f => f.Tamanho));
        BtnBackup.IsEnabled = sel.Count > 0;
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        BtnBackup.IsEnabled = false;
        var prog = new Progress<(int Feitos, int Total)>(p =>
        {
            Progresso.ScaleX = p.Total == 0 ? 1 : (double)p.Feitos / p.Total;
            Status.Text = $"Compactando… {p.Feitos:N0} de {p.Total:N0} arquivos";
        });
        try
        {
            var (zip, n, falhas) = await BackupSaves.Criar(_fontes, prog, CancellationToken.None);
            Status.Text = $"Backup pronto: {zip} ({n:N0} arquivos, {Ui.Tam(new FileInfo(zip).Length)})."
                          + (falhas.Count > 0 ? $" {falhas.Count} arquivo(s) em uso ficaram de fora (feche o jogo e repita): {falhas[0]}" : "");
            Ui.AbrirPasta(zip);
        }
        catch (Exception ex) { Status.Text = "Erro no backup: " + ex.Message; }
        finally { Progresso.ScaleX = 1; BtnBackup.IsEnabled = true; }
    }

    private void Adicionar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta de saves para incluir no backup" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        var s = AppSettings.Atual;
        if (!s.BackupPastasExtras.Contains(dlg.FolderName, StringComparer.OrdinalIgnoreCase)) s.BackupPastasExtras.Add(dlg.FolderName);
        try { s.Save(); } catch { }
        Procurar_Click(sender, e);
    }

    private void AbrirBackups_Click(object sender, RoutedEventArgs e) => Ui.AbrirPasta(BackupSaves.PastaBackups);

    private void Mostrar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string p }) Ui.AbrirPasta(p);
    }
}
