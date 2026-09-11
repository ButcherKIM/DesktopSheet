using System;
using System.Collections.Generic;
using DesktopSheet.Core;
using Xunit;

namespace DesktopSheet.Core.Tests;

/// <summary>시험에 쓰는 칸 창고. A1 같은 주소에 값을 미리 넣어 둔다.</summary>
internal sealed class FakeCells : ICellSource
{
    private readonly Dictionary<string, Value> _cells = new(StringComparer.OrdinalIgnoreCase);

    public FakeCells Set(string a1, Value v) { _cells[Key(a1)] = v; return this; }
    public FakeCells Set(string a1, double v) => Set(a1, Value.Num(v));
    public FakeCells Set(string a1, string v) => Set(a1, Value.Str(v));

    public Value Read(CellRef r)
    {
        if (!r.IsValid) return Value.Err(CellError.Reference);
        if (r.SheetName is not null and not "Sheet1") return Value.Err(CellError.Reference);
        return _cells.TryGetValue($"{r.Col}:{r.Row}", out Value v) ? v : Value.Blank;
    }

    private static string Key(string a1)
    {
        CellRef.TryParse(a1, out CellRef r);
        return $"{r.Col}:{r.Row}";
    }
}

public class FormulaTests
{
    private static Value Eval(string formula, FakeCells? cells = null) =>
        new Evaluator(cells ?? new FakeCells()).Evaluate(formula);

    private static double Num(string formula, FakeCells? cells = null)
    {
        Value v = Eval(formula, cells);
        Assert.Equal(ValueKind.Number, v.Kind);
        return v.Number;
    }

    [Theory]
    [InlineData("=-2^2", 4)]        // 단항 마이너스가 제곱보다 위다
    [InlineData("=2^3^2", 64)]      // 제곱은 왼쪽부터 묶는다
    [InlineData("=2+3*4", 14)]
    [InlineData("=(2+3)*4", 20)]
    [InlineData("=10*10%", 1)]
    [InlineData("=2^-2", 0.25)]
    [InlineData("=-3^2+1", 10)]
    [InlineData("=100/4/5", 5)]
    public void 우선순위는_8_1_표대로다(string formula, double expected)
    {
        Assert.Equal(expected, Num(formula), 12);
    }

    [Theory]
    [InlineData("=LOG(100)", 2)]        // 밑의 기본은 10
    [InlineData("=LOG(8,2)", 3)]
    [InlineData("=LOG10(1000)", 3)]
    [InlineData("=LN(EXP(1))", 1)]
    [InlineData("=SQRT(16)", 4)]
    [InlineData("=POWER(2,10)", 1024)]
    [InlineData("=ABS(-3)", 3)]
    [InlineData("=INT(-1.5)", -2)]
    [InlineData("=MOD(-3,2)", 1)]       // 나누는 수의 부호를 따른다
    [InlineData("=ROUND(0.125,2)", 0.13)]
    [InlineData("=ROUNDUP(1.01,1)", 1.1)]
    [InlineData("=ROUNDDOWN(1.09,1)", 1)]
    public void 함수가_엑셀과_같은_값을_낸다(string formula, double expected)
    {
        Assert.Equal(expected, Num(formula), 10);
    }

    [Fact]
    public void 집계_함수는_범위_안의_텍스트와_빈_칸을_무시한다()
    {
        var cells = new FakeCells().Set("A1", 10).Set("A2", "합계").Set("A4", 20);
        Assert.Equal(30, Num("=SUM(A1:A4)", cells));
        Assert.Equal(15, Num("=AVERAGE(A1:A4)", cells));   // 빈 칸을 0 으로 세지 않고 빼고 나눈다
        Assert.Equal(2, Num("=COUNT(A1:A4)", cells));
        Assert.Equal(10, Num("=MIN(A1:A4)", cells));
        Assert.Equal(20, Num("=MAX(A1:A4)", cells));
    }

    [Fact]
    public void 인수로_직접_준_텍스트는_오류다()
    {
        Assert.Equal(CellError.Value, Eval("=SUM(\"합계\",1)").Error);
    }

    [Fact]
    public void 빈_칸은_0이다()
    {
        Assert.Equal(1, Num("=A1+1"));
    }

    [Theory]
    [InlineData("=1/0",        CellError.DivideByZero)]
    [InlineData("=\"가\"+1",   CellError.Value)]
    [InlineData("=SQRT(-1)",   CellError.Number)]
    [InlineData("=LOG(0)",     CellError.Number)]
    [InlineData("=LOG(8,1)",   CellError.Number)]
    [InlineData("=NOSUCH(1)",  CellError.Name)]
    [InlineData("=A1:B2",      CellError.Value)]     // 8.3: 범위는 함수 인수 자리에서만
    [InlineData("=AVERAGE(A1:A3)", CellError.DivideByZero)]
    public void 오류는_엑셀_이름으로_난다(string formula, CellError expected)
    {
        Value v = Eval(formula);
        Assert.True(v.IsError, $"{formula} 가 오류가 아니라 {v}");
        Assert.Equal(expected, v.Error);
    }

    [Fact]
    public void 시트_밖을_가리키면_참조_오류다()
    {
        Assert.Equal(CellError.Reference, Eval("=A999").Error);
        Assert.Equal(CellError.Reference, Eval("='없는 시트'!A1").Error);
    }

    [Fact]
    public void 조건_함수는_고른_가지만_셈한다()
    {
        var cells = new FakeCells().Set("A1", 10);
        Assert.Equal(1, Num("=IF(A1>5,1,2)", cells));
        Assert.Equal(2, Num("=IF(A1>50,1,2)", cells));
        // 고르지 않은 가지에 오류가 있어도 번지지 않는다
        Assert.Equal(1, Num("=IF(A1>5,1,1/0)", cells));
        // 세 번째를 생략하면 FALSE 다
        Value v = Eval("=IF(A1>50,1)", cells);
        Assert.Equal(ValueKind.Bool, v.Kind);
        Assert.Equal(0, v.Number);
    }

    [Fact]
    public void 비교는_참거짓을_낸다()
    {
        var cells = new FakeCells().Set("A1", 10).Set("B1", "가");
        Assert.Equal(ValueKind.Bool, Eval("=A1>5", cells).Kind);
        Assert.Equal("TRUE",  Eval("=A1>5", cells).ToString());
        Assert.Equal("FALSE", Eval("=A1>B1", cells).ToString());   // 숫자가 텍스트보다 작다
        Assert.Equal("TRUE",  Eval("=A1<B1", cells).ToString());
        Assert.Equal("TRUE",  Eval("=A1<>5", cells).ToString());
    }

    [Fact]
    public void 참거짓은_계산에서_1과_0이다()
    {
        var cells = new FakeCells().Set("A1", 10);
        Assert.Equal(1, Num("=(A1>5)*1", cells));
        Assert.Equal(0, Num("=(A1>50)*1", cells));
    }

    [Fact]
    public void 더하기_빼기의_찌꺼기를_0으로_맞춘다()
    {
        Assert.Equal(0, Num("=0.1+0.2-0.3"));
        Assert.Equal("0", DisplayFormatter.General(Num("=0.1+0.2-0.3")));
    }

    [Fact]
    public void 날짜_함수가_일련번호를_낸다()
    {
        Assert.Equal(46276, Num("=DATE(2026,9,11)"));
        Assert.Equal(19, Num("=DATE(2026,9,30)-DATE(2026,9,11)"));
        Assert.Equal(SerialDate.FromDate(DateOnly.FromDateTime(DateTime.Now)), Num("=TODAY()"));
    }

    [Fact]
    public void 시트_간_참조를_읽는다()
    {
        var cells = new FakeCells().Set("A1", 7);
        Assert.Equal(7, Num("=Sheet1!A1", cells));
    }

    [Fact]
    public void 대소문자와_공백을_가리지_않는다()
    {
        var cells = new FakeCells().Set("A1", 2).Set("B2", 3);
        Assert.Equal(5, Num("= sum( a1 : b2 )", cells));
    }
}

public class CellRefTests
{
    [Theory]
    [InlineData("A1", 0, 0, false, false)]
    [InlineData("$A$1", 0, 0, true, true)]
    [InlineData("$A1", 0, 0, false, true)]
    [InlineData("A$1", 0, 0, true, false)]
    [InlineData("Z200", 199, 25, false, false)]
    public void 주소를_읽는다(string text, int row, int col, bool rowAbs, bool colAbs)
    {
        Assert.True(CellRef.TryParse(text, out CellRef r));
        Assert.Equal((row, col, rowAbs, colAbs), (r.Row, r.Col, r.RowAbs, r.ColAbs));
    }

    [Fact]
    public void 시트_이름을_읽는다()
    {
        Assert.True(CellRef.TryParse("Sheet2!A1", out CellRef a));
        Assert.Equal("Sheet2", a.SheetName);
        Assert.True(CellRef.TryParse("'내 시트'!A1", out CellRef b));
        Assert.Equal("내 시트", b.SheetName);
        Assert.Equal("'내 시트'!A1", b.ToA1());
    }

    [Fact]
    public void 붙여넣을_때_상대_참조만_민다()
    {
        CellRef.TryParse("A1", out CellRef rel);
        Assert.Equal("A2", rel.Offset(1, 0).ToA1());
        CellRef.TryParse("$A$1", out CellRef abs);
        Assert.Equal("$A$1", abs.Offset(1, 0).ToA1());
        CellRef.TryParse("$A1", out CellRef mix);
        Assert.Equal("$A2", mix.Offset(1, 0).ToA1());
    }

    [Fact]
    public void 시트_밖은_잘못된_주소다()
    {
        CellRef.TryParse("A1", out CellRef r);
        Assert.False(r.Offset(-1, 0).IsValid);
        Assert.False(r.Offset(200, 0).IsValid);
        Assert.True(r.Offset(199, 25).IsValid);
    }
}
