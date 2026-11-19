using System.Windows;
using System.Windows.Media;

namespace Hefesto;

/// <summary>
/// Troca entre modo escuro e claro substituindo os pincéis da paleta (Theme.xaml). O XAML os referencia com DynamicResource,
/// então a troca vale na hora, sem recarregar janelas. Overlay e aviso de dispositivo ficam sempre escuros (aparecem sobre jogos).
/// </summary>
public static class ThemeManager
{
    public static bool IsLight { get; private set; }

    // Paleta da Kairós: escuro = identidade do site; claro = papel quente com o mesmo dourado, um tom mais fechado para dar contraste.
    private static readonly (object Key, string Dark, string Light)[] Palette =
    {
        ("Bg", "#0A0A0B", "#F7F5F0"),
        ("Panel", "#0E0E10", "#EFECE5"),
        ("Card", "#121214", "#FFFFFF"),
        ("Hover", "#1C1C1F", "#E8E4DB"),
        ("Line", "#27272B", "#DDD8CD"),
        ("LineStrong", "#3D3D43", "#A8A397"),
        ("Fg", "#F1EFEA", "#0A0A0B"),
        ("Muted", "#A3A29D", "#55544E"),
        ("Faint", "#6E6D68", "#77756E"),
        ("Ink", "#0A0A0B", "#F7F5F0"),
        ("Accent", "#F2B544", "#B47A12"),
        ("AccentInk", "#0A0A0B", "#FFFFFF"),
        ("ThumbBrush", "#2E2E32", "#CFCAC0"),
        ("ThumbHover", "#6A6A6F", "#A5A096"),
        ("ThumbDrag", "#9A9A9F", "#7A766D"),
        (SystemColors.ControlBrushKey, "#0A0A0B", "#F7F5F0"),
        (SystemColors.HighlightBrushKey, "#1C1C1F", "#E8E4DB"),
        (SystemColors.InactiveSelectionHighlightBrushKey, "#1C1C1F", "#E8E4DB"),
    };


    public static void Apply(bool light)
    {
        IsLight = light;
        var res = Application.Current.Resources;
        foreach (var (key, dark, lightHex) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(light ? lightHex : dark);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            res[key] = brush; // o XAML usa DynamicResource: trocar o pincel atualiza todos os controles na hora
        }
    }
}
