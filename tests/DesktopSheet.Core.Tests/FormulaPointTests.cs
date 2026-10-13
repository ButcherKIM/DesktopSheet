using DesktopSheet.Core;
using Xunit;

namespace DesktopSheet.Core.Tests;

/// <summary>사양서 10.6. 등호를 치고 칸을 클릭했을 때 식에 들어가는 글자를 본다.</summary>
public class FormulaPointTests
{
    [Theory]
    [InlineData("=", true)]              // 등호만 쳐 둔 자리
    [InlineData("=A1+", true)]
    [InlineData("=SUM(", true)]
    [InlineData("=SUM(A1,", true)]
    [InlineData("=A1:", true)]           // 범위의 뒤쪽
    [InlineData("=A1>", true)]
    [InlineData("=1* ", true)]           // 연산자 뒤 공백은 넘어간다
    [InlineData("=A1", false)]           // 참조 다음에 끼우면 =A1B3 이 된다
    [InlineData("=12", false)]
    [InlineData("=SUM(A1)", false)]
    [InlineData("=5%", false)]
    [InlineData("=\"가", false)]         // 문자열 안은 글자일 뿐이다
    [InlineData("1+2", false)]           // 식이 아니다
    [InlineData("", false)]
    public void 참조를_끼울_자리는_커서_앞_글자가_정한다(string text, bool accepts)
    {
        Assert.Equal(accepts, FormulaPoint.Accepts(text, text.Length));
    }

    [Fact]
    public void 문자열을_닫은_뒤에는_다시_끼울_수_있다()
    {
        Assert.False(FormulaPoint.Accepts("=IF(\"가", 6));
        Assert.True(FormulaPoint.Accepts("=IF(\"가\",", 8));
        Assert.False(FormulaPoint.Accepts("=\"\"\"가", 5));    // 따옴표 두 개는 글자 하나라 아직 문자열 안이다
    }

    [Fact]
    public void 커서_자리에_끼우고_그_자리를_알려_준다()
    {
        (string text, int start, int length) = FormulaPoint.Put("=", 1, -1, 0, "B3");
        Assert.Equal("=B3", text);
        Assert.Equal(1, start);
        Assert.Equal(2, length);
    }

    [Fact]
    public void 찍어_둔_참조는_다음_클릭이_갈아치운다()
    {
        (string text, int start, int length) = FormulaPoint.Put("=", 1, -1, 0, "B3");
        Assert.Equal("=B3", text);
        (text, start, length) = FormulaPoint.Put(text, start + length, start, length, "C7");
        Assert.Equal("=C7", text);

        (text, start, length) = FormulaPoint.Put(text, start + length, start, length, "C7:D9");
        Assert.Equal("=C7:D9", text);
        Assert.Equal(1, start);
        Assert.Equal(5, length);
    }

    [Fact]
    public void 식_가운데에_끼워도_뒤쪽_글자가_남는다()
    {
        (string text, int start, _) = FormulaPoint.Put("=+1", 1, -1, 0, "A2");
        Assert.Equal("=A2+1", text);
        Assert.Equal(1, start);
    }

    [Fact]
    public void 한_칸은_A1_여러_칸은_A1_콜론_B3_으로_적는다()
    {
        Assert.Equal("B3", FormulaPoint.Reference(CellRange.One(new CellAddress(0, 2, 1))));
        Assert.Equal("B3:D5", FormulaPoint.Reference(CellRange.Of(0, 2, 1, 4, 3)));
        Assert.Equal("B3:D5", FormulaPoint.Reference(CellRange.Of(0, 4, 3, 2, 1)));   // 어느 모서리에서 끌어도 같다
    }

    [Fact]
    public void 찍은_참조는_상대_참조라_붙여넣을_때_밀린다()
    {
        string reference = FormulaPoint.Reference(CellRange.One(new CellAddress(0, 0, 0)));
        (string text, _, _) = FormulaPoint.Put("=", 1, -1, 0, reference);
        Assert.Equal("=A2", FormulaRewriter.Shift(text, 1, 0));
    }
}
