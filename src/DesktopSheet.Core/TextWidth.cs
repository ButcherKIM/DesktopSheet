namespace DesktopSheet.Core;

/// <summary>
/// 사양서 1장과 6장의 폭 계산. 길이를 글자 수가 아니라 고정폭 글꼴에서 차지하는 자리로 센다.
/// 한글 한 자가 2폭, 영문 한 자가 1폭, 말줄임표가 2폭이다.
/// </summary>
public static class TextWidth
{
    /// <summary>잘림 표시. 1장의 실측 가정에 따라 2폭으로 본다.</summary>
    public const char Ellipsis = '…';
    public const int EllipsisWidth = 2;

    public static int Of(char c) => c switch
    {
        Ellipsis => EllipsisWidth,
        >= 'ᄀ' and <= 'ᅟ' => 2,   // 한글 자모
        >= '⺀' and <= '꓏' => 2,   // CJK 부수부터 이(Yi)까지
        >= '가' and <= '힣' => 2,   // 한글 음절
        >= '豈' and <= '﫿' => 2,   // CJK 호환 한자
        >= '︰' and <= '﹯' => 2,   // CJK 호환 기호
        >= '＀' and <= '｠' => 2,   // 전각 영숫자
        >= '￠' and <= '￦' => 2,   // 전각 기호
        _ => 1,
    };

    public static int Of(string s)
    {
        int w = 0;
        foreach (char c in s) w += Of(c);
        return w;
    }

    /// <summary>
    /// 사양서 6장. budget 폭을 넘으면 앞에서부터 (budget - 잘림 표시 폭) 까지만 남기고 말줄임표를 붙인다.
    /// 경계가 한글 한 자의 가운데에 걸리면 그 글자를 통째로 버리므로 결과가 budget 보다 한 폭 좁아지기도 한다.
    /// </summary>
    public static string Clip(string s, int budget)
    {
        if (Of(s) <= budget) return s;
        int keep = budget - EllipsisWidth;
        int w = 0, i = 0;
        for (; i < s.Length; i++)
        {
            int cw = Of(s[i]);
            if (w + cw > keep) break;
            w += cw;
        }
        return s[..i] + Ellipsis;
    }
}
