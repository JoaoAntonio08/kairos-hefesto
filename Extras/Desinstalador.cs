using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Hefesto;

/// <summary>Programa instalado, lido das chaves Uninstall do registro.</summary>
public sealed class ProgramaInstalado
{
    public string Nome { get; init; } = "";
    public string Editor { get; init; } = "";
    public string Versao { get; init; } = "";
    public string? Pasta { get; init; }
    public string Desinstalar { get; init; } = "";
    public long Tamanho { get; init; }
    public DateTime? Instalado { get; init; }
    public string Origem { get; init; } = "";
    public string TamanhoTexto => Tamanho > 0 ? Ui.Tam(Tamanho) : "";
    public string Detalhe => string.Join(" · ", new[] { Editor, Versao, Instalado?.ToString("dd/MM/yyyy") ?? "" }.Where(s => s.Length > 0));
}

/// <summary>
/// Desinstalador com limpeza de sobras. Inspirado no fluxo do Bulk Crap Uninstaller (Apache 2.0), sem código dele:
/// roda o desinstalador oficial do programa e depois procura pastas que ficaram para trás, que vão para a Lixeira
/// (nunca exclusão direta) só depois de o usuário conferir. O registro não é alterado.
/// </summary>
public static class Desinstalador
{
    private const string Chave = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<ProgramaInstalado> Listar()
    {
        var lista = new Dictionary<string, ProgramaInstalado>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view, origem) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64, "todos os usuários"),
                     (RegistryHive.LocalMachine, RegistryView.Registry32, "todos os usuários (32 bits)"),
                     (RegistryHive.CurrentUser, RegistryView.Default, "só este usuário"),
                 })
        {
            try
            {
                using var raiz = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(Chave);
                if (raiz == null) continue;
                foreach (var sub in raiz.GetSubKeyNames())
                {
                    try
                    {
                        using var k = raiz.OpenSubKey(sub);
                        if (k == null) continue;
                        string nome = (k.GetValue("DisplayName") as string ?? "").Trim();
                        string unin = (k.GetValue("UninstallString") as string ?? "").Trim();
                        if (nome.Length == 0 || unin.Length == 0) continue;
                        if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (k.GetValue("ParentKeyName") != null || k.GetValue("ReleaseType") is string rt && rt.Contains("Update")) continue;
                        DateTime? data = null;
                        if (k.GetValue("InstallDate") is string d && DateTime.TryParseExact(d, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dt)) data = dt;
                        var p = new ProgramaInstalado
                        {
                            Nome = nome,
                            Editor = (k.GetValue("Publisher") as string ?? "").Trim(),
                            Versao = (k.GetValue("DisplayVersion") as string ?? "").Trim(),
                            Pasta = (k.GetValue("InstallLocation") as string)?.Trim().Trim('"') is string il && il.Length > 3 ? il.TrimEnd('\\') : null,
                            Desinstalar = unin,
                            Tamanho = k.GetValue("EstimatedSize") is int kb ? kb * 1024L : 0,
                            Instalado = data,
                            Origem = origem,
                        };
                        lista.TryAdd(nome + "|" + p.Versao, p);
                    }
                    catch { }
                }
            }
            catch { }
        }
        return lista.Values.OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Separa executável e argumentos de um UninstallString (com ou sem aspas; MsiExec vira msiexec /x).</summary>
    public static (string Exe, string Args) Comando(string s)
    {
        s = s.Trim();
        var msi = Regex.Match(s, @"msiexec(\.exe)?\s+/[IX]\s*(\{[0-9A-F\-]+\})", RegexOptions.IgnoreCase);
        if (msi.Success) return ("msiexec.exe", "/x " + msi.Groups[2].Value);
        if (s.StartsWith('"'))
        {
            int f = s.IndexOf('"', 1);
            if (f > 0) return (s[1..f], s[(f + 1)..].Trim());
        }
        // Sem aspas: tenta o maior prefixo que é um arquivo existente (caminhos com espaço).
        var partes = s.Split(' ');
        for (int i = partes.Length; i > 0; i--)
        {
            string cand = string.Join(' ', partes.Take(i));
            if (File.Exists(cand)) return (cand, string.Join(' ', partes.Skip(i)));
        }
        return (partes[0], string.Join(' ', partes.Skip(1)));
    }

    /// <summary>Roda o desinstalador oficial e espera ele (e os processos que ele abrir a partir do %TEMP%) terminar.</summary>
    public static async Task<int> Executar(ProgramaInstalado p, CancellationToken ct)
    {
        var (exe, args) = Comando(p.Desinstalar);
        using var proc = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true })
                         ?? throw new InvalidOperationException("o desinstalador não abriu");
        await proc.WaitForExitAsync(ct);
        // Muitos desinstaladores (NSIS, Inno) se copiam para %TEMP% e abrem outro processo: espera eles também.
        for (int i = 0; i < 1800 && !ct.IsCancellationRequested; i++)
        {
            bool algum = Process.GetProcesses().Any(x =>
            {
                try { return Regex.IsMatch(x.ProcessName, @"^(Au_|Un_A|unins\d*|uninst|uninstall|_iu14D2N|Un_)", RegexOptions.IgnoreCase); }
                catch { return false; }
            });
            if (!algum) break;
            await Task.Delay(1000, ct);
        }
        return proc.ExitCode;
    }

    private static string Normal(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]", "");

    /// <summary>Nome sem versão, arquitetura e idioma (ex.: "7-Zip 24.08 (x64)" → "7-Zip").</summary>
    public static string NomeBase(string nome)
    {
        string n = Regex.Replace(nome, @"\(.*?\)|\[.*?\]", " ");
        n = Regex.Replace(n, @"\bv?\d+(\.\d+)+\b.*$", " ");
        n = Regex.Replace(n, @"\b(x64|x86|64-bit|32-bit|pt-br|en-us)\b", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(n, @"\s+", " ").Trim(' ', '-', '_');
    }

    /// <summary>Pastas que sobraram depois da desinstalação. Só nomes que batem exatamente com o programa (ou dentro da pasta do fabricante).</summary>
    public static List<ItemLimpeza> Sobras(ProgramaInstalado p)
    {
        var achados = new Dictionary<string, ItemLimpeza>(StringComparer.OrdinalIgnoreCase);
        string alvo = Normal(NomeBase(p.Nome));
        string editor = Normal(Regex.Replace(p.Editor, @"\b(inc|ltd|llc|gmbh|corp|corporation|co|s\.?a\.?)\b\.?", "", RegexOptions.IgnoreCase));
        if (alvo.Length < 3) return new();

        void Add(string pasta)
        {
            try
            {
                if (!Directory.Exists(pasta) || achados.ContainsKey(pasta)) return;
                var di = new DirectoryInfo(pasta);
                if (di.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
                if (Protecao.Motivo(pasta, permitirProgramas: true) != null) return;
                achados[pasta] = new ItemLimpeza { Caminho = pasta, EhPasta = true, Tamanho = LimpezaRapida.TamanhoPasta(pasta), Modificado = di.LastWriteTime, Regra = p.Nome };
            }
            catch { }
        }

        if (p.Pasta != null && Path.GetPathRoot(p.Pasta)?.TrimEnd('\\') != p.Pasta) Add(p.Pasta);
        var bases = new[]
        {
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
        }.Select(Environment.GetFolderPath).Where(b => b.Length > 0)
         .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"))
         .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"))
         .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var b in bases)
        {
            if (!Directory.Exists(b)) continue;
            List<string> dirs;
            try { dirs = Directory.EnumerateDirectories(b).ToList(); } catch { continue; }
            foreach (var d in dirs)
            {
                string n = Normal(Path.GetFileName(d));
                if (n == alvo) { Add(d); continue; }
                // Pasta do fabricante: procura o programa dentro dela.
                if (editor.Length >= 3 && n == editor)
                {
                    try
                    {
                        foreach (var s in Directory.EnumerateDirectories(d))
                            if (Normal(Path.GetFileName(s)) == alvo) Add(s);
                    }
                    catch { }
                }
            }
        }
        return achados.Values.OrderByDescending(i => i.Tamanho).ToList();
    }

    public static bool AindaInstalado(ProgramaInstalado p) => Listar().Any(x => x.Nome == p.Nome && x.Versao == p.Versao);
}
