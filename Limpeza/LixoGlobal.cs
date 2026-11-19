using System.IO;
using MFTLib;

namespace Hefesto;

/// <summary>
/// Lixo espalhado pelo disco inteiro, achado pelo nome. Em NTFS lê a MFT direto (MFTLib, exige administrador):
/// o disco todo em segundos, sem abrir pasta por pasta. Em outros sistemas de arquivos (exFAT, FAT32) ou sem permissão,
/// cai para a enumeração normal. Tudo passa pela lista de proteção: dentro de repositórios git nada é tocado.
/// </summary>
public static class LixoGlobal
{
    private sealed record Alvo(string Id, string Nome, string Descricao, string Busca, bool Pasta, Func<string, bool> Combina, int DiasMin);

    private static readonly Alvo[] Alvos =
    {
        new("thumbs", "Miniaturas soltas (Thumbs.db, .DS_Store)",
            "Arquivos de miniatura que o Windows e o macOS deixam nas pastas. São recriados quando precisar.",
            "Thumbs.db", false, n => n.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || n.Equals("ehthumbs.db", StringComparison.OrdinalIgnoreCase)
                                     || n.Equals(".DS_Store", StringComparison.Ordinal), 0),
        new("macfork", "Restos do macOS (._arquivo)",
            "Arquivos ._ criados ao copiar de um Mac para pendrive ou HD. Não servem para nada no Windows.",
            "._", false, n => n.StartsWith("._", StringComparison.Ordinal) && n.Length > 2, 0),
        new("pycache", "Cache do Python (__pycache__)",
            "Pastas __pycache__ fora de projetos com git. O Python recria na próxima execução.",
            "__pycache__", true, n => n.Equals("__pycache__", StringComparison.Ordinal), 0),
        new("dmp", "Despejos de memória (.dmp) espalhados",
            "Arquivos .dmp com mais de 7 dias fora das pastas do Windows: sobras de travamentos de programas e jogos.",
            ".dmp", false, n => n.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase), 7),
        new("tmp", "Arquivos .tmp antigos",
            "Arquivos .tmp com mais de 14 dias que ficaram para trás fora da pasta Temp.",
            ".tmp", false, n => n.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase), 14),
        new("chk", "Fragmentos do CHKDSK (FOUND.000)",
            "Pastas FOUND.xxx criadas pelo CHKDSK com pedaços de arquivos recuperados. Confira antes se não há nada seu.",
            "FOUND.", true, n => n.StartsWith("FOUND.", StringComparison.OrdinalIgnoreCase) && n.Length == 9, 0),
    };

    public static List<string> Discos() => DriveInfo.GetDrives()
        .Where(d => { try { return d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable; } catch { return false; } })
        .Select(d => d.RootDirectory.FullName).ToList();

    public static async Task<List<ResultadoRegra>> Analisar(IEnumerable<string> discos, ContextoVarredura c, IProgress<string>? status)
    {
        var resultados = Alvos.Select(a => new ResultadoRegra
        {
            Regra = new Regra { Id = a.Id, Nome = a.Nome, Descricao = a.Descricao },
        }).ToList();

        foreach (var raiz in discos)
        {
            if (c.Ct.IsCancellationRequested) break;
            string fs = "";
            try { fs = new DriveInfo(raiz).DriveFormat; } catch { }
            bool mft = fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
            status?.Report($"Procurando em {raiz} ({(mft ? "lendo a MFT" : "pasta por pasta")})…");

            List<(string Caminho, bool Pasta)> achados = await Task.Run(() =>
            {
                if (mft)
                {
                    try { return ViaMft(raiz); }
                    catch (Exception ex) { c.Avisos.Add($"MFT de {raiz} indisponível ({ex.Message}); usando a enumeração normal."); }
                }
                return ViaEnumeracao(raiz, c.Ct);
            });

            for (int i = 0; i < Alvos.Length; i++)
            {
                var a = Alvos[i];
                var limite = DateTime.Now.AddDays(-a.DiasMin);
                foreach (var (caminho, pasta) in achados)
                {
                    if (pasta != a.Pasta || !a.Combina(Path.GetFileName(caminho))) continue;
                    if (a.Id == "dmp" && caminho.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase)) continue; // já na Limpeza rápida
                    if (a.Id == "tmp" && caminho.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase)) continue;
                    string? motivo = Protecao.Motivo(caminho);
                    if (motivo != null) { c.Protegidos.Add((caminho, motivo)); continue; }
                    try
                    {
                        if (pasta)
                        {
                            var di = new DirectoryInfo(caminho);
                            if (!di.Exists || di.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                            resultados[i].Itens.Add(new ItemLimpeza { Caminho = caminho, EhPasta = true, Tamanho = LimpezaRapida.TamanhoPasta(caminho), Modificado = di.LastWriteTime, Regra = a.Id });
                        }
                        else
                        {
                            var fi = new FileInfo(caminho);
                            if (!fi.Exists || fi.LastWriteTime > limite) continue;
                            resultados[i].Itens.Add(new ItemLimpeza { Caminho = caminho, Tamanho = fi.Length, Modificado = fi.LastWriteTime, Regra = a.Id });
                        }
                    }
                    catch { }
                }
            }
        }
        foreach (var r in resultados) r.Selecionada = r.Itens.Count > 0 && r.Regra.Id != "chk";
        return resultados;
    }

    private static List<(string, bool)> ViaMft(string raiz)
    {
        var r = new List<(string, bool)>();
        using var vol = MftVolume.Open(raiz[..1]);
        foreach (var a in Alvos)
        {
            var flags = (a.Busca.StartsWith('.') || a.Busca.EndsWith('.') || a.Busca == "._" ? MatchFlags.Contains : MatchFlags.ExactMatch) | MatchFlags.ResolvePaths;
            foreach (var rec in vol.FindByName(a.Busca, flags))
            {
                if (!rec.InUse || rec.IsDirectory != a.Pasta) continue;
                string? p = rec.FullPath;
                if (string.IsNullOrEmpty(p)) continue;
                if (!Path.IsPathRooted(p)) p = Path.Combine(raiz, p.TrimStart('\\'));
                r.Add((p, rec.IsDirectory));
            }
            if (a.Id == "thumbs")
                foreach (var extra in new[] { "ehthumbs.db", ".DS_Store" })
                    foreach (var rec in vol.FindByName(extra, MatchFlags.ExactMatch | MatchFlags.ResolvePaths))
                        if (rec.InUse && !rec.IsDirectory && rec.FullPath is string fp)
                            r.Add((Path.IsPathRooted(fp) ? fp : Path.Combine(raiz, fp.TrimStart('\\')), false));
        }
        return r.Distinct().ToList();
    }

    private static List<(string, bool)> ViaEnumeracao(string raiz, CancellationToken ct)
    {
        var r = new List<(string, bool)>();
        var op = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            foreach (var fsi in new DirectoryInfo(raiz).EnumerateFileSystemInfos("*", op))
            {
                if (ct.IsCancellationRequested) break;
                bool pasta = fsi is DirectoryInfo;
                if (Alvos.Any(a => a.Pasta == pasta && a.Combina(fsi.Name))) r.Add((fsi.FullName, pasta));
            }
        }
        catch { }
        return r;
    }
}
