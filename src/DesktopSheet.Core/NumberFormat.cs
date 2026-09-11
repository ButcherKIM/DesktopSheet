using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DesktopSheet.Core;

/// <summary>서식을 적용한 결과. 색은 [Red] 처럼 서식 코드가 지정한 글자색이며 없으면 null 이다.</summary>
public readonly record struct FormatResult(string Text, string? Color);

/// <summary>
/// 사양서 4장. 엑셀 표기법을 따르되 자리표시자·퍼센트·지수·날짜·세미콜론 세 구역·색 지정까지만 해석한다.
/// 분수, 조건 구역, 통화 기호 자동 변환, 텍스트 구역은 넣지 않는다.
/// </summary>
public static class NumberFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>서식 코드에 쓰는 색 이름. 15장 팔레트의 열 가지다.</summary>
    private static readonly (string[] Names, string Color)[] Colors =
    {
        (new[] { "Black",   "검"   }, "검"),
        (new[] { "White",   "흰"   }, "흰"),
        (new[] { "Gray1",   "회1"  }, "회1"),
        (new[] { "Gray2",   "회2"  }, "회2"),
        (new[] { "Red",     "빨강" }, "빨강"),
        (new[] { "Green",   "초록" }, "초록"),
        (new[] { "Blue",    "파랑" }, "파랑"),
        (new[] { "Cyan",    "청록" }, "청록"),
        (new[] { "Magenta", "자홍" }, "자홍"),
        (new[] { "Yellow",  "노랑" }, "노랑"),
    };

    public static bool IsGeneral(string? code) =>
        string.IsNullOrWhiteSpace(code) || code == "일반" || code.Equals("General", StringComparison.OrdinalIgnoreCase);

    public static string Apply(double value, string code) => ApplyWithColor(value, code).Text;

    public static FormatResult ApplyWithColor(double value, string code)
    {
        if (IsGeneral(code)) return new FormatResult(DisplayFormatter.General(value), null);

        string[] sections = code.Split(';');
        if (sections.Length > 3) return new FormatResult(CellErrorText.Of(CellError.Value), null);

        string section;
        double v = value;
        if (sections.Length == 1) section = sections[0];
        else if (value < 0)                      { section = sections[1]; v = Math.Abs(value); }
        else if (value == 0 && sections.Length == 3) section = sections[2];
        else section = sections[0];

        (section, string? color) = TakeColor(section.Trim());

        try
        {
            string text = IsDateSection(section)
                ? FormatDate(v, section)
                : v.ToString(section, Inv);
            return new FormatResult(text, color);
        }
        catch (FormatException)
        {
            return new FormatResult(CellErrorText.Of(CellError.Value), color);
        }
    }

    /// <summary>날짜 서식인가. y·m·d 가 있고 숫자 자리표시자가 없으면 날짜로 본다.</summary>
    public static bool IsDateSection(string section)
    {
        bool hasDateToken = section.Any(c => c is 'y' or 'Y' or 'd' or 'D' or 'm' or 'M');
        bool hasNumberToken = section.Any(c => c is '0' or '#');
        return hasDateToken && !hasNumberToken;
    }

    private static string FormatDate(double serial, string section)
    {
        int s = (int)Math.Floor(serial);
        if (s < SerialDate.MinSerial || s > SerialDate.MaxSerial)
            return CellErrorText.Of(CellError.Number);
        return SerialDate.ToDate(s).ToString(ToNetDatePattern(section), Inv);
    }

    /// <summary>엑셀의 mm(월)을 .NET 의 MM 으로 바꾼다. 시간을 넣지 않으므로 m 은 언제나 월이다(5장).</summary>
    public static string ToNetDatePattern(string section)
    {
        var sb = new StringBuilder(section.Length);
        foreach (char c in section) sb.Append(c == 'm' ? 'M' : c);
        return sb.ToString();
    }

    private static (string Section, string? Color) TakeColor(string section)
    {
        if (!section.StartsWith('[')) return (section, null);
        int close = section.IndexOf(']');
        if (close < 0) return (section, null);

        string inside = section[1..close].Trim();
        foreach (var (names, color) in Colors)
            if (names.Any(n => n.Equals(inside, StringComparison.OrdinalIgnoreCase)))
                return (section[(close + 1)..], color);

        return (section, null);   // 모르는 대괄호는 색이 아니므로 그대로 둔다
    }
}
