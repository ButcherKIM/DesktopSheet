using System;
using System.IO;
using System.Linq;
using DesktopSheet.Core;
using Xunit;

namespace DesktopSheet.Core.Tests;

public class SelectionTests
{
    [Fact]
    public void 클릭은_한_칸_시프트클릭은_영역이다()
    {
        var s = new Selection();
        s.MoveTo(2, 3);
        Assert.True(s.Range.IsSingle);

        s.ExtendTo(5, 6);
        Assert.Equal((2, 3, 5, 6), (s.Range.Top, s.Range.Left, s.Range.Bottom, s.Range.Right));
        Assert.Equal(16, s.Range.Count);
        Assert.Equal(new CellAddress(0, 2, 3), s.Cursor);   // 커서는 그대로다
    }

    [Fact]
    public void 위로_끌어도_좌상단과_우하단으로_맞춘다()
    {
        var s = new Selection();
        s.MoveTo(5, 6);
        s.ExtendTo(2, 3);
        Assert.Equal((2, 3, 5, 6), (s.Range.Top, s.Range.Left, s.Range.Bottom, s.Range.Right));
    }

    [Fact]
    public void 시트_밖으로는_나가지_않는다()
    {
        var s = new Selection();
        s.MoveTo(0, 0);
        s.Move(MoveDirection.Up);
        s.Move(MoveDirection.Left);
        Assert.Equal(new CellAddress(0, 0, 0), s.Cursor);

        s.MoveTo(Sheet.Rows - 1, Sheet.Cols - 1);
        s.Move(MoveDirection.Down);
        s.Move(MoveDirection.Right);
        Assert.Equal(new CellAddress(0, Sheet.Rows - 1, Sheet.Cols - 1), s.Cursor);
    }

    [Fact]
    public void 머리글_클릭은_행과_열_전체다()
    {
        var s = new Selection();
        s.SelectRow(3);
        Assert.Equal(Sheet.Cols, s.Range.Count);
        s.SelectColumn(4);
        Assert.Equal(Sheet.Rows, s.Range.Count);
        s.SelectAll();
        Assert.Equal(Sheet.Rows * Sheet.Cols, s.Range.Count);
    }

    [Fact]
    public void 영역을_골라_두면_Tab이_그_안에서만_돈다()
    {
        var s = new Selection();
        s.MoveTo(1, 1);
        s.ExtendTo(2, 2);          // 2x2

        s.Advance(MoveDirection.Right);
        Assert.Equal(new CellAddress(0, 1, 2), s.Cursor);
        s.Advance(MoveDirection.Right);
        Assert.Equal(new CellAddress(0, 2, 1), s.Cursor);   // 줄 끝에서 다음 줄 첫 칸으로
        s.Advance(MoveDirection.Right);
        Assert.Equal(new CellAddress(0, 2, 2), s.Cursor);
        s.Advance(MoveDirection.Right);
        Assert.Equal(new CellAddress(0, 1, 1), s.Cursor);   // 끝에서 처음으로 돌아온다
        Assert.Equal(4, s.Range.Count);                      // 영역은 그대로다
    }

    [Fact]
    public void 한_칸만_골랐으면_Enter가_그냥_아래로_간다()
    {
        var s = new Selection();
        s.MoveTo(1, 1);
        s.Advance(MoveDirection.Down);
        Assert.Equal(new CellAddress(0, 2, 1), s.Cursor);
    }

    [Fact]
    public void 컨트롤_방향키는_값이_끊기는_데까지_건너뛴다()
    {
        var book = new Workbook();
        for (int r = 0; r < 5; r++) book.SetInput(new CellAddress(0, r, 0), "1");

        var s = new Selection();
        s.MoveTo(0, 0);
        s.Jump(MoveDirection.Down, book);
        Assert.Equal(4, s.Cursor.Row);          // 값이 든 마지막 칸

        s.Jump(MoveDirection.Down, book);
        Assert.Equal(Sheet.Rows - 1, s.Cursor.Row);   // 값이 끊긴 뒤로는 시트 끝까지
    }
}

public class UndoStackTests
{
    private static CellAddress A(int row, int col = 0) => new(0, row, col);

    [Fact]
    public void 되돌리고_다시_실행한다()
    {
        var book = new Workbook();
        var undo = new UndoStack();
        book.SetInput(A(0), "1");

        UndoStep step = UndoStep.Begin(book, new[] { A(0) });
        book.SetInput(A(0), "2");
        undo.Push(step.Commit(book));

        Assert.True(undo.Undo(book));
        Assert.Equal(1, book.Read(A(0)).Number);
        Assert.True(undo.Redo(book));
        Assert.Equal(2, book.Read(A(0)).Number);
    }

    [Fact]
    public void 스무_단계까지만_기억한다()
    {
        var book = new Workbook();
        var undo = new UndoStack();
        for (int i = 0; i < 30; i++)
        {
            UndoStep step = UndoStep.Begin(book, new[] { A(0) });
            book.SetInput(A(0), i.ToString());
            undo.Push(step.Commit(book));
        }
        Assert.Equal(UndoStack.MaxSteps, undo.Count);

        int undone = 0;
        while (undo.Undo(book)) undone++;
        Assert.Equal(UndoStack.MaxSteps, undone);
        Assert.Equal(9, book.Read(A(0)).Number);   // 20단계 앞은 9 를 넣기 직전이 아니라 9 다
    }

    [Fact]
    public void 되돌린_뒤_새로_고치면_앞의_것은_버린다()
    {
        var book = new Workbook();
        var undo = new UndoStack();
        foreach (string v in new[] { "1", "2", "3" })
        {
            UndoStep step = UndoStep.Begin(book, new[] { A(0) });
            book.SetInput(A(0), v);
            undo.Push(step.Commit(book));
        }
        undo.Undo(book);
        Assert.True(undo.CanRedo);

        UndoStep fresh = UndoStep.Begin(book, new[] { A(0) });
        book.SetInput(A(0), "9");
        undo.Push(fresh.Commit(book));
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void 서식도_함께_되돌린다()
    {
        var book = new Workbook();
        book.SetShade(A(0), "노랑");

        UndoStep step = UndoStep.Begin(book, new[] { A(0) });
        book.SetShade(A(0), "파랑");
        undo(step);

        void undo(UndoStep s)
        {
            var stack = new UndoStack();
            stack.Push(s.Commit(book));
            stack.Undo(book);
        }
        Assert.Equal("노랑", book.FindCell(A(0))!.Shade);
    }

    [Fact]
    public void 바뀐_것이_없으면_단계를_쌓지_않는다()
    {
        var book = new Workbook();
        var undo = new UndoStack();
        UndoStep step = UndoStep.Begin(book, new[] { A(0) });
        undo.Push(step.Commit(book));
        Assert.Equal(0, undo.Count);
    }
}

public class DelimitedTextTests
{
    [Fact]
    public void 화면_문자열이_아니라_저장된_값을_싣는다()
    {
        var book = new Workbook();
        book.SetInput(new CellAddress(0, 0, 0), "123456789012");    // 화면에는 1.2346E+11
        book.SetInput(new CellAddress(0, 0, 1), "2026-09-11");      // 화면에는 2026-09-11
        string tsv = DelimitedText.Write(book, CellRange.Of(0, 0, 0, 0, 1), DelimitedText.Tab);
        Assert.Equal("123456789012\t2026-09-11\r\n", tsv);
    }

    [Fact]
    public void 수식은_계산_결과를_싣는다()
    {
        var book = new Workbook();
        book.SetInput(new CellAddress(0, 0, 0), "2");
        book.SetInput(new CellAddress(0, 0, 1), "=A1*3");
        string tsv = DelimitedText.Write(book, CellRange.Of(0, 0, 0, 0, 1), DelimitedText.Tab);
        Assert.Equal("2\t6\r\n", tsv);
    }

    [Fact]
    public void 구분자가_든_텍스트는_따옴표로_묶는다()
    {
        var book = new Workbook();
        book.SetInput(new CellAddress(0, 0, 0), "가,나");
        Assert.Equal("\"가,나\"\r\n", DelimitedText.Write(book, CellRange.One(new CellAddress(0, 0, 0)), DelimitedText.Comma));
        Assert.Equal("가,나\r\n", DelimitedText.Write(book, CellRange.One(new CellAddress(0, 0, 0)), DelimitedText.Tab));
    }

    [Fact]
    public void 탭은_열_줄바꿈은_행으로_나눈다()
    {
        var rows = DelimitedText.Read("1\t2\r\n3\t4\r\n", DelimitedText.Tab);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "1", "2" }, rows[0]);
        Assert.Equal(new[] { "3", "4" }, rows[1]);
    }

    [Fact]
    public void 따옴표로_묶인_칸을_푼다()
    {
        var rows = DelimitedText.Read("\"가,나\",다\r\n", DelimitedText.Comma);
        Assert.Equal(new[] { "가,나", "다" }, rows[0]);
    }
}

public class SaveSchedulerTests
{
    [Fact]
    public void 마지막_입력에서_500ms_뒤에_쓴다()
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0);
        var s = new SaveScheduler();
        s.Touch(now);
        Assert.False(s.ShouldSave(now.AddMilliseconds(499)));
        Assert.True(s.ShouldSave(now.AddMilliseconds(500)));
    }

    [Fact]
    public void 계속_치면_5초마다_한_번은_쓴다()
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0);
        var s = new SaveScheduler();
        s.Touch(now);
        for (int ms = 100; ms < 5000; ms += 100)
        {
            s.Touch(now.AddMilliseconds(ms));
            Assert.False(s.ShouldSave(now.AddMilliseconds(ms)));   // 유휴가 오지 않는다
        }
        s.Touch(now.AddMilliseconds(5000));
        Assert.True(s.ShouldSave(now.AddMilliseconds(5000)));      // 그래도 5초에 한 번
    }

    [Fact]
    public void 쓰고_나면_다시_조용해진다()
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0);
        var s = new SaveScheduler();
        s.Touch(now);
        Assert.True(s.IsDirty);
        s.Saved();
        Assert.False(s.IsDirty);
        Assert.False(s.ShouldSave(now.AddHours(1)));
    }
}

public class BookStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DesktopSheetTest-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { System.IO.Directory.Delete(_dir, true); } catch (IOException) { } }

    private static Workbook Sample()
    {
        var book = new Workbook(2);
        book.SetInput(new CellAddress(0, 0, 0), "2");
        book.SetInput(new CellAddress(0, 0, 1), "=A1*3");
        book.SetInput(new CellAddress(0, 1, 0), "합계");
        book.SetShade(new CellAddress(0, 1, 0), "노랑");
        book.SetInk(new CellAddress(0, 1, 0), "빨강");
        book.SetInput(new CellAddress(1, 0, 0), "2026-09-11");
        return book;
    }

    [Fact]
    public void 저장하고_다시_읽으면_값과_수식과_서식이_그대로다()
    {
        var store = new BookStore(_dir);
        store.Save(Sample(), new WindowPlacement { X = 10, Y = 20, Sheet = 1, Row = 3, Col = 4 });

        LoadResult r = store.Load();
        Assert.Equal(LoadOutcome.Loaded, r.Outcome);
        Assert.Equal(2, r.Book.Sheets.Count);
        Assert.Equal(6, r.Book.Read(new CellAddress(0, 0, 1)).Number);        // 수식이 다시 셈해졌다
        Assert.Equal("=A1*3", r.Book.FindCell(new CellAddress(0, 0, 1))!.Raw);
        Assert.Equal("노랑", r.Book.FindCell(new CellAddress(0, 1, 0))!.Shade);
        Assert.Equal("빨강", r.Book.FindCell(new CellAddress(0, 1, 0))!.Ink);
        Assert.Equal(46276, r.Book.Read(new CellAddress(1, 0, 0)).Number);
        Assert.Equal(10, r.Window.X);
        Assert.Equal(20, r.Window.Y);
        Assert.Equal((1, 3, 4), (r.Window.Sheet, r.Window.Row, r.Window.Col));
    }

    [Fact]
    public void 두_번째_저장부터_직전_복사본이_남는다()
    {
        var store = new BookStore(_dir);
        store.Save(Sample(), new WindowPlacement());
        Assert.False(File.Exists(store.PrevPath));

        var second = Sample();
        second.SetInput(new CellAddress(0, 0, 0), "99");
        store.Save(second, new WindowPlacement());

        Assert.True(File.Exists(store.PrevPath));
        Assert.True(File.Exists(store.BookPath));
        Assert.DoesNotContain("book.tmp", System.IO.Directory.GetFiles(_dir).Select(Path.GetFileName)!);
    }

    [Fact]
    public void 파일이_깨지면_직전_복사본으로_되살린다()
    {
        var store = new BookStore(_dir);
        store.Save(Sample(), new WindowPlacement());
        var second = Sample();
        second.SetInput(new CellAddress(0, 0, 0), "99");
        store.Save(second, new WindowPlacement());

        File.WriteAllText(store.BookPath, "{ 이건 JSON 이 아니다");

        LoadResult r = store.Load();
        Assert.Equal(LoadOutcome.RestoredFromPrev, r.Outcome);
        Assert.Equal(2, r.Book.Read(new CellAddress(0, 0, 0)).Number);   // 첫 번째 저장분
    }

    [Fact]
    public void 둘_다_못_읽으면_빈_시트로_시작하고_깨진_파일을_옆에_남긴다()
    {
        var store = new BookStore(_dir);
        System.IO.Directory.CreateDirectory(_dir);
        File.WriteAllText(store.BookPath, "깨짐");

        LoadResult r = store.Load();
        Assert.Equal(LoadOutcome.StartedEmpty, r.Outcome);
        Assert.NotNull(r.BrokenFile);
        Assert.True(File.Exists(r.BrokenFile!));
        Assert.False(File.Exists(store.BookPath));
        Assert.Single(r.Book.Sheets);
    }

    [Fact]
    public void 값이_든_칸만_적는다()
    {
        var store = new BookStore(_dir);
        var book = new Workbook();
        book.SetInput(new CellAddress(0, 100, 20), "1");
        store.Save(book, new WindowPlacement());

        string json = File.ReadAllText(store.BookPath);
        Assert.True(json.Length < 300, $"빈 시트에 한 칸만 썼는데 {json.Length}자입니다");
    }
}
