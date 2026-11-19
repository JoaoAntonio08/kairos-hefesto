using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Hefesto;

/// <summary>Uma entrada do winapp2.ini (um programa ou tipo de lixo).</summary>
public sealed class EntradaWinapp2
{
    public string Nome { get; set; } = "";
    public string Secao { get; set; } = "";
    public string? Aviso { get; set; }
    public List<string> Detect { get; } = new();
    public List<string> DetectFile { get; } = new();
    public List<string> FileKeys { get; } = new();
    public List<string> Excludes { get; } = new();
    public int RegKeys { get; set; }
}

/// <summary>
/// Regras do winapp2.ini (CC BY-SA 4.0, projeto Winapp2, github.com/MoscaDotTo/Winapp2). O arquivo é baixado do repositório
/// oficial quando a página é aberta, conferido pelo hash do Git que a API do GitHub informa, guardado em
/// %LocalAppData%\Hefesto\winapp2 e nunca embutido no programa. Só as regras de ARQUIVO são usadas; chaves de registro
/// (RegKey) são ignoradas de propósito. Cada item ainda passa pela lista de proteção do Hefesto.
/// </summary>
public static class Winapp2
{
    public const string Url = "https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master/Winapp2.ini";
    private const string Api = "https://api.github.com/repos/MoscaDotTo/Winapp2/contents/Winapp2.ini";
    public static string Arquivo => Path.Combine(Ui.PastaLocal("winapp2"), "winapp2.ini");

    public static string? VersaoLocal()
    {
        if (!File.Exists(Arquivo)) return null;
        foreach (var l in File.ReadLines(Arquivo).Take(5))
            if (l.StartsWith("; Version:", StringComparison.OrdinalIgnoreCase)) return l[10..].Trim();
        return "?";
    }

    /// <summary>Baixa o winapp2.ini se não houver cópia ou se ela tiver mais de 7 dias (ou se forçar).</summary>
    public static async Task<string> Atualizar(bool forcar, CancellationToken ct)
    {
        if (!forcar && File.Exists(Arquivo) && File.GetLastWriteTime(Arquivo) > DateTime.Now.AddDays(-7))
            return "winapp2.ini já está atualizado";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Hefesto");
        byte[] dados = await http.GetByteArrayAsync(Url, ct);

        // Confere o conteúdo com o SHA-1 de blob do Git informado pela API oficial do GitHub.
        string verif = "sem verificação (API do GitHub indisponível)";
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Api, ct));
            string esperado = doc.RootElement.GetProperty("sha").GetString() ?? "";
            byte[] cab = Encoding.ASCII.GetBytes($"blob {dados.Length}\0");
            string calc = Convert.ToHexString(SHA1.HashData(cab.Concat(dados).ToArray())).ToLowerInvariant();
            if (esperado.Length > 0 && calc != esperado)
                throw new InvalidDataException("o arquivo baixado não confere com o hash do repositório oficial");
            verif = "conferido com o hash do repositório oficial";
        }
        catch (InvalidDataException) { throw; }
        catch { }

        string tmp = Arquivo + ".novo";
        await File.WriteAllBytesAsync(tmp, dados, ct);
        File.Move(tmp, Arquivo, true);
        return $"winapp2.ini baixado ({Ui.Tam(dados.Length)}, {verif})";
    }

    public static List<EntradaWinapp2> Ler()
    {
        var lista = new List<EntradaWinapp2>();
        if (!File.Exists(Arquivo)) return lista;
        EntradaWinapp2? atual = null;
        foreach (var bruta in File.ReadLines(Arquivo))
        {
            string l = bruta.Trim();
            if (l.Length == 0 || l[0] == ';') continue;
            if (l[0] == '[' && l.EndsWith(']'))
            {
                atual = new EntradaWinapp2 { Nome = l[1..^1].TrimEnd(' ', '*').Trim() };
                lista.Add(atual);
                continue;
            }
            if (atual == null) continue;
            int eq = l.IndexOf('=');
            if (eq <= 0) continue;
            string k = l[..eq].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'), v = l[(eq + 1)..].Trim();
            switch (k.ToLowerInvariant())
            {
                case "section": atual.Secao = v; break;
                case "warning": atual.Aviso = v; break;
                case "detect": atual.Detect.Add(v); break;
                case "detectfile": atual.DetectFile.Add(v); break;
                case "filekey": atual.FileKeys.Add(v); break;
                case "excludekey": atual.Excludes.Add(v); break;
                case "regkey": atual.RegKeys++; break;
            }
        }
        return lista;
    }

    // ---------------- variáveis e curingas ----------------

    private static readonly string Perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly Dictionary<string, string[]> Vars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["%LocalAppData%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) },
        ["%AppData%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) },
        ["%LocalLowAppData%"] = new[] { Path.Combine(Perfil, "AppData", "LocalLow") },
        ["%CommonAppData%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) },
        ["%ProgramData%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) },
        ["%ProgramFiles%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct().ToArray(),
        ["%CommonProgramFiles%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles) },
        ["%Documents%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
        ["%Pictures%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) },
        ["%Music%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) },
        ["%Video%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) },
        ["%Desktop%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
        ["%UserProfile%"] = new[] { Perfil },
        ["%WinDir%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows) },
        ["%SystemRoot%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows) },
        ["%SystemDrive%"] = new[] { Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!.TrimEnd('\\') },
        ["%Public%"] = new[] { Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public" },
        ["%AllUsersProfile%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) },
        ["%Temp%"] = new[] { Path.GetTempPath().TrimEnd('\\') },
        ["%Tmp%"] = new[] { Path.GetTempPath().TrimEnd('\\') },
    };

    /// <summary>Expande variáveis e curingas de pasta (ex.: %LocalAppData%\Google\Chrome*\User Data\*) em pastas reais.</summary>
    public static List<string> ExpandirPastas(string caminho)
    {
        var bases = new List<string> { caminho };
        int a = caminho.IndexOf('%'), b = a >= 0 ? caminho.IndexOf('%', a + 1) : -1;
        if (a >= 0 && b > a)
        {
            string v = caminho[a..(b + 1)];
            if (Vars.TryGetValue(v, out var vals)) bases = vals.Where(x => x.Length > 0).Select(x => caminho.Replace(v, x, StringComparison.OrdinalIgnoreCase)).ToList();
            else bases = new List<string> { Environment.ExpandEnvironmentVariables(caminho) };
        }
        var res = new List<string>();
        foreach (var bp in bases)
        {
            if (bp.Contains('%')) continue;
            var partes = bp.TrimEnd('\\').Split('\\');
            var atuais = new List<string> { partes[0] + "\\" };
            for (int i = 1; i < partes.Length && atuais.Count > 0; i++)
            {
                string seg = partes[i];
                var prox = new List<string>();
                foreach (var cur in atuais)
                {
                    if (seg.Contains('*') || seg.Contains('?'))
                    {
                        try { prox.AddRange(Directory.EnumerateDirectories(cur, seg, Raso)); } catch { }
                    }
                    else
                    {
                        string p = Path.Combine(cur, seg);
                        if (Directory.Exists(p)) prox.Add(p);
                    }
                }
                atuais = prox;
            }
            res.AddRange(atuais.Where(x => x.Length > 3));
        }
        return res.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly EnumerationOptions Raso = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
    private static readonly EnumerationOptions Fundo = new() { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };

    public static bool Detectado(EntradaWinapp2 e)
    {
        if (e.Detect.Count == 0 && e.DetectFile.Count == 0) return true; // entradas sem Detect valem sempre (ex.: limpezas do Windows)
        foreach (var d in e.Detect) if (ChaveExiste(d)) return true;
        foreach (var f in e.DetectFile)
        {
            string pasta = f, arq = "";
            int bar = f.LastIndexOf('\\');
            // DetectFile pode apontar para pasta (com ou sem curinga) ou arquivo
            if (ExpandirPastas(pasta).Count > 0) return true;
            if (bar > 0)
            {
                pasta = f[..bar]; arq = f[(bar + 1)..];
                foreach (var p in ExpandirPastas(pasta))
                    try { if (Directory.EnumerateFiles(p, arq, Raso).Any()) return true; } catch { }
            }
        }
        return false;
    }

    private static bool ChaveExiste(string chave)
    {
        try
        {
            int i = chave.IndexOf('\\');
            if (i < 0) return false;
            string h = chave[..i].ToUpperInvariant(), sub = chave[(i + 1)..];
            RegistryKey? raiz = h switch
            {
                "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
                "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                "HKU" or "HKEY_USERS" => Registry.Users,
                _ => null,
            };
            if (raiz == null) return false;
            using var k = raiz.OpenSubKey(sub);
            if (k != null) return true;
            if (raiz == Registry.LocalMachine)
            {
                using var k32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(sub);
                return k32 != null;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Arquivos (e pastas, com REMOVESELF) que a entrada limparia, já filtrados pelos ExcludeKey e pela proteção.</summary>
    public static List<ItemLimpeza> Itens(EntradaWinapp2 e, ContextoVarredura c)
    {
        var itens = new Dictionary<string, ItemLimpeza>(StringComparer.OrdinalIgnoreCase);
        var excl = e.Excludes.Select(x => x.Split('|')).Where(x => x.Length >= 2).ToList();
        bool Excluido(string caminho)
        {
            foreach (var x in excl)
            {
                string tipo = x[0].ToUpperInvariant();
                if (tipo == "REG") continue;
                foreach (var p in ExpandirPastas(x[1]))
                {
                    if (!caminho.StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (tipo == "PATH" || x.Length < 3) return true;
                    string nome = Path.GetFileName(caminho);
                    if (x[2].Split(';').Any(pad => FileSystemName.Combina(pad, nome))) return true;
                }
            }
            return false;
        }

        foreach (var fk in e.FileKeys)
        {
            if (c.Ct.IsCancellationRequested) break;
            var partes = fk.Split('|');
            string padroes = partes.Length > 1 ? partes[1] : "*";
            if (padroes.Contains("RemoveEmptyFoldersOnly", StringComparison.OrdinalIgnoreCase)) continue; // só remove pastas vazias: não vale a pena
            bool recurse = partes.Skip(2).Any(p => p.Equals("RECURSE", StringComparison.OrdinalIgnoreCase));
            bool removeSelf = partes.Skip(2).Any(p => p.Equals("REMOVESELF", StringComparison.OrdinalIgnoreCase));
            foreach (var pasta in ExpandirPastas(partes[0]))
            {
                if (removeSelf && padroes == "*")
                {
                    if (Excluido(pasta + "\\x")) continue;
                    string? m = Protecao.Motivo(pasta);
                    if (m != null) { c.Protegidos.Add((pasta, m)); continue; }
                    var di = new DirectoryInfo(pasta);
                    itens[pasta] = new ItemLimpeza { Caminho = pasta, EhPasta = true, Tamanho = LimpezaRapida.TamanhoPasta(pasta), Modificado = di.LastWriteTime, Regra = e.Nome };
                    continue;
                }
                foreach (var pad in padroes.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    IEnumerable<FileInfo> arqs;
                    try { arqs = new DirectoryInfo(pasta).EnumerateFiles(pad, recurse || removeSelf ? Fundo : Raso).ToList(); }
                    catch { continue; }
                    foreach (var f in arqs)
                    {
                        if (itens.ContainsKey(f.FullName) || Excluido(f.FullName)) continue;
                        if (LimpezaRapida.ExtensoesTrabalho.Contains(f.Extension)) continue;
                        string? m = Protecao.Motivo(f.FullName);
                        if (m != null) { c.Protegidos.Add((f.FullName, m)); continue; }
                        try { itens[f.FullName] = new ItemLimpeza { Caminho = f.FullName, Tamanho = f.Length, Modificado = f.LastWriteTime, Regra = e.Nome }; }
                        catch { }
                    }
                }
            }
        }
        return itens.Values.ToList();
    }

    /// <summary>Detecta as entradas que se aplicam a este PC e calcula o que cada uma limparia.</summary>
    public static async Task<List<ResultadoRegra>> Analisar(ContextoVarredura c, IProgress<(int, int)>? prog)
    {
        var todas = Ler();
        var detectadas = todas.Where(e => e.FileKeys.Count > 0).ToList();
        var res = new System.Collections.Concurrent.ConcurrentBag<ResultadoRegra>();
        int feitos = 0;
        await Task.Run(() => Parallel.ForEach(detectadas, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = c.Ct }, e =>
        {
            try
            {
                if (Detectado(e))
                {
                    var itens = Itens(e, c);
                    if (itens.Count > 0)
                        res.Add(new ResultadoRegra
                        {
                            Regra = new Regra
                            {
                                Id = "w2:" + e.Nome, Nome = e.Nome,
                                Descricao = (string.IsNullOrEmpty(e.Secao) ? "winapp2" : e.Secao)
                                            + (e.Aviso != null ? " · ATENÇÃO: " + e.Aviso : "")
                                            + (e.RegKeys > 0 ? " · as chaves de registro desta regra são ignoradas" : ""),
                            },
                            Itens = itens,
                            Selecionada = false,
                        });
                }
            }
            catch { }
            int f = Interlocked.Increment(ref feitos);
            if (f % 50 == 0) prog?.Report((f, detectadas.Count));
        }));
        return res.OrderByDescending(r => r.Total).ToList();
    }
}

/// <summary>Curingas * e ? de nome de arquivo, sem diferenciar maiúsculas.</summary>
internal static class FileSystemName
{
    public static bool Combina(string padrao, string nome)
        => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(padrao, nome, ignoreCase: true);
}
