using System.IO;
using System.IO.Compression;

namespace Hefesto;

/// <summary>Pasta de saves encontrada, com tamanho e nome para o backup.</summary>
public sealed class FonteSave : System.ComponentModel.INotifyPropertyChanged
{
    public string Nome { get; init; } = "";
    public string Caminho { get; init; } = "";
    /// <summary>Caminho dentro do .zip.</summary>
    public string Destino { get; init; } = "";
    public long Tamanho { get; set; }
    public int Arquivos { get; set; }
    private bool _sel = true;
    public bool Selecionada { get => _sel; set { _sel = value; PropertyChanged?.Invoke(this, new(nameof(Selecionada))); } }
    public string TamanhoTexto => Ui.Tam(Tamanho);
    public string Detalhe => $"{Arquivos:N0} arquivos · {Caminho}";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Backup de saves: Saved Games, Documentos\My Games, saves na nuvem da Steam (userdata\&lt;conta&gt;\&lt;app&gt;\remote) e as
/// pastas que o usuário adicionar. Gera um .zip em Documentos\Hefesto\Backups de saves. Só lê as pastas originais.
/// </summary>
public static class BackupSaves
{
    public static string PastaBackups => Ui.PastaDocumentos("Backups de saves");

    public static List<FonteSave> Fontes(IEnumerable<string> extras)
    {
        var l = new List<FonteSave>();
        string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        void Pasta(string nome, string caminho, string destino)
        {
            if (!Directory.Exists(caminho)) return;
            var (t, n) = Medir(caminho);
            if (n > 0) l.Add(new FonteSave { Nome = nome, Caminho = caminho, Destino = destino, Tamanho = t, Arquivos = n });
        }

        Pasta("Saved Games", Path.Combine(perfil, "Saved Games"), "Saved Games");
        string myGames = Path.Combine(docs, "My Games");
        if (Directory.Exists(myGames))
            foreach (var d in SafeDirs(myGames)) Pasta("My Games · " + Path.GetFileName(d), d, Path.Combine("My Games", Path.GetFileName(d)));

        // Steam: userdata\<conta>\<appid>\remote, com o nome do jogo pelo appmanifest.
        string? steam = SteamInfo.SteamPath();
        if (steam != null)
        {
            var nomes = NomesSteam(steam);
            string ud = Path.Combine(steam, "userdata");
            foreach (var conta in SafeDirs(ud))
                foreach (var app in SafeDirs(conta))
                {
                    string id = Path.GetFileName(app);
                    if (id is "7" or "760" or "241100" or "config") continue; // configurações, capturas de tela, ajustes de controle
                    string remote = Path.Combine(app, "remote");
                    string nome = nomes.TryGetValue(id, out var n) ? n : $"jogo {id} (não instalado)";
                    Pasta("Steam · " + nome, remote, Path.Combine("Steam", Path.GetFileName(conta), id + " " + Limpo(nome)));
                }
        }

        foreach (var e in extras.Distinct(StringComparer.OrdinalIgnoreCase))
            Pasta("Sua pasta · " + Path.GetFileName(e.TrimEnd('\\')), e, Path.Combine("Outras", Limpo(Path.GetFileName(e.TrimEnd('\\')))));
        return l;
    }

    private static string Limpo(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    private static IEnumerable<string> SafeDirs(string p)
    {
        try { return Directory.Exists(p) ? Directory.GetDirectories(p) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static (long, int) Medir(string p)
    {
        long t = 0; int n = 0;
        try
        {
            foreach (var f in new DirectoryInfo(p).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            { t += f.Length; n++; }
        }
        catch { }
        return (t, n);
    }

    private static Dictionary<string, string> NomesSteam(string steam)
    {
        var d = new Dictionary<string, string>();
        foreach (var lib in SteamInfo.LibraryFolders(steam))
        {
            string apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var st = Vdf.Parse(File.ReadAllText(acf)).Child("AppState");
                    if (st?.Get("appid") is string id && st.Get("name") is string nome) d[id] = nome;
                }
                catch { }
            }
        }
        return d;
    }

    /// <summary>Compacta as fontes marcadas num .zip novo. Arquivos em uso são copiados se der; senão ficam de fora (listados).</summary>
    public static async Task<(string Zip, int Arquivos, List<string> Falhas)> Criar(IEnumerable<FonteSave> fontes, IProgress<(int, int)>? prog, CancellationToken ct)
    {
        var sel = fontes.Where(f => f.Selecionada).ToList();
        int total = sel.Sum(f => f.Arquivos), feitos = 0;
        var falhas = new List<string>();
        string zip = Path.Combine(PastaBackups, $"saves-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        await Task.Run(() =>
        {
            using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
            foreach (var f in sel)
            {
                var op = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var arq in Directory.EnumerateFiles(f.Caminho, "*", op))
                {
                    ct.ThrowIfCancellationRequested();
                    string rel = Path.Combine(f.Destino, Path.GetRelativePath(f.Caminho, arq)).Replace('\\', '/');
                    try
                    {
                        var e = z.CreateEntry(rel, CompressionLevel.Optimal);
                        e.LastWriteTime = File.GetLastWriteTime(arq);
                        using var src = new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var dst = e.Open();
                        src.CopyTo(dst);
                    }
                    catch (Exception ex) { falhas.Add($"{arq}: {ex.Message}"); }
                    if (++feitos % 25 == 0) prog?.Report((feitos, total));
                }
            }
            var leia = z.CreateEntry("LEIA-ME.txt");
            using var w = new StreamWriter(leia.Open());
            w.WriteLine($"Backup de saves feito pelo Hefesto em {DateTime.Now:dd/MM/yyyy HH:mm} no PC {Environment.MachineName}.");
            w.WriteLine("Para restaurar, copie a pasta do jogo de volta para o lugar de origem:");
            foreach (var f in sel) w.WriteLine($"  {f.Destino}  ->  {f.Caminho}");
        }, ct);
        return (zip, feitos - falhas.Count, falhas);
    }
}
