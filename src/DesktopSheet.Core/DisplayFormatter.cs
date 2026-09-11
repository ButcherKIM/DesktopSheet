using System;
using System.Globalization;

namespace DesktopSheet.Core;

/// <summary>
/// 사양서 1~4장과 6장. 무엇을 그리든 표시 예산 10자 안에서 끝낸다.
/// 넘치면 줄이거나(반올림) 바꾸거나(지수) 덮거나(#) 자른다(…).
/// </summary>
public static class DisplayFormatter
{
    /// <summary>1장. 부호를 뺀 내용이 쓰는 자릿수.</summary>
    public const int Budget = 10;

    /// <summary>4장. 지정 서식이 예산을 넘겼을 때 덮는 문자열.</summary>
    public const string Overflow = "##########";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>2장. 반올림을 마친 문자 수가 10자를 넘으면 지수로 바꾼다.</summary>
    public static string General(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return CellErrorText.Of(CellError.Number);
        if (x == 0) return "0";

        string sign = x < 0 ? "-" : "";
        double a = Math.Abs(x);

        if (a >= 1)
        {
            // 정수부가 이미 11자리 이상이면 반올림해도 줄지 않는다.
            if (a >= 1e10) return Scientific(x);

            decimal d = (decimal)a;
            int intDigits = IntDigits(d);
            int p = intDigits < Budget ? Budget - intDigits - 1 : 0;   // 소수점 한 자리를 뺀 나머지
            decimal r = Math.Round(d, p, MidpointRounding.AwayFromZero);

            if (IntDigits(r) > intDigits)          // 9999999999.6 처럼 반올림으로 자릿수가 늘면 다시 판정
            {
                intDigits = IntDigits(r);
                if (intDigits > Budget) return Scientific(x);
                p = intDigits < Budget ? Budget - intDigits - 1 : 0;
                r = Math.Round(d, p, MidpointRounding.AwayFromZero);
            }

            string s = r.ToString("F" + p, Inv);
            if (p > 0) s = s.TrimEnd('0').TrimEnd('.');
            return sign + s;
        }
        else
        {
            const int p = Budget - 2;              // "0." 두 자리를 뺀 여덟 자리
            decimal r = Math.Round((decimal)a, p, MidpointRounding.AwayFromZero);
            if (r == 0) return Scientific(x);      // 0.000000001 처럼 예산 안에서 0 이 되면 지수로
            if (r >= 1) return General(x < 0 ? -(double)r : (double)r);   // 0.999999996 이 1 로 올라간 경우
            string s = r.ToString("F" + p, Inv).TrimEnd('0').TrimEnd('.');
            return sign + s;
        }
    }

    /// <summary>3장. 엑셀 표기법 0.0000E+00 으로 10자를 채운다. 유효숫자는 지수 자릿수에 따라 4~5자리다.</summary>
    public static string Scientific(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return CellErrorText.Of(CellError.Number);
        if (x == 0) return "0";

        string sign = x < 0 ? "-" : "";
        double a = Math.Abs(x);

        int e = (int)Math.Floor(Math.Log10(a));
        double m = a / Math.Pow(10, e);
        if (m >= 10) { m /= 10; e++; }             // log10 의 오차로 한 칸 어긋난 것을 되짚는다
        else if (m < 1) { m *= 10; e--; }

        int ed = ExponentDigits(e);
        int p = 6 - ed;                            // 10자 = 정수 1 + 점 1 + 소수 p + E 1 + 지수 부호 1 + 지수 ed
        decimal md = Math.Round((decimal)m, p, MidpointRounding.AwayFromZero);
        if (md >= 10)                              // 9.99999 가 10 으로 올라가면 지수를 하나 올린다
        {
            md /= 10; e++;
            ed = ExponentDigits(e);
            p = 6 - ed;
            md = Math.Round(md, p, MidpointRounding.AwayFromZero);
        }

        return sign + md.ToString("F" + p, Inv)
             + "E" + (e < 0 ? "-" : "+")
             + Math.Abs(e).ToString("D" + ed, Inv);
    }

    /// <summary>4장. 지정한 서식대로 쓰되 예산을 넘기면 지수로 바꾸지 않고 #으로 덮는다.</summary>
    public static string WithFormat(double x, string formatCode)
    {
        if (NumberFormat.IsGeneral(formatCode)) return General(x);
        string s = NumberFormat.Apply(x, formatCode);
        return TextWidth.Of(s) > Budget ? Overflow : s;
    }

    /// <summary>6장. 텍스트는 셀 안에 가두고 넘치면 자른다.</summary>
    public static string Text(string s) => TextWidth.Clip(s, Budget);

    private static int ExponentDigits(int e) =>
        Math.Max(2, Math.Abs(e).ToString(Inv).Length);

    private static int IntDigits(decimal d)
    {
        d = Math.Truncate(Math.Abs(d));
        if (d < 1) return 1;                       // 0.5 의 정수부는 "0" 한 자
        int n = 0;
        while (d >= 1) { d = Math.Truncate(d / 10); n++; }
        return n;
    }
}
