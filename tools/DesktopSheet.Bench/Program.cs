// 사양서 16장의 수치를 재는 자리. 배포하는 프로그램에는 들어가지 않는다.
using System.Diagnostics;
using DesktopSheet.Core;

const int Rows = Sheet.Rows, Cols = Sheet.Cols, Cells = Rows * Cols;

static double Best(Func<double> run, int times = 7, int warmup = 2)
{
    var xs = new List<double>();
    for (int i = 0; i < times; i++)
    {
        double ms = run();
        if (i >= warmup) xs.Add(ms);
    }
    xs.Sort();
    return xs[0];
}

// 1. 식 하나를 셈하는 값만 따로 잰다. 그래프를 훑는 값은 빠진다.
var cells = new SimpleSource();
var ev = new Evaluator(cells);
Node node = Evaluator.ParseOrThrow("=A1+1");
double evalMs = Best(() =>
{
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < Cells; i++) ev.Evaluate(node);
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds;
});
Console.WriteLine($"식 {Cells:N0}개 셈하기          {evalMs,6:F2}ms   ({evalMs * 1000 / Cells:F2}us/칸)");

// 2. 사슬 전체 재계산. 그래프를 훑는 값이 더해진다.
var book = new Workbook();
book.SetInput(new CellAddress(0, 0, 0), "1");
for (int i = 1; i < Cells; i++)
{
    (int r, int c) = (i / Cols, i % Cols);
    (int pr, int pc) = ((i - 1) / Cols, (i - 1) % Cols);
    book.SetInput(new CellAddress(0, r, c), $"={(char)('A' + pc)}{pr + 1}+1");
}
int flip = 0;
double recalcMs = Best(() =>
{
    var sw = Stopwatch.StartNew();
    book.SetInput(new CellAddress(0, 0, 0), (++flip % 2 == 0 ? 2 : 1).ToString());
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds;
});
Console.WriteLine($"사슬 {Cells:N0}칸 전체 재계산     {recalcMs,6:F2}ms   ({recalcMs * 1000 / Cells:F2}us/칸)");
Console.WriteLine($"그래프를 훑는 값               {recalcMs - evalMs,6:F2}ms   ({(recalcMs - evalMs) / recalcMs * 100:F0}%)");

sealed class SimpleSource : ICellSource
{
    public Value Read(CellRef r) => Value.Num(1);
}
