using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Hefesto;

public sealed record LicenseItem(string Name, string License, string Use, string Copyright, string Url, string FullText);

/// <summary>Sobre → Licenças: versão, origem (ResetExplode) e a lista de tudo que veio de fora, com o texto de cada licença.</summary>
public partial class SobrePage : UserControl, IPagina
{
    private bool _loaded;

    public SobrePage() => InitializeComponent();

    public void Mostrar()
    {
        Logo.Source = new BitmapImage(new Uri(ThemeManager.IsLight
            ? "pack://application:,,,/Assets/logo-dark.png" : "pack://application:,,,/Assets/logo.png"));
        if (_loaded) return;
        _loaded = true;
        VersionText.Text = $"Versão {Ui.Versao} · Kairós · licença MIT";
        LicenseList.ItemsSource = Catalogo();
    }

    private static string Recurso(string nome)
    {
        using var s = typeof(SobrePage).Assembly.GetManifestResourceStream("Licenses/" + nome);
        if (s == null) return "(texto não encontrado no programa)";
        using var r = new StreamReader(s);
        return r.ReadToEnd().Trim();
    }

    private static string Mit(string copyright) => copyright + "\n\n" + MitBody;

    public static List<LicenseItem> Catalogo() => new()
    {
        new("Hefesto", "MIT", "Este programa.",
            "Copyright (c) 2026 Gustavo (GustavoReset) · Copyright (c) 2026 João (JoaoAntonio08)",
            "https://github.com/JoaoAntonio08/kairos-hefesto", Recurso("LICENSE.txt")),
        new("ResetExplode v0.16", "MIT", "Código de origem do Hefesto: diagnóstico, sensores, benchmarks, overlay, jogos, maker e iluminação.",
            "Copyright (c) 2026 Gustavo (GustavoReset)",
            "https://github.com/GustavoReset/ResetExplode", Mit("Copyright (c) 2026 Gustavo (GustavoReset)")),
        new("LibreHardwareMonitorLib", "MPL 2.0", "Leitura dos sensores (temperatura, clock, carga, ventoinhas). Usada sem alterações.",
            "Copyright (c) LibreHardwareMonitor e colaboradores",
            "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor", MplNotice),
        new("TraceEvent (Microsoft.Diagnostics.Tracing)", "MIT", "Medição de FPS por eventos ETW, sem tocar no processo do jogo.",
            "Copyright (c) Microsoft Corporation",
            "https://github.com/microsoft/perfview", Mit("Copyright (c) Microsoft Corporation")),
        new("Vortice.Windows", "MIT", "Direct3D 11/12 e DXC para o stress de GPU e os benchmarks gráficos.",
            "Copyright (c) Amer Koleci e colaboradores",
            "https://github.com/amerkoleci/Vortice.Windows", Mit("Copyright (c) Amer Koleci and contributors")),
        new("System.Management, ProtectedData", "MIT", "Consultas WMI e cifra DPAPI das chaves salvas.",
            "Copyright (c) .NET Foundation e colaboradores",
            "https://github.com/dotnet/runtime", Mit("Copyright (c) .NET Foundation and Contributors")),
        new("Archivo", "SIL OFL 1.1", "Fonte dos títulos e números grandes.",
            "Copyright 2020 The Archivo Project Authors",
            "https://github.com/Omnibus-Type/Archivo", Recurso("Archivo-OFL.txt")),
        new("Inter", "SIL OFL 1.1", "Fonte do texto.",
            "Copyright (c) 2016 The Inter Project Authors",
            "https://github.com/rsms/inter", Recurso("Inter-OFL.txt")),
        new("winapp2.ini", "CC BY-SA 4.0", "Regras de limpeza da Limpeza profunda. Baixado em tempo de execução, nunca embutido.",
            "Winapp2 Project (MoscaDotTo e colaboradores)",
            "https://github.com/MoscaDotTo/Winapp2",
            "winapp2.ini é distribuído sob a licença Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0).\n" +
            "O Hefesto baixa o arquivo do repositório oficial quando você abre a Limpeza profunda, lê as regras e não altera o arquivo.\n\n" +
            "Texto completo: https://creativecommons.org/licenses/by-sa/4.0/legalcode"),
        new("Drivers seriais (CH340, CP210x, FTDI, Arduino)", "Termos de cada fabricante",
            "Pacotes oficiais, sem modificação e com a assinatura original. Não fazem parte da licença MIT.",
            "WCH · Silicon Labs · FTDI · Arduino",
            "https://github.com/JoaoAntonio08/kairos-hefesto/blob/main/Drivers/LEIA-ME.md",
            "Estes drivers pertencem aos respectivos fabricantes e são distribuídos conforme os termos de cada um.\n" +
            "Versões, origens oficiais e SHA-256 dos pacotes estão em Drivers/LEIA-ME.md no código-fonte do Hefesto."),
    };

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && url.Length > 0) Ui.AbrirUrl(url);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.Folder);
        Ui.AbrirPasta(AppSettings.Folder);
    }

    private const string MitBody = @"Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";

    private const string MplNotice = @"This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

O Hefesto usa a LibreHardwareMonitorLib como pacote NuGet, sem alterar nenhum arquivo dela.
O código-fonte da biblioteca está em https://github.com/LibreHardwareMonitor/LibreHardwareMonitor.
Se algum arquivo dela for modificado, esse arquivo continua sob a MPL 2.0 e o código alterado precisa ser publicado.";
}
