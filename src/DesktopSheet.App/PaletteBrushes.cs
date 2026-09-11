using System.Collections.Generic;
using System.Windows.Media;
using DesktopSheet.Core;

namespace DesktopSheet.App;

/// <summary>사양서 15.2 의 색을 그리기용 붓으로 바꿔 둔다. 같은 색을 매번 새로 만들지 않는다.</summary>
internal static class PaletteBrushes
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();

    public static SolidColorBrush Of(string hex)
    {
        if (Cache.TryGetValue(hex, out SolidColorBrush? b)) return b;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        Cache[hex] = brush;
        return brush;
    }

    public static SolidColorBrush Shade(string? paletteName)
    {
        if (paletteName is null) return Of(Palette.DefaultShade);
        PaletteEntry e = Palette.ByName(paletteName);
        return Of(e.Shade ?? Palette.DefaultShade);
    }

    /// <summary>15.3. 글자색을 고르지 않았으면 음영에 따라 검정과 흰색으로 뒤집힌다.</summary>
    public static SolidColorBrush Ink(string? inkName, string? shadeName)
    {
        if (inkName is not null)
        {
            PaletteEntry e = Palette.ByName(inkName);
            if (e.Ink is not null) return Of(e.Ink);
        }
        string shade = shadeName is null ? Palette.DefaultShade : Palette.ByName(shadeName).Shade ?? Palette.DefaultShade;
        return Of(Palette.AutoInk(shade));
    }
}
