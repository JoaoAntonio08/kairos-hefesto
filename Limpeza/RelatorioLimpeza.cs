using System.IO;
using System.Net;
using System.Text;

namespace Hefesto;

/// <summary>Relatório HTML da limpeza (mesmo visual do relatório de hardware, ReportBuilder.Css).</summary>
public static class RelatorioLimpeza
{
    /// <summary>Caminho do último relatório gerado nesta sessão (usado pela IA local para explicar).</summary>
    public static string? Ultimo { get; private set; }
    /// <summary>Resumo em texto do último relatório, para a IA local.</summary>
    public static string? UltimoResumo { get; private set; }

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string Gerar(string titulo, IReadOnlyList<ResultadoRegra> regras, ContextoVarredura c,
                               IReadOnlyList<ResultadoRemocao>? remocoes, bool simulacao)
    {
        var sb = new StringBuilder(64 * 1024);
        var resumo = new StringBuilder();
        long encontrado = regras.Where(r => r.Selecionada).Sum(r => r.Total);
        int itens = regras.Where(r => r.Selecionada).Sum(r => r.Itens.Count);

        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>Hefesto · {E(titulo)}</title><style>{ReportBuilder.Css}");
        sb.Append(".hero{font:800 46px Archivo,'Segoe UI',sans-serif;margin:18px 0 0}.chip{display:inline-block;font-size:11px;padding:2px 9px;border:1px solid var(--line);border-radius:10px;color:var(--muted)}");
        sb.Append(".seu{color:var(--gold);font-weight:600}</style></head><body><div class=\"wrap\">");
        sb.Append($"<header><div><h1>Hefesto</h1><p class=\"sub\">{E(titulo)} · {E(Environment.MachineName)} · {DateTime.Now:dd/MM/yyyy HH:mm} · v{E(Ui.Versao)}</p></div></header>");

        if (remocoes == null)
        {
            sb.Append($"<p class=\"hero\">{E(Ui.Tam(encontrado))}</p><p class=\"muted\">podem ser liberados em {itens:N0} itens");
            sb.Append(simulacao ? " · <b>modo simulação: nada foi alterado</b></p>" : "</p>");
            resumo.AppendLine($"{titulo}: {Ui.Tam(encontrado)} podem ser liberados em {itens} itens (simulação, nada foi alterado).");
        }
        else
        {
            long lix = remocoes.Where(r => r.Destino == Destino.Lixeira).Sum(r => r.Item.Tamanho);
            long qua = remocoes.Where(r => r.Destino == Destino.Quarentena).Sum(r => r.Item.Tamanho);
            int falhas = remocoes.Count(r => r.Destino == Destino.Falhou);
            sb.Append($"<p class=\"hero\">{E(Ui.Tam(lix + qua))}</p><p class=\"muted\">liberados · {E(Ui.Tam(lix))} na Lixeira");
            if (qua > 0) sb.Append($" · {E(Ui.Tam(qua))} na quarentena (não cabiam na Lixeira)");
            if (falhas > 0) sb.Append($" · {falhas:N0} itens não puderam ser removidos (em uso ou sem permissão)");
            sb.Append("</p><p class=\"note\">Nada foi apagado de vez: para desfazer, restaure da Lixeira do Windows ou da quarentena em Limpeza.</p>");
            resumo.AppendLine($"{titulo}: {Ui.Tam(lix)} enviados à Lixeira, {Ui.Tam(qua)} à quarentena, {falhas} falhas.");
        }

        sb.Append("<section><h2>Por regra</h2><div class=\"card\"><table><tr><th>Regra</th><th>Itens</th><th class=\"num\">Tamanho</th><th>Situação</th></tr>");
        foreach (var r in regras)
        {
            string sit = r.Itens.Count == 0 ? "nada encontrado" : !r.Selecionada ? "não marcada" : remocoes == null ? (simulacao ? "seria limpa" : "encontrada") : "limpa";
            sb.Append($"<tr><td><b>{E(r.Nome)}</b><br><span class=\"muted\">{E(r.Descricao)}</span></td><td>{r.Itens.Count:N0}</td><td class=\"num\">{E(Ui.Tam(r.Total))}</td><td>{E(sit)}</td></tr>");
            resumo.AppendLine($"- {r.Nome}: {r.Itens.Count} itens, {Ui.Tam(r.Total)} ({sit}).");
        }
        sb.Append("</table></div></section>");

        var seus = c.PodeSerSeu.Values.OrderBy(p => p.Classe == "seu" ? 0 : p.Classe == "talvez" ? 1 : 2).ThenByDescending(p => p.Tamanho).ToList();
        if (seus.Count > 0)
        {
            sb.Append("<section><h2>Pode ser seu</h2><p class=\"muted\">Arquivos de trabalho (vídeo, documento, código, projeto) encontrados em pastas temporárias ou em Downloads. ");
            sb.Append("<b>A limpeza não mexe neles.</b> Confira e mova para uma pasta sua o que for importante.</p><div class=\"card\"><table><tr><th>Arquivo</th><th>Por quê</th><th class=\"num\">Tamanho</th></tr>");
            foreach (var p in seus.Take(300))
            {
                string cls = p.Classe == "seu" ? "<span class=\"seu\">provavelmente seu</span>" : p.Classe == "talvez" ? "talvez seja seu" : "parece gerado por programa";
                sb.Append($"<tr><td><b>{E(p.Nome)}</b><br><span class=\"muted\">{E(p.Pasta)}</span></td><td>{cls}<br><span class=\"muted\">{E(p.Motivo)}</span></td><td class=\"num\">{E(p.TamanhoTexto)}</td></tr>");
            }
            if (seus.Count > 300) sb.Append($"<tr><td colspan=\"3\" class=\"muted\">… e mais {seus.Count - 300:N0}</td></tr>");
            sb.Append("</table></div></section>");
            resumo.AppendLine($"Pode ser seu: {seus.Count(p => p.Classe == "seu")} arquivos provavelmente do usuário em pastas temporárias/Downloads (não foram limpos).");
            foreach (var p in seus.Where(p => p.Classe == "seu").Take(15)) resumo.AppendLine($"  · {p.Nome} ({p.TamanhoTexto}) em {p.Pasta}");
        }

        var prot = c.Protegidos.GroupBy(p => p.Motivo).OrderByDescending(g => g.Count()).ToList();
        if (prot.Count > 0)
        {
            sb.Append("<section><h2>Protegidos</h2><p class=\"muted\">Itens que as regras encontraram mas a lista de proteção barrou.</p><div class=\"card\"><table><tr><th>Motivo</th><th>Itens</th><th>Exemplo</th></tr>");
            foreach (var g in prot)
                sb.Append($"<tr><td>{E(g.Key)}</td><td>{g.Count():N0}</td><td class=\"muted\">{E(g.First().Caminho)}</td></tr>");
            sb.Append("</table></div></section>");
            resumo.AppendLine($"Protegidos: {c.Protegidos.Count} itens barrados pela lista de proteção.");
        }

        if (remocoes != null)
        {
            var quar = remocoes.Where(r => r.Destino == Destino.Quarentena).ToList();
            var falhas = remocoes.Where(r => r.Destino is Destino.Falhou or Destino.Protegido).ToList();
            if (quar.Count > 0)
            {
                sb.Append("<section><h2>Na quarentena</h2><div class=\"card\"><table><tr><th>Item</th><th>Motivo</th><th class=\"num\">Tamanho</th></tr>");
                foreach (var q in quar) sb.Append($"<tr><td>{E(q.Item.Caminho)}</td><td class=\"muted\">{E(q.Detalhe)}</td><td class=\"num\">{E(Ui.Tam(q.Item.Tamanho))}</td></tr>");
                sb.Append("</table></div></section>");
            }
            if (falhas.Count > 0)
            {
                sb.Append("<section><h2>Não removidos</h2><div class=\"card\"><table><tr><th>Item</th><th>Motivo</th></tr>");
                foreach (var f in falhas.Take(200)) sb.Append($"<tr><td>{E(f.Item.Caminho)}</td><td class=\"muted\">{E(f.Detalhe)}</td></tr>");
                if (falhas.Count > 200) sb.Append($"<tr><td colspan=\"2\" class=\"muted\">… e mais {falhas.Count - 200:N0}</td></tr>");
                sb.Append("</table></div></section>");
            }
        }

        if (!c.Avisos.IsEmpty)
        {
            sb.Append("<section><h2>Avisos</h2><div class=\"card\">");
            foreach (var a in c.Avisos) sb.Append($"<p>{E(a)}</p>");
            sb.Append("</div></section>");
            foreach (var a in c.Avisos) resumo.AppendLine("Aviso: " + a);
        }

        sb.Append("<footer>Gerado pelo Hefesto. A limpeza nunca apaga de vez: os itens vão para a Lixeira do Windows ou para a quarentena do Hefesto.</footer></div></body></html>");

        string nome = (titulo.StartsWith("Limpeza profunda") ? "limpeza-profunda-" : "limpeza-") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html";
        string arq = Path.Combine(Ui.PastaDocumentos("Relatórios"), nome);
        File.WriteAllText(arq, sb.ToString(), Encoding.UTF8);
        Ultimo = arq;
        UltimoResumo = resumo.ToString();
        return arq;
    }
}
