using System;

namespace DesktopSheet.Core;

/// <summary>팔레트 한 칸. 같은 이름이 음영과 글자색 두 몫을 맡되 값이 다르다(15.2).</summary>
public readonly record struct PaletteEntry(int Key, string Name, string Shade, string Ink);

/// <summary>
/// 사양서 15.2. 검·흰·회1·회2에 빛의 삼원색과 그 보색을 더한 순색 열 가지.
/// 음영은 순색을 그대로 쓰고, 글자색은 세 채널을 같은 비율로 줄여 밝기만 내린 값이다.
/// </summary>
public static class Palette
{
    public const string DefaultShade = "#FFFFFF";
    public const string DefaultInk   = "#000000";

    public static readonly PaletteEntry[] Entries =
    {
        new(1, "검",   "#000000", "#000000"),
        new(2, "흰",   "#FFFFFF", "#FFFFFF"),
        new(3, "회1",  "#C0C0C0", "#737373"),
        new(4, "회2",  "#808080", "#3C3C3C"),
        new(5, "빨강", "#FF0000", "#AE0000"),
        new(6, "초록", "#00FF00", "#006300"),
        new(7, "파랑", "#0000FF", "#0000FF"),
        new(8, "청록", "#00FFFF", "#005F5F"),
        new(9, "자홍", "#FF00FF", "#980098"),
        new(0, "노랑", "#FFFF00", "#585800"),
    };

    public static PaletteEntry ByName(string name) =>
        Array.Find(Entries, e => e.Name == name);

    /// <summary>15.3. 글자색을 고르지 않은 칸은 음영에서 대비가 높은 쪽으로 저절로 뒤집힌다.</summary>
    public static string AutoInk(string shadeHex) =>
        Contrast(shadeHex, "#000000") >= Contrast(shadeHex, "#FFFFFF") ? "#000000" : "#FFFFFF";

    /// <summary>WCAG 명도 대비. 1 에서 21 사이의 값이다.</summary>
    public static double Contrast(string hexA, string hexB)
    {
        double la = Luminance(hexA), lb = Luminance(hexB);
        (double hi, double lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    public static double Luminance(string hex)
    {
        (double r, double g, double b) = Channels(hex);
        return 0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);
    }

    private static (double, double, double) Channels(string hex)
    {
        string h = hex.TrimStart('#');
        return (Convert.ToInt32(h[..2], 16) / 255.0,
                Convert.ToInt32(h.Substring(2, 2), 16) / 255.0,
                Convert.ToInt32(h.Substring(4, 2), 16) / 255.0);
    }

    private static double ToLinear(double c) =>
        c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}
