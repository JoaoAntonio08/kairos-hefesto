using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Hefesto;

/// <summary>Um emulador ou port: sempre baixado das Releases oficiais no GitHub e executado como programa à parte.</summary>
public sealed record Classico(
    string Id, string Nome, string Plataforma, string Descricao, string Repo, string AssetPadrao,
    string[] Exes, string Site, string Requer, string Licenca);

public sealed record ReleaseClassico(string Tag, string Asset, string Url, long Tamanho, string? Sha256, string? UrlSha256);

/// <summary>
/// Lançador de clássicos. O Hefesto não contém código de nenhum emulador ou port: baixa o pacote oficial da página de
/// Releases do projeto, confere o SHA-256 informado pelo GitHub, extrai em %LocalAppData%\Hefesto\Classicos\&lt;id&gt; e abre
/// o programa como processo separado. Jogos, ROMs, BIOS, firmware e dumps nunca são baixados nem indicados.
/// </summary>
public static class Classicos
{
    public static readonly Classico[] Catalogo =
    {
        new("rpcs3", "RPCS3", "PlayStation 3",
            "Emulador de PlayStation 3 de código aberto.",
            "RPCS3/rpcs3-binaries-win", @"win64.*\.7z$", new[] { "rpcs3.exe" }, "https://rpcs3.net/quickstart",
            "Precisa do firmware do PS3 (instalado pelo próprio RPCS3, conforme o guia oficial) e dos seus jogos.", "GPL-2.0"),
        new("pcsx2", "PCSX2", "PlayStation 2",
            "Emulador de PlayStation 2 de código aberto.",
            "PCSX2/pcsx2", @"windows-x64-Qt\.7z$", new[] { "pcsx2-qt.exe", "pcsx2.exe" }, "https://pcsx2.net/docs/setup/",
            "Precisa da BIOS copiada do seu próprio PS2 e dos seus jogos.", "GPL-3.0"),
        new("shadps4", "shadPS4", "PlayStation 4",
            "Emulador de PlayStation 4 de código aberto, em desenvolvimento.",
            "shadps4-emu/shadPS4", @"win64.*\.zip$", new[] { "shadPS4.exe", "shadps4.exe" }, "https://shadps4.net",
            "Precisa dos seus próprios jogos de PS4.", "GPL-2.0"),
        new("spartanport", "SpartanPort", "God of War III (PS4)",
            "Port não oficial de God of War III Remastered para PC, com launcher próprio.",
            "FelipePrado0/SpartanPort", @"spartanport-windows\.zip$", new[] { "SpartanPort.exe", "spartanport.exe", "launcher.exe" },
            "https://github.com/FelipePrado0/SpartanPort",
            "Precisa do dump do seu próprio God of War III Remastered.", "GPL-2.0"),
        new("openrct2", "OpenRCT2", "RollerCoaster Tycoon 2",
            "Reimplementação de código aberto do RollerCoaster Tycoon 2, com melhorias e multijogador.",
            "OpenRCT2/OpenRCT2", @"windows-portable-x64\.zip$", new[] { "openrct2.exe" }, "https://openrct2.io",
            "Precisa do RollerCoaster Tycoon 2 ou do RCT Classic original (Steam ou GOG). O Hefesto procura e configura sozinho.", "GPL-3.0"),
    };

    public static string Pasta(Classico c) => Ui.PastaLocal(Path.Combine("Classicos", c.Id));

    public static string? VersaoInstalada(Classico c)
        => Exe(c) == null ? null : AppSettings.Atual.Classicos.TryGetValue(c.Id, out var v) ? Tag(v) : "versão desconhecida";

    /// <summary>Executável principal dentro da pasta extraída (procura pelos nomes conhecidos, depois qualquer .exe na raiz).</summary>
    public static string? Exe(Classico c)
    {
        string p = Pasta(c);
        if (!Directory.Exists(p)) return null;
        var op = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true };
        foreach (var nome in c.Exes)
        {
            var achado = Directory.EnumerateFiles(p, nome, op).OrderBy(f => f.Length).FirstOrDefault();
            if (achado != null) return achado;
        }
        return Directory.EnumerateFiles(p, "*.exe", new EnumerationOptions { MaxRecursionDepth = 1, RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => !Regex.IsMatch(Path.GetFileName(f), "unins|crash|update|probe|vc_redist", RegexOptions.IgnoreCase))
            .OrderBy(f => f.Length).FirstOrDefault();
    }

    private static HttpClient NovoHttp(int segundos = 30)
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(segundos) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Hefesto/" + Ui.Versao);
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    /// <summary>Última versão estável publicada na página de Releases do projeto.</summary>
    public static async Task<ReleaseClassico> UltimaRelease(Classico c, CancellationToken ct)
    {
        using var http = NovoHttp();
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{c.Repo}/releases/latest", ct));
        var r = doc.RootElement;
        string tag = r.GetProperty("tag_name").GetString() ?? "?";
        var re = new Regex(c.AssetPadrao, RegexOptions.IgnoreCase);
        var assets = r.GetProperty("assets").EnumerateArray().ToList();
        var x = assets.FirstOrDefault(a => re.IsMatch(a.GetProperty("name").GetString() ?? ""));
        if (x.ValueKind != JsonValueKind.Object) throw new InvalidOperationException($"a versão {tag} não tem pacote para Windows");
        string nomeAsset = x.GetProperty("name").GetString()!;
        string? urlSha = assets.Where(a => a.GetProperty("name").GetString() == nomeAsset + ".sha256")
                               .Select(a => a.GetProperty("browser_download_url").GetString()).FirstOrDefault();
        string? sha = x.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String && d.GetString() is string dg && dg.StartsWith("sha256:")
            ? dg[7..].ToLowerInvariant() : null;
        return new ReleaseClassico(Tag(tag), nomeAsset, x.GetProperty("browser_download_url").GetString()!, x.GetProperty("size").GetInt64(), sha, urlSha);
    }

    /// <summary>Encurta tags longas (o RPCS3 usa "build-&lt;hash&gt;").</summary>
    private static string Tag(string t)
    {
        if (t.StartsWith("build-") && t.Length > 14) return "build " + t[6..14];
        if (t.StartsWith("windows-", StringComparison.OrdinalIgnoreCase)) t = t[8..];
        return t.TrimStart('v', '.');
    }

    /// <summary>Baixa, confere o SHA-256 e extrai por cima da pasta atual (mantém configurações e saves que o emulador guarda nela).</summary>
    public static async Task<string> Instalar(Classico c, IProgress<(string Etapa, double Fracao)>? prog, CancellationToken ct)
    {
        var rel = await UltimaRelease(c, ct);
        string downloads = Ui.PastaLocal(Path.Combine("Classicos", "_downloads"));
        string arq = Path.Combine(downloads, rel.Asset);

        using (var http = NovoHttp(1800))
        {
            string? esperado = rel.Sha256;
            if (esperado == null && rel.UrlSha256 != null)
                esperado = (await http.GetStringAsync(rel.UrlSha256, ct)).Trim().Split(' ', '\t', '\n')[0].ToLowerInvariant();
            if (esperado == null)
                throw new InvalidDataException("o projeto não publicou o SHA-256 deste pacote; por segurança a instalação foi cancelada");

            using (var resp = await http.GetAsync(rel.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? rel.Tamanho;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(arq);
                var buf = new byte[1 << 16];
                long lido = 0; int n; var ultimo = DateTime.MinValue;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    lido += n;
                    if ((DateTime.Now - ultimo).TotalMilliseconds > 150)
                    {
                        ultimo = DateTime.Now;
                        prog?.Report(($"Baixando {c.Nome} {rel.Tag}… {Ui.Tam(lido)} de {Ui.Tam(total)}", total > 0 ? 0.85 * lido / total : 0.4));
                    }
                }
            }

            prog?.Report(("Conferindo o SHA-256…", 0.86));
            string calc;
            await using (var fs = File.OpenRead(arq)) calc = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            if (calc != esperado)
            {
                File.Delete(arq);
                throw new InvalidDataException("o arquivo baixado não confere com o SHA-256 publicado (download corrompido ou alterado)");
            }
        }

        prog?.Report(("Extraindo…", 0.9));
        string destino = Pasta(c);
        await Task.Run(() =>
        {
            if (arq.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipFile.ExtractToDirectory(arq, destino, true);
            else
            {
                using var a = ArchiveFactory.OpenArchive(arq);
                a.WriteToDirectory(destino, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
            }
        }, ct);
        try { File.Delete(arq); } catch { }

        if (Exe(c) == null) throw new InvalidOperationException("o pacote foi extraído, mas o executável não foi encontrado");
        AppSettings.Atual.Classicos[c.Id] = rel.Tag;
        try { AppSettings.Atual.Save(); } catch { }

        string extra = c.Id == "openrct2" ? " " + ConfigurarOpenRct2() : "";
        prog?.Report(("Pronto", 1));
        return $"{c.Nome} {rel.Tag} instalado (SHA-256 conferido).{extra}";
    }

    public static void Abrir(Classico c)
    {
        string exe = Exe(c) ?? throw new InvalidOperationException($"{c.Nome} não está instalado");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
    }

    // ---------------- OpenRCT2 ----------------

    /// <summary>Procura o RCT2 (ou RCT Classic) original instalado pela Steam ou GOG.</summary>
    public static LibGame? JogoRct2()
    {
        try
        {
            var jogos = GameLibrary.Scan(Array.Empty<CustomGame>());
            return jogos.FirstOrDefault(g => Regex.IsMatch(g.Name, @"RollerCoaster Tycoon\W*2", RegexOptions.IgnoreCase) && Directory.Exists(g.InstallDir))
                ?? jogos.FirstOrDefault(g => Regex.IsMatch(g.Name, @"RollerCoaster Tycoon\W*Classic", RegexOptions.IgnoreCase) && Directory.Exists(g.InstallDir));
        }
        catch { return null; }
    }

    /// <summary>Grava game_path no config.ini do OpenRCT2 (Documentos\OpenRCT2), só se ainda não houver um caminho válido.</summary>
    public static string ConfigurarOpenRct2()
    {
        var jogo = JogoRct2();
        if (jogo == null) return "RollerCoaster Tycoon 2 não foi encontrado na Steam nem no GOG; o OpenRCT2 vai pedir a pasta do jogo ao abrir.";
        string pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OpenRCT2");
        string ini = Path.Combine(pasta, "config.ini");
        Directory.CreateDirectory(pasta);
        var linhas = File.Exists(ini) ? File.ReadAllLines(ini).ToList() : new List<string>();
        string valor = $"game_path = \"{jogo.InstallDir.Replace("\\", "\\\\")}\"";
        int idx = linhas.FindIndex(l => l.TrimStart().StartsWith("game_path", StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            string atual = linhas[idx][(linhas[idx].IndexOf('=') + 1)..].Trim().Trim('"').Replace("\\\\", "\\");
            if (atual.Length > 0 && Directory.Exists(atual)) return $"O OpenRCT2 já usa o jogo em {atual}.";
            linhas[idx] = valor;
        }
        else
        {
            int geral = linhas.FindIndex(l => l.Trim().Equals("[general]", StringComparison.OrdinalIgnoreCase));
            if (geral < 0) { linhas.Insert(0, "[general]"); geral = 0; }
            linhas.Insert(geral + 1, valor);
        }
        File.WriteAllLines(ini, linhas);
        return $"Configurado para usar {jogo.Name} ({jogo.Store}) em {jogo.InstallDir}.";
    }
}
