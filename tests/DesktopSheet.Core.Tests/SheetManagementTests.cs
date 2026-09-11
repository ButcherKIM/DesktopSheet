using System;
using DesktopSheet.Core;
using Xunit;

namespace DesktopSheet.Core.Tests;

public class FormulaRewriterTests
{
    [Theory]
    [InlineData("=A1+B1", 1, 0, "=A2+B2")]
    [InlineData("=$A$1+B1", 1, 0, "=$A$1+B2")]
    [InlineData("=$A1+A$1", 1, 1, "=$A2+B$1")]
    [InlineData("=SUM(A1:A3)*2", 0, 1, "=SUM(B1:B3)*2")]
    [InlineData("=A1", -1, 0, "=#REF!")]
    public void 붙여넣을_때_상대_참조만_민다(string formula, int dr, int dc, string expected)
    {
        Assert.Equal(expected, FormulaRewriter.Shift(formula, dr, dc));
    }

    [Fact]
    public void 수식이_아니면_건드리지_않는다()
    {
        Assert.Equal("합계", FormulaRewriter.Shift("합계", 1, 0));
    }

    [Fact]
    public void 큰따옴표_안의_글자는_참조로_보지_않는다()
    {
        Assert.Equal("=IF(A2>5,\"A1\",\"\")", FormulaRewriter.Shift("=IF(A1>5,\"A1\",\"\")", 1, 0));
    }

    [Theory]
    [InlineData("=Sheet2!A1*2", "Sheet2", "매출", "=매출!A1*2")]
    [InlineData("='내 시트'!A1", "내 시트", "비용", "=비용!A1")]
    [InlineData("=Sheet3!A1", "Sheet2", "매출", "=Sheet3!A1")]
    [InlineData("=A1+Sheet2!B2", "Sheet2", "내 시트", "=A1+'내 시트'!B2")]
    public void 시트_이름을_바꾸면_식_안의_이름도_바뀐다(string formula, string oldName, string newName, string expected)
    {
        Assert.Equal(expected, FormulaRewriter.RenameSheet(formula, oldName, newName));
    }
}

public class SheetManagementTests
{
    private static CellAddress At(int sheet, int row, int col) => new(sheet, row, col);

    [Fact]
    public void 이름을_바꾸면_그_이름을_쓰던_식이_계속_돈다()
    {
        var book = new Workbook(2);
        book.SetInput(At(1, 0, 0), "10");
        book.SetInput(At(0, 0, 0), "=Sheet2!A1*2");
        Assert.Equal(20, book.Read(At(0, 0, 0)).Number);

        book.RenameSheet(1, "매출");
        Assert.Equal("=매출!A1*2", book.FindCell(At(0, 0, 0))!.Raw);
        Assert.Equal(20, book.Read(At(0, 0, 0)).Number);

        book.SetInput(At(1, 0, 0), "30");
        Assert.Equal(60, book.Read(At(0, 0, 0)).Number);   // 그래프도 살아 있다
    }

    [Fact]
    public void 공백이_든_이름은_식에서_작은따옴표로_묶인다()
    {
        var book = new Workbook(2);
        book.SetInput(At(1, 0, 0), "7");
        book.SetInput(At(0, 0, 0), "=Sheet2!A1");
        book.RenameSheet(1, "내 시트");
        Assert.Equal("='내 시트'!A1", book.FindCell(At(0, 0, 0))!.Raw);
        Assert.Equal(7, book.Read(At(0, 0, 0)).Number);
    }

    [Fact]
    public void 같은_이름이나_빈_이름은_거절한다()
    {
        var book = new Workbook(2);
        Assert.Throws<InvalidOperationException>(() => book.RenameSheet(1, "Sheet1"));
        Assert.Throws<ArgumentException>(() => book.RenameSheet(1, "  "));
    }

    [Fact]
    public void 시트를_지우면_그것을_가리키던_식이_REF가_된다()
    {
        var book = new Workbook(2);
        book.SetInput(At(1, 0, 0), "10");
        book.SetInput(At(0, 0, 0), "=Sheet2!A1*2");
        Assert.Equal(20, book.Read(At(0, 0, 0)).Number);

        book.RemoveSheet(1);
        Assert.Single(book.Sheets);
        Assert.Equal(CellError.Reference, book.Read(At(0, 0, 0)).Error);
    }

    [Fact]
    public void 시트를_지운_뒤에도_남은_시트의_그래프가_어긋나지_않는다()
    {
        // 칸 번호가 시트 번호를 품고 있어, 앞 시트가 빠지면 번호가 통째로 밀린다.
        var book = new Workbook(3);
        book.SetInput(At(2, 0, 0), "2");
        book.SetInput(At(2, 0, 1), "=A1*10");
        book.SetInput(At(2, 0, 2), "=B1+1");
        Assert.Equal(21, book.Read(At(2, 0, 2)).Number);

        book.RemoveSheet(0);                       // 세 번째 시트가 두 번째가 된다
        Assert.Equal(2, book.Sheets.Count);
        Assert.Equal(21, book.Read(At(1, 0, 2)).Number);

        book.SetInput(At(1, 0, 0), "3");           // 밀린 자리에서도 달린 칸이 따라 바뀐다
        Assert.Equal(30, book.Read(At(1, 0, 1)).Number);
        Assert.Equal(31, book.Read(At(1, 0, 2)).Number);
    }

    [Fact]
    public void 지운_이름으로_시트를_다시_만들면_식이_되살아난다()
    {
        var book = new Workbook(2);
        book.SetInput(At(1, 0, 0), "10");
        book.SetInput(At(0, 0, 0), "=Sheet2!A1*2");
        book.RemoveSheet(1);
        Assert.Equal(CellError.Reference, book.Read(At(0, 0, 0)).Error);

        book.AddSheet("Sheet2");
        book.SetInput(At(1, 0, 0), "5");
        Assert.Equal(10, book.Read(At(0, 0, 0)).Number);
    }

    [Fact]
    public void 마지막_한_장은_지우지_못한다()
    {
        var book = new Workbook();
        Assert.Throws<InvalidOperationException>(() => book.RemoveSheet(0));
    }

    [Fact]
    public void 이름을_바꿔도_서식과_색은_그대로다()
    {
        var book = new Workbook(2);
        book.SetInput(At(0, 0, 0), "1");
        book.SetShade(At(0, 0, 0), "노랑");
        book.SetFormat(At(0, 0, 0), "#,##0");

        book.RenameSheet(1, "매출");
        Cell cell = book.FindCell(At(0, 0, 0))!;
        Assert.Equal("노랑", cell.Shade);
        Assert.Equal("#,##0", cell.FormatCode);
    }
}
