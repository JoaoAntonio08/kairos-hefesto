using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;

namespace Hefesto;

public sealed class StatusDefender
{
    public bool Disponivel { get; init; }
    public bool Antivirus { get; init; }
    public bool TempoReal { get; init; }
    public bool Adulteracao { get; init; }
    public string Assinaturas { get; init; } = "";
    public DateTime? AssinaturasData { get; init; }
    public DateTime? UltimaRapida { get; init; }
    public DateTime? UltimaCompleta { get; init; }
    public string? Erro { get; init; }
    public List<string> Ameacas { get; init; } = new();
}

/// <summary>
/// Microsoft Defender: só leitura do estado (WMI root\Microsoft\Windows\Defender) e comandos do MpCmdRun que não
/// mudam configuração nenhuma: atualizar assinaturas, verificação rápida, completa ou de um arquivo/pasta.
/// O Hefesto nunca desliga nem altera a proteção.
/// </summary>
public static class Defender
{
    public static StatusDefender Status()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Defender");
            scope.Connect();
            using var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpComputerStatus"));
            foreach (ManagementObject o in s.Get())
            {
                var ameacas = new List<string>();
                try
                {
                    var nomes = new Dictionary<string, string>();
                    using (var tn = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT ThreatID, ThreatName FROM MSFT_MpThreat")))
                        foreach (ManagementObject x in tn.Get()) nomes[x["ThreatID"]?.ToString() ?? ""] = x["ThreatName"] as string ?? "";
                    var deteccoes = new List<(string Id, string Recurso, DateTime? Quando)>();
                    using (var t = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpThreatDetection")))
                        foreach (ManagementObject d in t.Get())
                            deteccoes.Add((d["ThreatID"]?.ToString() ?? "", d["Resources"] is string[] r ? string.Join(", ", r.Take(2)) : "", Data(d["InitialDetectionTime"])));
                    foreach (var g in deteccoes.GroupBy(d => (d.Id, d.Recurso)).OrderByDescending(g => g.Max(x => x.Quando)).Take(15))
                    {
                        string nome = nomes.TryGetValue(g.Key.Id, out var n) && n.Length > 0 ? n : "ameaça " + g.Key.Id;
                        string rec = g.Key.Recurso.StartsWith("file:_") ? g.Key.Recurso[6..] : g.Key.Recurso;
                        ameacas.Add($"{nome} · {rec} · {g.Count()}x, última em {g.Max(x => x.Quando):dd/MM/yyyy HH:mm}");
                    }
                }
                catch { }
                return new StatusDefender
                {
                    Disponivel = true,
                    Antivirus = o["AntivirusEnabled"] is true,
                    TempoReal = o["RealTimeProtectionEnabled"] is true,
                    Adulteracao = o["IsTamperProtected"] is true,
                    Assinaturas = o["AntivirusSignatureVersion"] as string ?? "",
                    AssinaturasData = Data(o["AntivirusSignatureLastUpdated"]),
                    UltimaRapida = Data(o["QuickScanEndTime"]),
                    UltimaCompleta = Data(o["FullScanEndTime"]),
                    Ameacas = ameacas,
                };
            }
            return new StatusDefender { Erro = "o Defender não respondeu" };
        }
        catch (Exception ex) { return new StatusDefender { Erro = ex.Message }; }
    }

    private static DateTime? Data(object? v)
    {
        try { return v is string s && s.Length >= 14 ? ManagementDateTimeConverter.ToDateTime(s) : null; }
        catch { return null; }
    }

    /// <summary>MpCmdRun.exe mais novo (pasta Platform) ou o de Arquivos de Programas.</summary>
    public static string? MpCmdRun()
    {
        string plat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows Defender\Platform");
        try
        {
            var novo = Directory.Exists(plat)
                ? Directory.GetDirectories(plat).OrderByDescending(d => Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : new Version())
                    .Select(d => Path.Combine(d, "MpCmdRun.exe")).FirstOrDefault(File.Exists)
                : null;
            if (novo != null) return novo;
        }
        catch { }
        string pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
        return File.Exists(pf) ? pf : null;
    }

    public static Task<int> AtualizarAssinaturas(IProgress<string> saida, CancellationToken ct) => Rodar("-SignatureUpdate", saida, ct);
    public static Task<int> VerificacaoRapida(IProgress<string> saida, CancellationToken ct) => Rodar("-Scan -ScanType 1", saida, ct);
    public static Task<int> VerificacaoCompleta(IProgress<string> saida, CancellationToken ct) => Rodar("-Scan -ScanType 2", saida, ct);
    public static Task<int> Verificar(string caminho, IProgress<string> saida, CancellationToken ct)
        => Rodar($"-Scan -ScanType 3 -File \"{caminho.TrimEnd('\\')}\"", saida, ct);

    private static async Task<int> Rodar(string args, IProgress<string> saida, CancellationToken ct)
    {
        string exe = MpCmdRun() ?? throw new FileNotFoundException("MpCmdRun.exe não encontrado (o Microsoft Defender está instalado?)");
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("não foi possível iniciar o MpCmdRun");
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) saida.Report(e.Data.Trim()); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) saida.Report(e.Data.Trim()); };
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        using (ct.Register(() => { try { p.Kill(); } catch { } }))
            await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        return p.ExitCode;
    }
}
