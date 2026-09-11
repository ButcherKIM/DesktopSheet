using System;
using System.Diagnostics;
using DesktopSheet.Core;
using Xunit;

namespace DesktopSheet.Core.Tests;

public class WorkbookTests
{
    private static CellAddress At(string a1, int sheet = 0)
    {
        Assert.True(CellRef.TryParse(a1, out CellRef r));
        return new CellAddress(sheet, r.Row, r.Col);
    }

    private static string Shown(Workbook b, string a1, int sheet = 0)
    {
        Value v = b.Read(At(a1, sheet));
        return v.Kind switch
        {
            ValueKind.Number => DisplayFormatter.General(v.Number),
            ValueKind.Text   => DisplayFormatter.Text(v.Text),
            _                => v.ToString(),
        };
    }

    [Fact]
    public void 값을_바꾸면_달린_칸이_따라_바뀐다()
    {
        var b = new Workbook();
        b.SetInput(At("A1"), "2");
        b.SetInput(At("A2"), "3");
        b.SetInput(At("A3"), "=A1+A2");
        b.SetInput(At("A4"), "=A3*10");
        Assert.Equal("5", Shown(b, "A3"));
        Assert.Equal("50", Shown(b, "A4"));

        b.SetInput(At("A1"), "7");
        Assert.Equal("10", Shown(b, "A3"));
        Assert.Equal("100", Shown(b, "A4"));
    }

    [Fact]
    public void 순환_참조는_CIRC로_찍는다()
    {
        var b = new Workbook();
        b.SetInput(At("A1"), "=B1");
        b.SetInput(At("B1"), "=A1");
        Assert.Equal("#CIRC!", Shown(b, "A1"));
        Assert.Equal("#CIRC!", Shown(b, "B1"));
    }

    [Fact]
    public void 자기를_가리켜도_CIRC다()
    {
        var b = new Workbook();
        b.SetInput(At("A1"), "=A1+1");
        Assert.Equal("#CIRC!", Shown(b, "A1"));
    }

    [Fact]
    public void 순환을_풀면_값이_돌아온다()
    {
        var b = new Workbook();
        b.SetInput(At("A1"), "=B1");
        b.SetInput(At("B1"), "=A1");
        Assert.Equal("#CIRC!", Shown(b, "A1"));

        b.SetInput(At("B1"), "5");
        Assert.Equal("5", Shown(b, "A1"));
        Assert.Equal("5", Shown(b, "B1"));
    }

    [Fact]
    public void 시트_간_참조가_따라_바뀐다()
    {
        var b = new Workbook(2);
        b.SetInput(At("A1", 1), "9");
        b.SetInput(At("A1", 0), "=Sheet2!A1*2");
        Assert.Equal("18", Shown(b, "A1"));

        b.SetInput(At("A1", 1), "10");
        Assert.Equal("20", Shown(b, "A1"));
    }

    [Fact]
    public void 입력_인식이_칸에_그대로_먹는다()
    {
        var b = new Workbook();
        b.SetInput(At("A1"), "2026-09-11");
        Assert.Equal(46276, b.Read(At("A1")).Number);
        b.SetInput(At("A2"), "50%");
        Assert.Equal(0.5, b.Read(At("A2")).Number, 12);
        b.SetInput(At("A3"), "1,234");
        Assert.Equal(ValueKind.Text, b.Read(At("A3")).Kind);
        b.SetInput(At("A4"), "1,234", fromPaste: true);
        Assert.Equal(1234, b.Read(At("A4")).Number);
    }

    [Fact]
    public void Delete는_값만_지우고_서식은_남긴다()
    {
        var b = new Workbook();
        b.SetInput(At("A1"), "5");
        Cell cell = b.Sheets[0].GetOrCreate(0, 0);
        cell.Shade = "노랑";
        b.ClearValue(At("A1"));
        Assert.Equal(ValueKind.Blank, b.Read(At("A1")).Kind);
        Assert.Equal("노랑", b.Sheets[0].Find(0, 0)!.Shade);
    }

    [Fact]
    public void 시트는_다섯_장이_상한이다()
    {
        var b = new Workbook(5);
        Assert.Equal(5, b.Sheets.Count);
        Assert.Equal("Sheet1", b.Sheets[0].Name);
        Assert.Equal("Sheet5", b.Sheets[4].Name);
        Assert.Throws<InvalidOperationException>(() => b.AddSheet());
    }

    [Fact]
    public void 시트_한_장을_가득_채우고_다시_셈해도_10ms_안에_끝난다()
    {
        // 16장의 목표: 시트 한 장(5,200칸) 전체 재계산 10ms.
        // A1 에서 시작해 Z200 까지 한 줄로 이어지는 사슬을 만든다. 마지막 칸까지 A1 에 달려 있다.
        var b = new Workbook();
        b.SetInput(At("A1"), "1");
        for (int i = 1; i < Sheet.Rows * Sheet.Cols; i++)
        {
            (int r, int c) = (i / Sheet.Cols, i % Sheet.Cols);
            (int pr, int pc) = ((i - 1) / Sheet.Cols, (i - 1) % Sheet.Cols);
            b.SetInput(new CellAddress(0, r, c), $"={(char)('A' + pc)}{pr + 1}+1");
        }
        Assert.Equal("5200", Shown(b, "Z200"));

        // 한 번만 재면 편차가 커서 여러 번 재고 가장 빠른 값을 쓴다.
        var times = new System.Collections.Generic.List<double>();
        for (int i = 0; i < 7; i++)
        {
            var sw = Stopwatch.StartNew();
            b.SetInput(At("A1"), (i % 2 == 0 ? 2 : 1).ToString());
            sw.Stop();
            if (i >= 2) times.Add(sw.Elapsed.TotalMilliseconds);   // 앞의 둘은 몸풀기
        }
        times.Sort();

        Assert.Equal("2", Shown(b, "A1"));
        Assert.Equal("5201", Shown(b, "Z200"));   // 2 + 5,199 칸
        Assert.True(times[0] < 10,
            $"전체 재계산이 가장 빠를 때 {times[0]:F1}ms, 가운뎃값 {times[times.Count / 2]:F1}ms 걸렸습니다 (목표 10ms)");
    }
}
