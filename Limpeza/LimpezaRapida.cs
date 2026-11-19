using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Hefesto;

/// <summary>Arquivo de trabalho encontrado fora do lugar (em Temp ou Downloads). Nunca é limpo, só aparece no relatório.</summary>
public sealed record PodeSerSeu(string Caminho, long Tamanho, DateTime Modificado, string Classe, string Motivo)
{
    public string Nome => Path.GetFileName(Caminho);
    public string Pasta => Path.GetDirectoryName(Caminho) ?? "";
    public string TamanhoTexto => Ui.Tam(Tamanho);
}

/// <summary>Contexto de uma varredura: parâmetros, achados "pode ser seu" e itens que a proteção barrou.</summary>
public sealed class ContextoVarredura
{
    public int DiasTemp { get; init; } = 7;
    public int DiasInstaladores { get; init; } = 30;
    public CancellationToken Ct { get; init; }
    public ConcurrentDictionary<string, PodeSerSeu> PodeSerSeu { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentBag<(string Caminho, string Motivo)> Protegidos { get; } = new();
    public ConcurrentBag<string> Avisos { get; } = new();
}

public sealed class Regra
{
    public string Id { get; init; } = "";
    public string Nome { get; init; } = "";
    public string Descricao { get; init; } = "";
    public Func<ContextoVarredura, IEnumerable<ItemLimpeza>> Varrer { get; init; } = _ => Array.Empty<ItemLimpeza>();
}

/// <summary>Resultado de uma regra na tela (marcável).</summary>
public sealed class ResultadoRegra : INotifyPropertyChanged
{
    public Regra Regra { get; init; } = new();
    public List<ItemLimpeza> Itens { get; set; } = new();
    public long Total => Itens.Sum(i => i.Tamanho);
    public string TotalTexto => Itens.Count == 0 ? "nada" : Ui.Tam(Total);
    public string Contagem => Itens.Count == 0 ? "nada encontrado" : Itens.Count == 1 ? "1 item" : $"{Itens.Count:N0} itens";
    public string Nome => Regra.Nome;
    public string Descricao => Regra.Descricao;
    private bool _sel = true;
    public bool Selecionada { get => _sel; set { _sel = value; PropertyChanged?.Invoke(this, new(nameof(Selecionada))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Regras da Limpeza rápida. Cada uma só olha onde costuma haver lixo e respeita a lista de proteção.</summary>
public static class LimpezaRapida
{
    private static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string Dados = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>Extensões de trabalho: se aparecerem em Temp ou Downloads, provavelmente são do usuário.</summary>
    public static readonly HashSet<string> ExtensoesTrabalho = new(StringComparer.OrdinalIgnoreCase)
    {
        ".blend", ".mp4", ".mov", ".mkv", ".md", ".docx", ".doc", ".xlsx", ".pptx", ".odt", ".py", ".ipynb",
        ".pas", ".dpr", ".dfm", ".cs", ".psd", ".kra", ".xcf", ".ai", ".aep", ".prproj", ".fig", ".sketch", ".obj", ".fbx",
    };

    private static readonly HashSet<string> Instaladores = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle" };

    public static List<Regra> Regras() => new()
    {
        new Regra
        {
            Id = "temp", Nome = "Arquivos temporários",
            Descricao = "Pasta Temp do seu usuário e a Temp do Windows. Só arquivos parados há mais dias do que o limite.",
            Varrer = c => ArquivosAntigos(Path.GetTempPath(), c.DiasTemp, c, "temp")
                .Concat(ArquivosAntigos(Path.Combine(WinDir, "Temp"), c.DiasTemp, c, "temp")),
        },
        new Regra
        {
            Id = "dumps", Nome = "Despejos de travamento (CrashDumps)",
            Descricao = "Arquivos .dmp e relatórios de erro do Windows guardados depois de travamentos. Mais antigos que o limite.",
            Varrer = c => ArquivosAntigos(Path.Combine(Local, "CrashDumps"), c.DiasTemp, c, "dumps")
                .Concat(ArquivosAntigos(Path.Combine(WinDir, "Minidump"), c.DiasTemp, c, "dumps"))
                .Concat(ArquivoUnico(Path.Combine(WinDir, "MEMORY.DMP"), c.DiasTemp, c, "dumps"))
                .Concat(PastasAntigas(Path.Combine(Dados, @"Microsoft\Windows\WER\ReportArchive"), c.DiasTemp, c, "dumps"))
                .Concat(PastasAntigas(Path.Combine(Dados, @"Microsoft\Windows\WER\ReportQueue"), c.DiasTemp, c, "dumps"))
                .Concat(PastasAntigas(Path.Combine(Local, @"Microsoft\Windows\WER\ReportArchive"), c.DiasTemp, c, "dumps")),
        },
        new Regra
        {
            Id = "dev", Nome = "Caches de desenvolvimento",
            Descricao = "Caches do npm, pip, NuGet, Yarn e Go. São baixados de novo quando precisar; projetos não são tocados.",
            Varrer = c => new[]
            {
                Path.Combine(Local, "npm-cache", "_cacache"), Path.Combine(Local, "pip", "Cache"),
                Path.Combine(Local, "NuGet", "v3-cache"), Path.Combine(Local, "NuGet", "plugins-cache"),
                Path.Combine(Local, "Yarn", "Cache"), Path.Combine(Local, "go-build"),
            }.SelectMany(d => ConteudoDaPasta(d, c, "dev")),
        },
        new Regra
        {
            Id = "nav", Nome = "Cache dos navegadores",
            Descricao = "Só as pastas Cache do Chrome, Edge, Brave, Vivaldi, Opera e Firefox. Senhas, cookies e histórico ficam protegidos. Feche o navegador antes.",
            Varrer = CacheNavegadores,
        },
        new Regra
        {
            Id = "inst", Nome = "Instaladores antigos em Downloads",
            Descricao = "Arquivos .exe, .msi e .msix na pasta Downloads parados há mais dias do que o limite de instaladores.",
            Varrer = c => InstaladoresAntigos(c),
        },
    };

    /// <summary>Roda as regras escolhidas em paralelo e procura arquivos "pode ser seu" em Temp e Downloads.</summary>
    public static async Task<List<ResultadoRegra>> Analisar(IEnumerable<Regra> regras, ContextoVarredura c)
    {
        var lista = regras.Select(r => new ResultadoRegra { Regra = r }).ToList();
        var tarefas = lista.Select(r => Task.Run(() =>
        {
            try { r.Itens = r.Regra.Varrer(c).ToList(); }
            catch (Exception ex) { c.Avisos.Add($"{r.Regra.Nome}: {ex.Message}"); }
        })).ToList();
        tarefas.Add(Task.Run(() => ProcurarPodeSerSeu(c)));
        await Task.WhenAll(tarefas);
        foreach (var r in lista) r.Selecionada = r.Itens.Count > 0;
        return lista;
    }

    // ---------------- varredura ----------------

    private static readonly EnumerationOptions Recursivo = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
    };
    private static readonly EnumerationOptions Raso = new()
    {
        RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static bool Barrado(string caminho, ContextoVarredura c)
    {
        string? m = Protecao.Motivo(caminho);
        if (m == null) return false;
        c.Protegidos.Add((caminho, m));
        return true;
    }

    private static IEnumerable<ItemLimpeza> ArquivosAntigos(string pasta, int dias, ContextoVarredura c, string regra)
    {
        if (!Directory.Exists(pasta)) yield break;
        var limite = DateTime.Now.AddDays(-dias);
        foreach (var f in new DirectoryInfo(pasta).EnumerateFiles("*", Recursivo))
        {
            if (c.Ct.IsCancellationRequested) yield break;
            FileInfo fi = f;
            DateTime mod;
            long len;
            try { mod = fi.LastWriteTime; len = fi.Length; } catch { continue; }
            if (mod > limite) continue;
            if (ExtensoesTrabalho.Contains(fi.Extension)) continue; // vai para "pode ser seu"
            if (Barrado(fi.FullName, c)) continue;
            yield return new ItemLimpeza { Caminho = fi.FullName, Tamanho = len, Modificado = mod, Regra = regra };
        }
    }

    private static IEnumerable<ItemLimpeza> ArquivoUnico(string arquivo, int dias, ContextoVarredura c, string regra)
    {
        var fi = new FileInfo(arquivo);
        if (!fi.Exists || fi.LastWriteTime > DateTime.Now.AddDays(-dias) || Barrado(arquivo, c)) yield break;
        yield return new ItemLimpeza { Caminho = fi.FullName, Tamanho = fi.Length, Modificado = fi.LastWriteTime, Regra = regra };
    }

    private static IEnumerable<ItemLimpeza> PastasAntigas(string pasta, int dias, ContextoVarredura c, string regra)
    {
        if (!Directory.Exists(pasta)) yield break;
        var limite = DateTime.Now.AddDays(-dias);
        foreach (var d in new DirectoryInfo(pasta).EnumerateDirectories("*", Raso))
        {
            if (d.LastWriteTime > limite || Barrado(d.FullName, c)) continue;
            yield return new ItemLimpeza { Caminho = d.FullName, Tamanho = TamanhoPasta(d.FullName), Modificado = d.LastWriteTime, EhPasta = true, Regra = regra };
        }
    }

    /// <summary>Cada filho direto da pasta (arquivo ou subpasta) vira um item; a pasta em si fica.</summary>
    private static IEnumerable<ItemLimpeza> ConteudoDaPasta(string pasta, ContextoVarredura c, string regra)
    {
        if (!Directory.Exists(pasta)) yield break;
        var di = new DirectoryInfo(pasta);
        foreach (var d in di.EnumerateDirectories("*", Raso))
        {
            if (Barrado(d.FullName, c)) continue;
            long t = TamanhoPasta(d.FullName);
            if (t > 0) yield return new ItemLimpeza { Caminho = d.FullName, Tamanho = t, Modificado = d.LastWriteTime, EhPasta = true, Regra = regra };
        }
        foreach (var f in di.EnumerateFiles("*", Raso))
        {
            if (Barrado(f.FullName, c)) continue;
            yield return new ItemLimpeza { Caminho = f.FullName, Tamanho = f.Length, Modificado = f.LastWriteTime, Regra = regra };
        }
    }

    public static long TamanhoPasta(string pasta)
    {
        long t = 0;
        try { foreach (var f in new DirectoryInfo(pasta).EnumerateFiles("*", Recursivo)) { try { t += f.Length; } catch { } } }
        catch { }
        return t;
    }

    private static IEnumerable<ItemLimpeza> CacheNavegadores(ContextoVarredura c)
    {
        var abertos = new[] { ("chrome", "Chrome"), ("msedge", "Edge"), ("brave", "Brave"), ("vivaldi", "Vivaldi"), ("opera", "Opera"), ("firefox", "Firefox") }
            .Where(p => Process.GetProcessesByName(p.Item1).Length > 0).Select(p => p.Item2).ToList();
        if (abertos.Count > 0)
            c.Avisos.Add($"Navegador aberto ({string.Join(", ", abertos)}): parte do cache em uso não será limpa. Feche-o e analise de novo.");

        string[] chromium =
        {
            @"Google\Chrome\User Data", @"Microsoft\Edge\User Data", @"BraveSoftware\Brave-Browser\User Data", @"Vivaldi\User Data",
        };
        string[] nomesCache = { "Cache", "Code Cache", "GPUCache" };
        foreach (var rel in chromium)
        {
            string ud = Path.Combine(Local, rel);
            if (!Directory.Exists(ud)) continue;
            foreach (var g in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" })
                foreach (var i in ConteudoDaPasta(Path.Combine(ud, g), c, "nav")) yield return i;
            foreach (var perfil in Directory.EnumerateDirectories(ud, "*", Raso))
            {
                string n = Path.GetFileName(perfil);
                if (n != "Default" && !n.StartsWith("Profile ", StringComparison.Ordinal) && n != "Guest Profile") continue;
                foreach (var nc in nomesCache)
                    foreach (var i in ConteudoDaPasta(Path.Combine(perfil, nc), c, "nav")) yield return i;
            }
        }
        foreach (var opera in new[] { @"Opera Software\Opera Stable", @"Opera Software\Opera GX Stable" })
        {
            string b = Path.Combine(Local, opera);
            foreach (var nc in nomesCache.Append("ShaderCache"))
                foreach (var i in ConteudoDaPasta(Path.Combine(b, nc), c, "nav")) yield return i;
        }
        string ff = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(ff))
            foreach (var perfil in Directory.EnumerateDirectories(ff, "*", Raso))
                foreach (var i in ConteudoDaPasta(Path.Combine(perfil, "cache2"), c, "nav")) yield return i;
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    public static string PastaDownloads()
    {
        try
        {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out IntPtr p) == 0)
            {
                string s = Marshal.PtrToStringUni(p)!;
                Marshal.FreeCoTaskMem(p);
                return s;
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private static IEnumerable<ItemLimpeza> InstaladoresAntigos(ContextoVarredura c)
    {
        string dl = PastaDownloads();
        if (!Directory.Exists(dl)) yield break;
        var limite = DateTime.Now.AddDays(-c.DiasInstaladores);
        foreach (var f in new DirectoryInfo(dl).EnumerateFiles("*", Raso))
        {
            if (!Instaladores.Contains(f.Extension) || f.LastWriteTime > limite || Barrado(f.FullName, c)) continue;
            yield return new ItemLimpeza { Caminho = f.FullName, Tamanho = f.Length, Modificado = f.LastWriteTime, Regra = "inst" };
        }
    }

    // ---------------- "pode ser seu" ----------------

    private static void ProcurarPodeSerSeu(ContextoVarredura c)
    {
        var pastas = new List<(string Pasta, EnumerationOptions Op)>
        {
            (Path.GetTempPath(), Recursivo), (Path.Combine(WinDir, "Temp"), Recursivo), (PastaDownloads(), Raso),
        };
        foreach (var (pasta, op) in pastas)
        {
            if (!Directory.Exists(pasta)) continue;
            try
            {
                foreach (var f in new DirectoryInfo(pasta).EnumerateFiles("*", op))
                {
                    if (c.Ct.IsCancellationRequested) return;
                    if (!ExtensoesTrabalho.Contains(f.Extension)) continue;
                    var (classe, motivo) = ClassificarNome(Path.GetFileNameWithoutExtension(f.Name));
                    // Em Downloads, documentos são esperados: só entram os que parecem digitados por uma pessoa.
                    if (op == Raso && classe != "seu") continue;
                    c.PodeSerSeu[f.FullName] = new PodeSerSeu(f.FullName, f.Length, f.LastWriteTime, classe,
                        $"{f.Extension.ToLowerInvariant()} em {(op == Raso ? "Downloads" : "pasta temporária")} · {motivo}");
                }
            }
            catch { }
        }
    }

    private static readonly Regex Guid_ = new(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Hex = new(@"^[0-9a-f]{10,}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Temporario = new(@"^(~|tmp|_|\$|\.)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> NomesDePacote = new(StringComparer.OrdinalIgnoreCase)
        { "readme", "license", "licence", "changelog", "history", "contributing", "index", "main", "setup", "__init__", "notice", "authors", "security" };

    /// <summary>Nome digitado por uma pessoa ("Trabalho final v2") ou gerado por programa (GUID, hex, tmp1234)?
    /// Classe: "seu" (provavelmente seu), "talvez" ou "gerado".</summary>
    public static (string Classe, string Motivo) ClassificarNome(string nome)
    {
        if (Guid_.IsMatch(nome)) return ("gerado", "nome com GUID, típico de programa");
        if (Hex.IsMatch(nome)) return ("gerado", "nome em código hexadecimal");
        if (Temporario.IsMatch(nome)) return ("gerado", "nome de arquivo temporário");
        if (NomesDePacote.Contains(nome)) return ("gerado", "nome padrão de pacote de software");
        int letras = nome.Count(char.IsLetter);
        if (nome.Length > 0 && letras < nome.Length * 0.4) return ("gerado", "nome quase só com números e símbolos");
        bool acento = nome.Any(ch => ch > 127 && char.IsLetter(ch));
        var palavras = Regex.Split(nome, @"[\s_\-\.]+").Where(p => p.Count(char.IsLetter) >= 2 && p.Any(ch => "aeiouAEIOU".Contains(ch))).ToList();
        if (nome.Contains(' ') || acento || palavras.Count >= 2) return ("seu", "nome escrito por uma pessoa");
        return ("talvez", "nome curto, pode ser seu");
    }
}
