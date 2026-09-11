using DesktopSheet.Core;
using Xunit;

namespace DesktopSheet.Core.Tests;

public class NumberFormatTests
{
    [Theory]
    [InlineData(99999999d,  "#,##0",      "99,999,999")]
    [InlineData(0.5,        "0.00%",      "50.00%")]
    [InlineData(1234.5,     "0.00",       "1234.50")]
    [InlineData(1234.5,     "0",          "1235")]
    [InlineData(1.23454e8,  "0.0000E+00", "1.2345E+08")]
    public void 지정_서식은_그대로_찍는다(double v, string code, string expected)
    {
        Assert.Equal(expected, DisplayFormatter.WithFormat(v, code));
    }

    [Fact]
    public void 예산을_넘기면_지수가_아니라_샵으로_덮는다()
    {
        Assert.Equal("##########", DisplayFormatter.WithFormat(999999999d, "#,##0"));
        Assert.Equal(10, DisplayFormatter.Overflow.Length);
    }

    [Fact]
    public void 서식을_지정한_칸에서는_자동_지수_전환이_없다()
    {
        // 일반이면 지수로 가지만 서식이 있으면 # 으로 덮는다.
        Assert.Equal("1.0000E+10", DisplayFormatter.General(1e10));
        Assert.Equal("##########", DisplayFormatter.WithFormat(1e10, "#,##0"));
    }

    [Fact]
    public void 날짜_서식은_일련번호를_날짜로_푼다()
    {
        Assert.Equal("2026-09-11", DisplayFormatter.WithFormat(46276, "yyyy-mm-dd"));
        Assert.Equal(10, DisplayFormatter.WithFormat(46276, "yyyy-mm-dd").Length);
    }

    [Fact]
    public void 세미콜론_세_구역이_양수_음수_0을_가른다()
    {
        const string code = "0.00;[Red]-0.00;-";
        Assert.Equal("1.50",  NumberFormat.Apply(1.5, code));
        Assert.Equal("-1.50", NumberFormat.Apply(-1.5, code));
        Assert.Equal("-",     NumberFormat.Apply(0, code));
    }

    [Fact]
    public void 색_지정은_글자색_이름으로_돌아온다()
    {
        var r = NumberFormat.ApplyWithColor(-1.5, "0.00;[Red]-0.00");
        Assert.Equal("-1.50", r.Text);
        Assert.Equal("빨강", r.Color);
        Assert.Null(NumberFormat.ApplyWithColor(1.5, "0.00").Color);
    }

    [Fact]
    public void 일반_서식은_빈_코드로_나타낸다()
    {
        Assert.True(NumberFormat.IsGeneral(""));
        Assert.True(NumberFormat.IsGeneral("일반"));
        Assert.False(NumberFormat.IsGeneral("#,##0"));
    }
}

public class InputParserTests
{
    [Theory]
    [InlineData("1234.5",     1234.5,  "")]
    [InlineData("1.2e5",      120000,  "")]
    [InlineData("-5",         -5,      "")]
    [InlineData("50%",        0.5,     "0%")]
    [InlineData("12.50%",     0.125,   "0.00%")]
    public void 숫자와_퍼센트와_지수를_알아듣는다(string raw, double value, string code)
    {
        var p = InputParser.Parse(raw);
        Assert.Equal(InputKind.Number, p.Kind);
        Assert.Equal(value, p.Value, 12);
        Assert.Equal(code, p.FormatCode);
    }

    [Theory]
    [InlineData("2026-09-11")]
    [InlineData("2026.9.11")]
    [InlineData("26-9-11")]
    public void 연도를_적은_날짜_셋을_알아듣는다(string raw)
    {
        var p = InputParser.Parse(raw);
        Assert.Equal(InputKind.Number, p.Kind);
        Assert.Equal(46276, p.Value);
        Assert.Equal("yyyy-mm-dd", p.FormatCode);
    }

    [Theory]
    [InlineData("9/11")]          // 연도가 없으면 날짜가 아니다
    [InlineData("합계")]
    [InlineData("1,234")]         // 직접 칠 때 콤마는 텍스트다
    [InlineData("2026년 9월 11일")]
    public void 나머지는_텍스트다(string raw)
    {
        Assert.Equal(InputKind.Text, InputParser.Parse(raw).Kind);
    }

    [Fact]
    public void 붙여넣기에_한해_콤마가_든_숫자를_숫자로_읽는다()
    {
        var typed = InputParser.Parse("1,234");
        Assert.Equal(InputKind.Text, typed.Kind);

        var pasted = InputParser.Parse("1,234", fromPaste: true);
        Assert.Equal(InputKind.Number, pasted.Kind);
        Assert.Equal(1234, pasted.Value);
        Assert.Equal("#,##0", pasted.FormatCode);
    }

    [Fact]
    public void 등호로_시작하면_수식이다()
    {
        var p = InputParser.Parse("=A1+B1");
        Assert.Equal(InputKind.Formula, p.Kind);
        Assert.Equal("=A1+B1", p.Text);
    }
}
