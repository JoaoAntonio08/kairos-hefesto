using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Hefesto;

/// <summary>Arquivo ou pasta que uma regra quer remover.</summary>
public sealed class ItemLimpeza
{
    public string Caminho { get; init; } = "";
    public long Tamanho { get; init; }
    public DateTime Modificado { get; init; }
    public bool EhPasta { get; init; }
    public string Regra { get; init; } = "";
}

public enum Destino { Lixeira, Quarentena, Falhou, Protegido }

public sealed record ResultadoRemocao(ItemLimpeza Item, Destino Destino, string? Detalhe);

public sealed record EntradaQuarentena(string Original, string Atual, long Tamanho, DateTime Data);

/// <summary>
/// Remoção segura: nada é apagado de vez. Tudo vai para a Lixeira do Windows (SHFileOperation com FOF_ALLOWUNDO).
/// Se o item for maior que a capacidade da Lixeira daquele disco, se a Lixeira estiver desligada ou se o disco não tiver
/// Lixeira (pendrive, rede), o item é movido para "&lt;disco&gt;:\Hefesto Quarentena\&lt;data&gt;" com um manifesto para restaurar.
/// FOF_WANTNUKEWARNING fica ligado como rede de segurança: se mesmo assim o Windows fosse apagar de vez, ele pergunta antes.
/// </summary>
public static class Lixeira
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.U4)] public int wFunc;
        public string pFrom;
        public string? pTo;
        public short fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)] // x64: alinhamento natural (o app só sai em win-x64)
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder name, int len);

    private const int FO_DELETE = 3;
    private const short FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;
    private const short Flags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING;

    public sealed record InfoLixeira(bool Habilitada, long Capacidade, long EmUso, string Motivo);

    private static readonly Dictionary<string, InfoLixeira> InfoCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Estado da Lixeira de um disco: se existe/está ligada, capacidade configurada e quanto já ocupa.</summary>
    public static InfoLixeira Info(string raiz)
    {
        raiz = raiz.TrimEnd('\\') + "\\";
        lock (InfoCache)
            if (InfoCache.TryGetValue(raiz, out var c)) return c;

        InfoLixeira r;
        try
        {
            var di = new DriveInfo(raiz);
            if (di.DriveType != DriveType.Fixed) r = new(false, 0, 0, "disco sem Lixeira (removível ou de rede)");
            else
            {
                var q = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
                SHQueryRecycleBin(raiz, ref q);
                long cap = di.TotalSize / 20; // padrão aproximado do Windows (5%) se a capacidade não estiver no registro
                bool ligada = true;
                var sb = new StringBuilder(64);
                if (GetVolumeNameForVolumeMountPoint(raiz, sb, sb.Capacity))
                {
                    string guid = sb.ToString();
                    int a = guid.IndexOf('{'), b = guid.IndexOf('}');
                    if (a >= 0 && b > a)
                    {
                        using var k = Registry.CurrentUser.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + guid[a..(b + 1)]);
                        if (k?.GetValue("NukeOnDelete") is int nuke && nuke == 1) ligada = false;
                        if (k?.GetValue("MaxCapacity") is int mb && mb > 0) cap = (long)mb * 1024 * 1024;
                    }
                }
                r = new(ligada, cap, q.i64Size, ligada ? "" : "a Lixeira deste disco está desligada");
            }
        }
        catch (Exception ex) { r = new(false, 0, 0, "não foi possível ler a Lixeira: " + ex.Message); }
        lock (InfoCache) InfoCache[raiz] = r;
        return r;
    }

    /// <summary>Remove os itens com segurança. Roda numa thread STA própria (exigência das APIs do shell).</summary>
    public static Task<List<ResultadoRemocao>> RemoverAsync(IReadOnlyList<ItemLimpeza> itens, bool permitirProgramas,
                                                            IProgress<(int Feitos, int Total)>? prog, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<List<ResultadoRemocao>>();
        var t = new Thread(() =>
        {
            try { tcs.SetResult(Remover(itens, permitirProgramas, prog, ct)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true, Name = "Hefesto Lixeira" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    private static List<ResultadoRemocao> Remover(IReadOnlyList<ItemLimpeza> itens, bool permitirProgramas,
                                                  IProgress<(int, int)>? prog, CancellationToken ct)
    {
        lock (InfoCache) InfoCache.Clear();
        var res = new List<ResultadoRemocao>();
        var paraLixeira = new List<ItemLimpeza>();
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        int feitos = 0;

        foreach (var it in itens)
        {
            if (ct.IsCancellationRequested) break;
            string? motivo = Protecao.Motivo(it.Caminho, permitirProgramas);
            if (motivo != null) { res.Add(new(it, Destino.Protegido, motivo)); prog?.Report((++feitos, itens.Count)); continue; }
            if (!Existe(it.Caminho)) { res.Add(new(it, Destino.Falhou, "já não existe")); prog?.Report((++feitos, itens.Count)); continue; }

            var info = Info(Path.GetPathRoot(it.Caminho) ?? "C:\\");
            if (!info.Habilitada || it.Tamanho > info.Capacidade)
            {
                string porque = !info.Habilitada ? info.Motivo : $"maior que a Lixeira ({Ui.Tam(info.Capacidade)})";
                res.Add(MoverParaQuarentena(it, stamp, porque));
                prog?.Report((++feitos, itens.Count));
            }
            else paraLixeira.Add(it);
        }

        // Arquivos em lotes (bem mais rápido); pastas uma a uma. Se um lote falhar (arquivo em uso), refaz item a item.
        foreach (var lote in paraLixeira.Where(i => !i.EhPasta).Chunk(150).Concat(paraLixeira.Where(i => i.EhPasta).Select(i => new[] { i })))
        {
            if (ct.IsCancellationRequested) break;
            int rc = EnviarParaLixeira(lote.Select(i => i.Caminho));
            if (rc != 0 && lote.Length > 1)
                foreach (var i in lote.Where(i => Existe(i.Caminho))) EnviarParaLixeira(new[] { i.Caminho });
            foreach (var i in lote)
                res.Add(Existe(i.Caminho)
                    ? new(i, Destino.Falhou, "em uso ou sem permissão")
                    : new(i, Destino.Lixeira, null));
            feitos += lote.Length;
            prog?.Report((feitos, itens.Count));
        }
        return res;
    }

    private static int EnviarParaLixeira(IEnumerable<string> caminhos)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = string.Join('\0', caminhos) + "\0\0",
            fFlags = Flags,
        };
        int rc = SHFileOperation(ref op);
        return op.fAnyOperationsAborted ? -1 : rc;
    }

    private static bool Existe(string p) => File.Exists(p) || Directory.Exists(p);

    public static string PastaQuarentena(string raiz) => Path.Combine(raiz, "Hefesto Quarentena");

    private static ResultadoRemocao MoverParaQuarentena(ItemLimpeza it, string stamp, string porque)
    {
        try
        {
            string raiz = Path.GetPathRoot(it.Caminho)!;
            string sessao = Path.Combine(PastaQuarentena(raiz), stamp);
            string relativo = it.Caminho[raiz.Length..];
            string destino = Path.Combine(sessao, relativo);
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            if (it.EhPasta) Directory.Move(it.Caminho, destino);
            else File.Move(it.Caminho, destino);

            string leia = Path.Combine(PastaQuarentena(raiz), "LEIA-ME.txt");
            if (!File.Exists(leia))
                File.WriteAllText(leia, "Quarentena do Hefesto\r\n\r\nArquivos que a limpeza tirou do lugar sem apagar, porque não cabiam na Lixeira.\r\n" +
                                        "Cada pasta tem um manifesto.json com o caminho original. Para devolver, use Limpeza > Quarentena no Hefesto\r\n" +
                                        "ou mova os arquivos de volta. Quando tiver certeza de que não precisa deles, pode apagar esta pasta.\r\n");
            AnotarManifesto(sessao, new EntradaQuarentena(it.Caminho, destino, it.Tamanho, DateTime.Now));
            return new(it, Destino.Quarentena, porque);
        }
        catch (Exception ex) { return new(it, Destino.Falhou, "não foi possível mover para a quarentena: " + ex.Message); }
    }

    private static readonly object ManifestoLock = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static void AnotarManifesto(string sessao, EntradaQuarentena e)
    {
        lock (ManifestoLock)
        {
            string arq = Path.Combine(sessao, "manifesto.json");
            var lista = LerManifesto(arq);
            lista.Add(e);
            File.WriteAllText(arq, JsonSerializer.Serialize(lista, JsonOpts));
        }
    }

    private static List<EntradaQuarentena> LerManifesto(string arq)
    {
        try { return File.Exists(arq) ? JsonSerializer.Deserialize<List<EntradaQuarentena>>(File.ReadAllText(arq)) ?? new() : new(); }
        catch { return new(); }
    }

    /// <summary>Tudo o que está em quarentena em todos os discos fixos e removíveis.</summary>
    public static List<EntradaQuarentena> ListarQuarentena()
    {
        var r = new List<EntradaQuarentena>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                string q = PastaQuarentena(d.RootDirectory.FullName);
                if (!Directory.Exists(q)) continue;
                foreach (var s in Directory.GetDirectories(q))
                    r.AddRange(LerManifesto(Path.Combine(s, "manifesto.json")).Where(e => File.Exists(e.Atual) || Directory.Exists(e.Atual)));
            }
            catch { }
        }
        return r.OrderByDescending(e => e.Data).ToList();
    }

    /// <summary>Devolve um item da quarentena ao lugar original (se o lugar estiver livre).</summary>
    public static string? Restaurar(EntradaQuarentena e)
    {
        try
        {
            if (File.Exists(e.Original) || Directory.Exists(e.Original)) return "já existe um arquivo com esse nome no lugar original";
            Directory.CreateDirectory(Path.GetDirectoryName(e.Original)!);
            if (Directory.Exists(e.Atual)) Directory.Move(e.Atual, e.Original);
            else File.Move(e.Atual, e.Original);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
