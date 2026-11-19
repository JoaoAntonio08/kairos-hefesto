using System.Collections.Concurrent;
using System.IO;

namespace Hefesto;

/// <summary>
/// Lista de proteção fixa da limpeza. Vale para todas as regras (rápida, profunda, winapp2 e sobras do desinstalador):
/// nada aqui é removido, mesmo que uma regra aponte para o caminho.
/// Protege senhas, cookies e histórico de navegadores, chaves (.ssh, .gnupg), saves de jogo, pastas pessoais,
/// repositórios git (qualquer pasta com .git acima do arquivo), credenciais do Windows e o próprio Windows.
/// </summary>
public static class Protecao
{
    private static readonly string Perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    // Pastas de cache de navegador: só o conteúdo delas pode ser limpo (o resto do perfil guarda senhas, cookies e histórico).
    private static readonly HashSet<string> PastasCache = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cache", "Cache_Data", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache", "GraphiteDawnCache",
        "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "cache2",
    };

    private static readonly HashSet<string> SegmentosSensiveis = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker", ".git",
    };

    private static readonly string[] Pessoais =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        Path.Combine(Perfil, "OneDrive"),
        Path.Combine(Perfil, "Saved Games"),
        Path.Combine(Perfil, "AppData", "LocalLow"), // saves de jogos Unity
    };

    // Dentro do Windows só estas pastas podem ser limpas.
    private static readonly string[] WindowsLiberadas =
    {
        Path.Combine(WinDir, "Temp"), Path.Combine(WinDir, "Minidump"), Path.Combine(WinDir, "MEMORY.DMP"),
        Path.Combine(WinDir, "LiveKernelReports"), Path.Combine(WinDir, "SoftwareDistribution", "DeliveryOptimization"),
        Path.Combine(WinDir, "Logs", "CBS"), Path.Combine(WinDir, "Prefetch"),
    };

    private static readonly ConcurrentDictionary<string, bool> GitCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Motivo da proteção, ou null se o caminho pode ser limpo.
    /// <paramref name="permitirProgramas"/> libera Program Files e ProgramData (sobras de um programa desinstalado).</summary>
    public static string? Motivo(string caminho, bool permitirProgramas = false)
    {
        string p;
        try { p = Path.GetFullPath(caminho).TrimEnd('\\'); }
        catch { return "caminho inválido"; }

        string? raiz = Path.GetPathRoot(p)?.TrimEnd('\\');
        if (raiz == null || p.Length <= raiz.Length + 1) return "raiz do disco";
        if (Igual(p, Perfil) || Igual(p, WinDir)) return "pasta do sistema";

        var segs = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Any(s => s.Equals("Hefesto Quarentena", StringComparison.OrdinalIgnoreCase))) return "quarentena do Hefesto";
        if (segs.Any(s => s.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) || s.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)))
            return "Lixeira ou pasta do sistema (esvazie a Lixeira pelo próprio Windows)";
        foreach (var s in segs)
            if (SegmentosSensiveis.Contains(s))
                return s.Equals(".git", StringComparison.OrdinalIgnoreCase) ? "repositório git" : "chaves e credenciais";

        // Saves de jogo
        if (Contem(p, @"\My Games\") || Contem(p, @"\Steam\userdata\") || Contem(p, @"\Saved Games\")) return "saves de jogo";

        // Navegadores: Roaming guarda senhas, cookies, histórico e extensões
        if (Contem(p, @"\AppData\Roaming\Opera Software") || Contem(p, @"\AppData\Roaming\Mozilla")
            || Contem(p, @"\AppData\Roaming\Vivaldi") || Contem(p, @"\AppData\Roaming\Thunderbird"))
            return "dados do navegador (senhas, cookies, histórico)";
        bool perfilNavegador = Contem(p, @"\User Data\") || Contem(p, @"\Mozilla\Firefox\Profiles\")
                               || Contem(p, @"\AppData\Local\Opera Software\");
        if (perfilNavegador && !segs.Any(PastasCache.Contains))
            return "dados do navegador (senhas, cookies, histórico)";

        // Credenciais e chaves do Windows
        if (Contem(p, @"\Microsoft\Credentials") || Contem(p, @"\Microsoft\Protect") || Contem(p, @"\Microsoft\Vault")
            || Contem(p, @"\Microsoft\SystemCertificates") || Contem(p, @"\Microsoft\Crypto"))
            return "credenciais do Windows";

        foreach (var pasta in Pessoais)
            if (pasta.Length > 0 && (Igual(p, pasta) || p.StartsWith(pasta + "\\", StringComparison.OrdinalIgnoreCase)))
                return pasta.EndsWith("LocalLow", StringComparison.OrdinalIgnoreCase) || pasta.EndsWith("Saved Games", StringComparison.OrdinalIgnoreCase)
                    ? "saves de jogo" : "pasta pessoal";

        if (p.StartsWith(WinDir + "\\", StringComparison.OrdinalIgnoreCase)
            && !WindowsLiberadas.Any(w => Igual(p, w) || p.StartsWith(w + "\\", StringComparison.OrdinalIgnoreCase)))
            return "pasta do Windows";

        if (!permitirProgramas)
        {
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                       Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                if (pf.Length > 0 && p.StartsWith(pf + "\\", StringComparison.OrdinalIgnoreCase)) return "programa instalado";
        }

        if (DentroDeRepositorioGit(Path.GetDirectoryName(p))) return "repositório git";
        return null;
    }

    public static bool Protegido(string caminho, bool permitirProgramas = false) => Motivo(caminho, permitirProgramas) != null;

    private static bool DentroDeRepositorioGit(string? dir)
    {
        var visitados = new List<string>();
        bool achou = false;
        while (!string.IsNullOrEmpty(dir))
        {
            if (GitCache.TryGetValue(dir, out bool v)) { achou = v; break; }
            visitados.Add(dir);
            try
            {
                if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git"))) { achou = true; break; }
            }
            catch { }
            dir = Path.GetDirectoryName(dir);
        }
        foreach (var d in visitados) GitCache[d] = achou;
        return achou;
    }

    private static bool Contem(string p, string trecho) => p.Contains(trecho, StringComparison.OrdinalIgnoreCase);
    private static bool Igual(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
