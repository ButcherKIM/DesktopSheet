using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopSheet.Core;

public enum InputKind { Empty, Number, Text, Formula }

/// <summary>읽어 들인 입력. Number 일 때만 Value 와 FormatCode 가 뜻이 있다.</summary>
public readonly record struct ParsedInput(InputKind Kind, double Value, string Text, string FormatCode);

/// <summary>
/// 사양서 7장. 숫자·퍼센트·지수·날짜 넷만 자동으로 알아듣고 나머지는 텍스트로 둔다.
/// 콤마가 든 입력은 텍스트지만, 붙여넣기에 한해 숫자로 읽는다(10.4).
/// </summary>
public static partial class InputParser
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>5장. 연도를 반드시 적어야 하며 9/11 처럼 연도를 뺀 입력은 날짜로 보지 않는다.</summary>
    private static readonly string[] DatePatterns =
    {
        "yyyy-M-d", "yyyy-MM-dd", "yyyy.M.d", "yyyy.MM.dd",
        "yy-M-d",   "yy-MM-dd",   "yy.M.d",   "yy.MM.dd",
    };

    [GeneratedRegex(@"^\s*([+-]?\d+(?:\.(\d+))?)\s*%\s*$")]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"^\s*[+-]?\d{1,3}(?:,\d{3})+(?:\.\d+)?\s*$")]
    private static partial Regex ThousandsPattern();

    public static ParsedInput Parse(string? raw, bool fromPaste = false)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new ParsedInput(InputKind.Empty, 0, "", "");

        string s = raw.Trim();

        if (s.StartsWith('=')) return new ParsedInput(InputKind.Formula, 0, s, "");

        if (TryDate(s, out int serial))
            return new ParsedInput(InputKind.Number, serial, s, "yyyy-mm-dd");

        Match pct = PercentPattern().Match(s);
        if (pct.Success && double.TryParse(pct.Groups[1].Value, NumberStyles.Float, Inv, out double pv))
        {
            int decimals = pct.Groups[2].Success ? pct.Groups[2].Value.Length : 0;
            string code = decimals == 0 ? "0%" : "0." + new string('0', decimals) + "%";
            return new ParsedInput(InputKind.Number, pv / 100.0, s, code);
        }

        // 콤마가 든 숫자는 직접 칠 때는 텍스트, 붙여넣을 때만 숫자다(10.4).
        if (ThousandsPattern().IsMatch(s))
        {
            if (!fromPaste) return new ParsedInput(InputKind.Text, 0, s, "");
            string bare = s.Replace(",", "");
            if (double.TryParse(bare, NumberStyles.Float, Inv, out double cv) && double.IsFinite(cv))
            {
                int dot = bare.IndexOf('.');
                string code = dot < 0 ? "#,##0" : "#,##0." + new string('0', bare.Length - dot - 1);
                return new ParsedInput(InputKind.Number, cv, s, code);
            }
        }

        if (double.TryParse(s, NumberStyles.Float, Inv, out double v) && double.IsFinite(v))
            return new ParsedInput(InputKind.Number, v, s, "");

        return new ParsedInput(InputKind.Text, 0, s, "");
    }

    private static bool TryDate(string s, out int serial)
    {
        serial = 0;
        if (!DateTime.TryParseExact(s, DatePatterns, Inv, DateTimeStyles.None, out DateTime d))
            return false;
        serial = SerialDate.FromDate(DateOnly.FromDateTime(d));
        return serial is >= SerialDate.MinSerial and <= SerialDate.MaxSerial;
    }
}
