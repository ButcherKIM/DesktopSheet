using System;
using System.Globalization;
using DesktopSheet.Core;
using Xunit;

namespace DesktopSheet.Core.Tests;

/// <summary>사양서 11장의 검증표를 그대로 옮긴 것이다. 표가 바뀌면 이 시험도 같이 바꾼다.</summary>
public class DisplayRulesTests
{
    [Theory]
    [InlineData("1234.5",        "1234.5")]
    [InlineData("1234.5678901",  "1234.56789")]
    [InlineData("99999999",      "99999999")]
    [InlineData("100000000",     "100000000")]
    [InlineData("9999999999",    "9999999999")]
    [InlineData("10000000000",   "1.0000E+10")]
    [InlineData("9999999999.6",  "1.0000E+10")]
    [InlineData("123456789012",  "1.2346E+11")]
    [InlineData("1e100",         "1.000E+100")]
    [InlineData("-9999999999",   "-9999999999")]
    [InlineData("0.000001",      "0.000001")]
    [InlineData("0.00000001",    "0.00000001")]
    [InlineData("0.000000001",   "1.0000E-09")]
    [InlineData("0",             "0")]
    public void 일반_서식은_11장_표대로_찍는다(string input, string expected)
    {
        double x = double.Parse(input, NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.Equal(expected, DisplayFormatter.General(x));
    }

    [Theory]
    [InlineData("1234.5")]
    [InlineData("1234.5678901")]
    [InlineData("9999999999")]
    [InlineData("10000000000")]
    [InlineData("123456789012")]
    [InlineData("1e100")]
    [InlineData("-9999999999")]
    [InlineData("-1e100")]
    [InlineData("0.000000001")]
    [InlineData("1e-300")]
    [InlineData("1.7976931348623157e308")]
    public void 표시는_부호를_넣어도_11자를_넘지_않는다(string input)
    {
        double x = double.Parse(input, NumberStyles.Float, CultureInfo.InvariantCulture);
        string s = DisplayFormatter.General(x);
        Assert.True(s.Length <= 11, $"{input} -> {s} ({s.Length}자)");
    }

    [Fact]
    public void 부호를_뺀_내용은_10자를_넘지_않는다()
    {
        var rnd = new Random(20260911);
        for (int i = 0; i < 20000; i++)
        {
            // 아주 큰 값부터 아주 작은 값까지 고루 뽑는다.
            double x = (rnd.NextDouble() * 2 - 1) * Math.Pow(10, rnd.Next(-320, 309));
            string s = DisplayFormatter.General(x);
            string body = s.StartsWith('-') ? s[1..] : s;
            Assert.True(body.Length <= DisplayFormatter.Budget, $"{x:R} -> {s} ({body.Length}자)");
        }
    }

    [Fact]
    public void 소수점_이하_0이_여덟_개부터_지수로_간다()
    {
        Assert.Equal("0.00000001", DisplayFormatter.General(1e-8));    // 0 일곱 개
        Assert.Equal("1.0000E-09", DisplayFormatter.General(1e-9));    // 0 여덟 개
    }

    [Fact]
    public void 정수부는_열_자리까지_일반_표기다()
    {
        Assert.Equal("9999999999", DisplayFormatter.General(9999999999d));
        Assert.Equal("1.0000E+10", DisplayFormatter.General(10000000000d));
    }

    [Fact]
    public void 반올림한_결과로_판정한다()
    {
        Assert.Equal("1.0000E+10", DisplayFormatter.General(9999999999.6));
        Assert.Equal("0.00000001", DisplayFormatter.General(9.5e-9));   // 반올림하면 예산에 들어간다
    }

    [Fact]
    public void 지수_표기의_유효숫자는_지수_자릿수에_따라_넷이나_다섯이다()
    {
        Assert.Equal("1.2345E+08", DisplayFormatter.Scientific(1.23454e8));
        Assert.Equal("1.000E+100", DisplayFormatter.Scientific(1e100));
        Assert.Equal(10, DisplayFormatter.Scientific(1e100).Length);
    }

    [Fact]
    public void 표시_반올림은_사사오입이다()
    {
        // 은행가 반올림이면 0.125 가 0.12 가 된다.
        Assert.Equal("0.13", NumberFormat.Apply(0.125, "0.00"));
        Assert.Equal("0.14", NumberFormat.Apply(0.135, "0.00"));
    }

    [Fact]
    public void 더하기_빼기의_찌꺼기는_0으로_맞춘다()
    {
        double step1 = 0.1 + 0.2;
        double step2 = Numeric.SnapNearZero(step1 - 0.3, step1, 0.3);
        Assert.Equal("0", DisplayFormatter.General(step2));

        // 작지만 진짜인 값까지 0으로 만들지는 않는다.
        Assert.Equal(1e-20, Numeric.SnapNearZero(1e-20, 1e-20, 0));
    }
}
