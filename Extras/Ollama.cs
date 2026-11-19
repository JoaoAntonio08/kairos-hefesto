using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Hefesto;

/// <summary>
/// IA local pelo Ollama (localhost:11434). Nada sai do PC: o texto do relatório vai só para o modelo rodando aqui.
/// O Hefesto não embute modelos; o Ollama é instalado pelo winget e os modelos baixados pelo próprio Ollama.
/// </summary>
public static class Ollama
{
    public const string Base = "http://127.0.0.1:11434";
    /// <summary>Modelo sugerido: pequeno (cerca de 2 GB) e bom em português.</summary>
    public const string ModeloSugerido = "qwen2.5:3b";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static string? Exe()
    {
        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Ollama\ollama.exe");
        return File.Exists(p) ? p : null;
    }

    /// <summary>Lista de modelos instalados, ou null se o Ollama não estiver rodando.</summary>
    public static async Task<List<string>?> Modelos(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(Base + "/api/tags", cts.Token));
            return doc.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString() ?? "").Where(n => n.Length > 0).ToList();
        }
        catch { return null; }
    }

    /// <summary>Inicia o servidor do Ollama em segundo plano (se instalado e parado).</summary>
    public static async Task<bool> Iniciar()
    {
        string? exe = Exe();
        if (exe == null) return false;
        Process.Start(new ProcessStartInfo(exe, "serve") { UseShellExecute = false, CreateNoWindow = true });
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(500);
            if (await Modelos() != null) return true;
        }
        return false;
    }

    /// <summary>Baixa um modelo pelo próprio Ollama (POST /api/pull), relatando o progresso.</summary>
    public static async Task Baixar(string modelo, IProgress<(string, double)> prog, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Base + "/api/pull")
        { Content = new StringContent(JsonSerializer.Serialize(new { model = modelo, stream = true }), Encoding.UTF8, "application/json") };
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        using var sr = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
        string? l;
        while ((l = await sr.ReadLineAsync(ct)) != null)
        {
            if (l.Length == 0) continue;
            using var d = JsonDocument.Parse(l);
            var r = d.RootElement;
            if (r.TryGetProperty("error", out var err)) throw new InvalidOperationException(err.GetString());
            string st = r.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            double f = r.TryGetProperty("total", out var t) && t.GetInt64() > 0 && r.TryGetProperty("completed", out var c) ? (double)c.GetInt64() / t.GetInt64() : -1;
            prog.Report((st, f));
        }
    }

    public const string Sistema =
        "Você é o assistente do Hefesto, um programa de manutenção de PC. Responda sempre em português do Brasil, " +
        "com frases curtas e simples, para quem não é técnico. Explique o que o relatório mostra, o que é seguro fazer e o que " +
        "merece atenção. Nunca sugira desligar o Windows Defender, o UAC, o Firewall ou o Windows Update, nem apagar arquivos " +
        "do sistema. Se não tiver certeza, diga que não tem certeza. Não invente números que não estão no texto.";

    /// <summary>Gera a resposta em streaming (POST /api/generate), entregando os pedaços conforme chegam.</summary>
    public static async Task Gerar(string modelo, string pedido, IProgress<string> pedacos, CancellationToken ct)
    {
        var corpo = new { model = modelo, system = Sistema, prompt = pedido, stream = true, options = new { temperature = 0.3 } };
        using var req = new HttpRequestMessage(HttpMethod.Post, Base + "/api/generate")
        { Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json") };
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        using var sr = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
        string? l;
        while ((l = await sr.ReadLineAsync(ct)) != null)
        {
            if (l.Length == 0) continue;
            using var d = JsonDocument.Parse(l);
            if (d.RootElement.TryGetProperty("error", out var err)) throw new InvalidOperationException(err.GetString());
            if (d.RootElement.TryGetProperty("response", out var r)) pedacos.Report(r.GetString() ?? "");
            if (d.RootElement.TryGetProperty("done", out var dn) && dn.GetBoolean()) break;
        }
    }

    /// <summary>Texto legível de um relatório HTML (sem tags, estilos e scripts), limitado para caber no contexto do modelo.</summary>
    public static string TextoDeHtml(string html, int max = 12000)
    {
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<(style|script)[^>]*>.*?</\1>", " ", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<br\s*/?>|</(p|tr|h\d|li|div|section)>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"</t[dh]>", " | ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        string t = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", ""));
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[ \t]+", " ");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\n\s*\n+", "\n").Trim();
        return t.Length > max ? t[..max] + "\n[…relatório cortado]" : t;
    }
}
