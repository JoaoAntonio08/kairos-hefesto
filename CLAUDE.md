# Kairós Hefesto

App gratuito e de código aberto para Windows que reúne diagnóstico, manutenção e jogos.
No app o nome aparece só como **Hefesto**. Repositório: `kairos-hefesto`, na conta pessoal do GitHub do João.

Definição completa do projeto (fonte da verdade, escrita antes do código):
https://claude.ai/artifact/7jzwfa2jU2D3fCWaN9F2Ps

Responda e escreva commits, comentários de UI e documentação em **português do Brasil**.

## Estado atual

- **0.1.0 (Fundação)**: feito. Projeto/assembly/namespace `Hefesto`, ícone e logos da Kairós em `Assets/`,
  módulos sem o prefixo "RESET", `LICENSE` com dois copyrights, `NOTICES.md`, README com a origem no topo.
  Configurações em `%AppData%\Hefesto`. CI compila em todo push; release só por tag `v*`, com `SHA256SUMS.txt`.
- Próximo: **0.2 (Visual)** no branch `visual`.
- Nomes dos módulos depois do rebrand: menu DIAGNÓSTICO, JOGOS, DESEMPENHO, FERRAMENTAS, MAKER;
  páginas BENCH, OTIMIZAR, OVERCLOCK, DRIVERS, INTEGRAÇÃO. Os `x:Name` internos (ex.: `GrpExplode`) ficaram como estavam.
- Os logos são gerados a partir de `public/media/logo.png` do site: `logo.png` (claro, tema escuro),
  `logo-dark.png` (escuro, tema claro), `logo-badge.png` (fundo `#0A0A0B`, usado no relatório) e `app.ico` (16 a 256 px).

## Origem

Baseado no **ResetExplode** v0.16 de Gustavo (GustavoReset): https://github.com/GustavoReset/ResetExplode
- WPF, .NET 8 (`net8.0-windows`), roda como administrador (`app.manifest`).
- Licença MIT. O upstream não aceita pull requests.
- O repositório `kairos-hefesto` é próprio (não um fork), mas sobe **com o histórico de commits do ResetExplode**.
  O ResetExplode fica cadastrado como remoto `upstream` para trazer melhorias.

## Decisões já tomadas

| Tema | Decisão |
| --- | --- |
| Licença | MIT. `LICENSE` com duas linhas de copyright: Gustavo (código original) e João (código novo) |
| Créditos | `NOTICES.md` + tela **Sobre → Licenças** no app listando tudo que veio de fora |
| Marca | Remover "RESET" / "RESET EXPLODE" de nome, logo, títulos e textos. Nome no app: Hefesto |
| Visual | Identidade da Kairós (abaixo). Manter tema claro e escuro |
| Módulos herdados | Todos mantidos, inclusive Maker e iluminação |
| Aviso ao Gustavo | Não será feito; o crédito fica no LICENSE, NOTICES.md e histórico |

## Identidade visual

Fonte: repositório do site da Kairós, https://github.com/JoaoAntonio08/Kairos-site
(`src/index.css` e `public/media/logo.png`: ampulheta sobre um controle, branca com fundo transparente).

| Papel | Cor |
| --- | --- |
| Fundo | `#0A0A0B` |
| Cartões | `#121214` |
| Linhas | `#27272B` |
| Texto | `#F1EFEA` |
| Texto secundário | `#A3A29D` |
| Destaque (dourado) | `#F2B544` |

Fontes: **Archivo** (500/800) para títulos e números grandes, **Inter** (400/500) para texto. Ambas SIL OFL:
embutir no app e incluir o texto da OFL em Sobre → Licenças.
Para tirar a "cara de IA": hierarquia forte (números grandes em Archivo), menos cartões iguais lado a lado,
dourado usado com parcimônia. O tema hoje está concentrado em `Theme.xaml` e `ThemeManager.cs`.

## Componentes de terceiros

| Componente | Licença | Regra |
| --- | --- | --- |
| ResetExplode | MIT | Manter aviso de copyright |
| LibreHardwareMonitorLib | MPL 2.0 | Manter aviso; alterações nos arquivos dela continuam MPL |
| TraceEvent, Vortice | MIT | Manter avisos |
| Drivers seriais em `Drivers/` | Termos de cada fabricante | Fora da MIT (ver `Drivers/LEIA-ME.md`) |
| MFTLib (futuro, limpeza profunda) | MIT | NuGet `MFTLib`, repo mtschoen/MFTLib; exige admin |
| winapp2.ini (futuro) | CC BY-SA 4.0 | Baixar em tempo de execução e dar crédito; não embutir |
| Emuladores e ports (SpartanPort, shadPS4, RPCS3, PCSX2, OpenRCT2...) | Quase todos GPL | Nunca copiar código; baixar do GitHub oficial e rodar como processo separado |

## Regras do projeto (valem para todo commit)

1. Nenhum arquivo de jogo, ROM, BIOS ou dump no repositório, no instalador ou em links do app.
2. Nada de injeção em processos de jogo, cheats, ou burlar DRM/anti-cheat. O overlay mede FPS por ETW.
3. Código GPL nunca entra no código do Hefesto.
4. Downloads só de fontes oficiais, com verificação de hash/assinatura quando houver.
5. Limpeza nunca apaga de vez: vai para a Lixeira. Se o arquivo for maior que a cota da Lixeira
   (checar com `SHQueryRecycleBin` e a capacidade configurada), vai para uma pasta de quarentena.
   Usar a API do Windows (`IFileOperation` com undo, ou `FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin)`),
   nunca exclusão direta com confirmações desligadas.
6. Lista de proteção fixa na limpeza: senhas/cookies/histórico de navegadores (ex.: `AppData\Roaming\Opera Software`),
   `.ssh`, saves de jogo, pastas com `.git`.
7. Nada que reduza a segurança do Windows (Defender, UAC, Update), regra herdada do ResetExplode.
8. Pedir confirmação antes de qualquer ação irreversível (push forçado, apagar arquivos, reescrever histórico).

## Roteiro

1. **Fundação (0.1)**: repositório com histórico do ResetExplode, renomear projeto/assembly/namespace/ícone,
   `LICENSE`, `NOTICES.md`, README com a origem no topo.
2. **Visual (0.2)**: tema Kairós, logo, tela Sobre → Licenças.
3. **Limpeza rápida (0.3)**: regras (Temp e CrashDumps com mais de N dias, caches de dev e navegador só na pasta Cache,
   instaladores velhos em Downloads), modo simulação, envio para Lixeira/quarentena, relatório HTML
   (reaproveitar `ReportBuilder.cs`) com seção "pode ser seu" (extensões de trabalho fora do lugar:
   `.blend`, `.mp4`, `.md`, `.docx`, `.py`, `.pas`, `.cs`, `.psd`; nomes digitados por humano vs GUID).
4. **Limpeza profunda (0.4)**: mapa do disco pela MFT (MFTLib), fallback de enumeração paralela para não-NTFS;
   regras do winapp2.
5. **Clássicos (0.5)**: lançador de emuladores/ports (SpartanPort, RPCS3, PCSX2, OpenRCT2), baixa das Releases
   oficiais, atualiza, configura, abre. Usa `GameLibrary.cs`/`Vdf.cs` para achar jogos originais na Steam/GOG.
6. **Extras (0.6+)**: desinstalador com limpeza de sobras (ref. Bulk Crap Uninstaller, Apache 2.0),
   backup de saves, verificação do Windows Defender (`MpCmdRun`), IA local via Ollama para explicar relatórios.

Fluxo de trabalho: `main` sempre estável, um branch por frente (`rebrand`, `limpeza`, `classicos`),
versionamento semântico a partir de `0.1.0`, `.exe` só nas Releases do GitHub com SHA-256.
