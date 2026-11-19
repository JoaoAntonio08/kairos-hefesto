# Avisos de terceiros

O Hefesto é distribuído sob a licença MIT (arquivo [LICENSE](LICENSE)). Ele inclui ou usa os componentes abaixo,
cada um com a sua própria licença. Esta lista também aparece no app, em **Sobre → Licenças**.

## Código de origem

### ResetExplode
- Autor: Gustavo (GustavoReset)
- Origem: https://github.com/GustavoReset/ResetExplode (v0.16)
- Licença: MIT
- O Hefesto começou como uma cópia do ResetExplode, com todo o histórico de commits preservado.
  O aviso de copyright original está no [LICENSE](LICENSE).

## Bibliotecas (NuGet)

| Componente | Uso no Hefesto | Licença | Origem |
| --- | --- | --- | --- |
| LibreHardwareMonitorLib | Sensores (temperatura, clock, carga, ventoinhas) | MPL 2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| Microsoft.Diagnostics.Tracing.TraceEvent | Medição de FPS por ETW (overlay) | MIT | https://github.com/microsoft/perfview |
| Vortice.Direct3D11, Vortice.Direct3D12, Vortice.D3DCompiler, Vortice.Dxc | Stress de GPU e benchmarks (Direct3D 11/12, DXR) | MIT | https://github.com/amerkoleci/Vortice.Windows |
| System.Management | Consultas WMI | MIT | https://github.com/dotnet/runtime |
| System.Security.Cryptography.ProtectedData | Cifra DPAPI das chaves salvas | MIT | https://github.com/dotnet/runtime |
| MFTLib 0.2.0 | Leitura da MFT do NTFS (Limpeza profunda) | MIT | https://github.com/mtschoen/MFTLib |
| SharpCompress | Extração de .7z (Clássicos) | MIT | https://github.com/adamhathcock/sharpcompress |

**MPL 2.0 (LibreHardwareMonitorLib):** a biblioteca é usada sem alterações, como pacote NuGet.
Se algum arquivo dela for modificado, esse arquivo continua sob a MPL 2.0 e o código-fonte alterado precisa ser publicado.

## Drivers embutidos

Os drivers seriais em [`Drivers/`](Drivers/LEIA-ME.md) (WCH CH340, Silicon Labs CP210x, FTDI, Arduino) são os pacotes
oficiais de cada fabricante, sem modificação e com a assinatura original. **Não fazem parte da licença MIT**:
seguem os termos de cada fabricante. Versões, origens e SHA-256 estão em [`Drivers/LEIA-ME.md`](Drivers/LEIA-ME.md).

## Identidade visual

- Logo da Kairós (ampulheta sobre um controle): © João (JoaoAntonio08). Vem do repositório
  https://github.com/JoaoAntonio08/Kairos-site (`public/media/logo.png`).
- Fontes **Archivo** e **Inter**: SIL Open Font License 1.1, embutidas no app. O texto completo da OFL está em
  [`Fonts/`](Fonts) e no próprio app, em **Sobre e licenças**.

## Dados baixados em tempo de execução

| Dado | Licença | Como é usado |
| --- | --- | --- |
| winapp2.ini (projeto Winapp2, MoscaDotTo e colaboradores) | CC BY-SA 4.0 | Baixado de https://github.com/MoscaDotTo/Winapp2 quando você usa a Limpeza profunda, conferido pelo hash do Git e guardado em `%LocalAppData%\Hefesto\winapp2`. Nunca embutido. Só as regras de arquivo são usadas. |

## Programas externos (não incluídos)

O Hefesto pode baixar e abrir outros programas, sempre das fontes oficiais e como processo separado.
Nenhum código deles entra no Hefesto.

| Programa | Licença | Como é usado |
| --- | --- | --- |
| PawnIO | Licença própria | Instalado pelo winget (`namazso.PawnIO`) para ler sensores da CPU |
| Corsair iCUE SDK | Termos da Corsair | Baixado do GitHub oficial só quando o usuário confirma |
| RPCS3 | GPL-2.0 | Baixado de https://github.com/RPCS3/rpcs3-binaries-win/releases, SHA-256 conferido |
| PCSX2 | GPL-3.0 | Baixado de https://github.com/PCSX2/pcsx2/releases, SHA-256 conferido |
| shadPS4 | GPL-2.0 | Baixado de https://github.com/shadps4-emu/shadPS4/releases, SHA-256 conferido |
| SpartanPort | GPL-2.0 | Baixado de https://github.com/FelipePrado0/SpartanPort/releases, SHA-256 conferido |
| OpenRCT2 | GPL-3.0 | Baixado de https://github.com/OpenRCT2/OpenRCT2/releases, SHA-256 conferido |
| Ollama | MIT | Instalado pelo winget (`Ollama.Ollama`) só quando o usuário pede, para a IA local |
| Microsoft Defender (MpCmdRun) | Parte do Windows | Usado para verificar vírus; o Hefesto nunca muda a configuração dele |

Jogos, ROMs, BIOS, firmware e dumps nunca são baixados nem indicados pelo Hefesto.

O desinstalador foi inspirado no fluxo do [Bulk Crap Uninstaller](https://github.com/Klocman/Bulk-Crap-Uninstaller)
(Apache 2.0), sem usar código dele.
| winapp2.ini (a partir da versão 0.4) | CC BY-SA 4.0 | Baixado em tempo de execução, com crédito; nunca embutido |
