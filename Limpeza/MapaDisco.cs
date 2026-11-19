using System.Collections.Concurrent;
using System.IO;

namespace Hefesto;

/// <summary>Pasta do mapa do disco, com o tamanho somado de tudo que está dentro.</summary>
public sealed class NoMapa
{
    public string Nome { get; init; } = "";
    public string Caminho { get; init; } = "";
    public bool EhPasta { get; init; } = true;
    public long Tamanho { get; set; }
    public long Arquivos { get; set; }
    public NoMapa? Pai { get; init; }
    public List<NoMapa> Filhos { get; } = new();
    /// <summary>Tamanho dos arquivos soltos desta pasta (não entram como filhos para o mapa não ficar gigante).</summary>
    public long ArquivosSoltos { get; set; }

    // Exibição
    public double Fracao { get; set; }
    public string TamanhoTexto => Ui.Tam(Tamanho);
    public string Detalhe => EhPasta ? (Arquivos == 1 ? "1 arquivo" : $"{Arquivos:N0} arquivos") : "arquivo";
    public string Icone => EhPasta ? "" : "";
}

/// <summary>
/// Mapa do disco por enumeração paralela (funciona em NTFS, exFAT, FAT32 e rede). A enumeração do .NET já traz o tamanho
/// junto com o nome (FindFirstFileEx/NtQueryDirectoryFile), sem abrir cada arquivo. Pontos de junção são pulados para não
/// contar a mesma pasta duas vezes. Também guarda os maiores arquivos do disco.
/// </summary>
public static class MapaDisco
{
    private static readonly EnumerationOptions Raso = new()
    {
        RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false,
    };

    public sealed class Resultado
    {
        public NoMapa Raiz { get; init; } = new();
        public List<NoMapa> MaioresArquivos { get; init; } = new();
        public long Pastas { get; set; }
        public TimeSpan Tempo { get; set; }
    }

    public static Resultado Mapear(string raiz, IProgress<long>? progresso, CancellationToken ct)
    {
        var t0 = DateTime.Now;
        var no = new NoMapa { Nome = raiz, Caminho = raiz };
        var grandes = new ConcurrentBag<NoMapa>();
        var pastas = new long[1];
        long limiteGrande = 50L * 1024 * 1024;
        Varrer(no, 0, grandes, pastas, limiteGrande, progresso, ct);
        return new Resultado
        {
            Raiz = no,
            MaioresArquivos = grandes.OrderByDescending(g => g.Tamanho).Take(60).ToList(),
            Pastas = pastas[0],
            Tempo = DateTime.Now - t0,
        };
    }

    private static void Varrer(NoMapa no, int nivel, ConcurrentBag<NoMapa> grandes, long[] pastas, long limiteGrande,
                               IProgress<long>? progresso, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        var di = new DirectoryInfo(no.Caminho);
        long soltos = 0, arquivos = 0;
        var subs = new List<NoMapa>();
        try
        {
            foreach (var fsi in di.EnumerateFileSystemInfos("*", Raso))
            {
                if (fsi is FileInfo f)
                {
                    long len;
                    try { len = f.Length; } catch { continue; }
                    soltos += len; arquivos++;
                    if (len >= limiteGrande) grandes.Add(new NoMapa { Nome = f.Name, Caminho = f.FullName, EhPasta = false, Tamanho = len, Pai = no });
                }
                else subs.Add(new NoMapa { Nome = fsi.Name, Caminho = fsi.FullName, Pai = no });
            }
        }
        catch { }
        long p = Interlocked.Increment(ref pastas[0]);
        if (p % 500 == 0) progresso?.Report(p);

        // Paralelo nos dois primeiros níveis (onde estão as pastas grandes); sequencial abaixo para não criar tarefas demais.
        if (nivel < 3 && subs.Count > 1)
        {
            try
            {
                Parallel.ForEach(subs, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                    s => Varrer(s, nivel + 1, grandes, pastas, limiteGrande, progresso, ct));
            }
            catch (OperationCanceledException) { }
        }
        else foreach (var s in subs) Varrer(s, nivel + 1, grandes, pastas, limiteGrande, progresso, ct);

        no.ArquivosSoltos = soltos;
        no.Filhos.AddRange(subs.Where(s => s.Tamanho > 0).OrderByDescending(s => s.Tamanho));
        no.Tamanho = soltos + subs.Sum(s => s.Tamanho);
        no.Arquivos = arquivos + subs.Sum(s => s.Arquivos);
    }

    /// <summary>Itens para exibir ao abrir uma pasta: subpastas + "arquivos soltos", com a fração relativa à maior.</summary>
    public static List<NoMapa> Exibir(NoMapa no)
    {
        var l = no.Filhos.Take(200).ToList();
        if (no.ArquivosSoltos > 0)
            l.Add(new NoMapa { Nome = "(arquivos nesta pasta)", Caminho = no.Caminho, EhPasta = false, Tamanho = no.ArquivosSoltos, Pai = no });
        l = l.OrderByDescending(x => x.Tamanho).ToList();
        double max = l.Count == 0 ? 1 : Math.Max(1, l[0].Tamanho);
        foreach (var x in l) x.Fracao = x.Tamanho / max;
        return l;
    }
}
