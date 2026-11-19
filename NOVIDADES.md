## Novidades da v0.6.0

Todo o roteiro do Hefesto em uma versão.

- **Visual Kairós**: tema preto e dourado, fontes Archivo e Inter embutidas, tela **Sobre e licenças** com o texto de cada licença.
- **Limpeza rápida**: Temp, despejos de travamento, caches e instaladores velhos. Modo simulação por padrão, tudo vai para a Lixeira
  (ou para a quarentena do Hefesto, se não couber), relatório HTML e a seção **Pode ser seu** com arquivos de trabalho achados em
  pastas temporárias. Lista de proteção fixa para senhas, chaves, saves, pastas pessoais e repositórios git.
- **Limpeza profunda**: mapa do disco com os maiores arquivos, lixo no disco inteiro pela MFT do NTFS e regras do winapp2
  baixadas do repositório oficial.
- **Clássicos**: RPCS3, PCSX2, shadPS4, SpartanPort e OpenRCT2 baixados das Releases oficiais, com SHA-256 conferido.
  Nenhum jogo, ROM, BIOS ou firmware é baixado ou indicado.
- **Backup de saves**, **Desinstalador** com limpeza de sobras, **Verificação de vírus** pelo Microsoft Defender e **IA local** pelo Ollama.

Confira o download com o `SHA256SUMS.txt` desta página. O app pede administrador, como antes.

## Novidades da v0.1.0

Primeira versão do **Hefesto**, baseada no ResetExplode v0.16 de Gustavo (GustavoReset), sob licença MIT.

- Novo nome e ícone: o app agora se chama **Hefesto** e usa o logo da Kairós (ampulheta sobre um controle).
- Os módulos perderam o prefixo antigo: DIAGNÓSTICO, JOGOS, DESEMPENHO, FERRAMENTAS e MAKER no menu;
  BENCH, OTIMIZAR, OVERCLOCK, DRIVERS e INTEGRAÇÃO nas páginas.
- Configurações agora ficam em `%AppData%\Hefesto` (as do ResetExplode não são migradas).
- `LICENSE` com os dois avisos de copyright e `NOTICES.md` com o crédito de todos os componentes de terceiros.
- Releases só por tag de versão, com `SHA256SUMS.txt` para conferir o download.

Todas as funções do ResetExplode v0.16 continuam no app.
