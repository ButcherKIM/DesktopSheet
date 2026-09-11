using System;
using DesktopSheet.Core;
using Xunit;

namespace DesktopSheet.Core.Tests;

public class TextWidthTests
{
    [Theory]
    [InlineData("합계금액표",    "합계금액표")]     // 한글 5자 = 10폭, 넘치지 않는다
    [InlineData("합계금액표시",  "합계금액…")]      // 12폭 -> 8 + 2 = 10폭
    [InlineData("ABCDEFGHIJK",  "ABCDEFGH…")]     // 11폭 -> 8 + 2 = 10폭
    [InlineData("A합계금액표",   "A합계금…")]       // 11폭 -> 7 + 2 = 9폭, 한글이 경계에 걸린다
    public void 텍스트는_10폭에_가둔다(string input, string expected)
    {
        Assert.Equal(expected, DisplayFormatter.Text(input));
    }

    [Fact]
    public void 자른_결과는_예산을_넘지_않는다()
    {
        foreach (string s in new[] { "합계금액표시", "ABCDEFGHIJK", "A합계금액표", "가나다라마바사", "abcdefghijklmnop" })
            Assert.True(TextWidth.Of(DisplayFormatter.Text(s)) <= DisplayFormatter.Budget);
    }

    [Fact]
    public void 한글은_두_폭_영문은_한_폭이다()
    {
        Assert.Equal(2, TextWidth.Of("가"));
        Assert.Equal(1, TextWidth.Of("A"));
        Assert.Equal(2, TextWidth.Of("…"));
        Assert.Equal(10, TextWidth.Of("합계금액표"));
    }
}

public class SerialDateTests
{
    [Theory]
    [InlineData(1900, 1, 1, 1)]        // 엑셀의 1번
    [InlineData(1900, 2, 28, 59)]
    [InlineData(1900, 3, 1, 61)]       // 엑셀이 세는 없는 날 1900-02-29 가 60번이다
    [InlineData(2026, 9, 11, 46276)]   // 7장 표의 값
    [InlineData(2026, 9, 30, 46295)]
    public void 엑셀_일련번호와_맞는다(int y, int m, int d, int serial)
    {
        Assert.Equal(serial, SerialDate.FromDate(new DateOnly(y, m, d)));
    }

    [Fact]
    public void 되돌리면_같은_날짜다()
    {
        for (int s = 61; s < SerialDate.MaxSerial; s += 997)
            Assert.Equal(s, SerialDate.FromDate(SerialDate.ToDate(s)));
    }

    [Fact]
    public void 날짜_산술이_일수로_나온다()
    {
        int a = SerialDate.FromDate(new DateOnly(2026, 9, 11));
        int b = SerialDate.FromDate(new DateOnly(2026, 9, 30));
        Assert.Equal(19, b - a);
    }
}
