using System;

namespace DesktopSheet.Core;

/// <summary>고른 사각 영역. 어느 모서리에서 끌었든 좌상단과 우하단으로 맞춰 든다.</summary>
public readonly record struct CellRange(int Sheet, int Top, int Left, int Bottom, int Right)
{
    public static CellRange Of(int sheet, int r1, int c1, int r2, int c2) =>
        new(sheet, Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2));

    public static CellRange One(CellAddress a) => new(a.Sheet, a.Row, a.Col, a.Row, a.Col);

    public int Rows => Bottom - Top + 1;
    public int Cols => Right - Left + 1;
    public int Count => Rows * Cols;
    public bool IsSingle => Count == 1;

    public bool Contains(CellAddress a) =>
        a.Sheet == Sheet && a.Row >= Top && a.Row <= Bottom && a.Col >= Left && a.Col <= Right;
}

public enum MoveDirection { Up, Down, Left, Right }

/// <summary>
/// 사양서 10.1. 커서가 있는 칸과 고른 영역을 함께 든다.
/// 영역을 골라 둔 채로 Enter 나 Tab 을 누르면 커서가 그 영역 안에서만 돈다.
/// </summary>
public sealed class Selection
{
    public CellAddress Cursor { get; private set; }
    public CellRange Range { get; private set; }

    public Selection(int sheet = 0)
    {
        Cursor = new CellAddress(sheet, 0, 0);
        Range = CellRange.One(Cursor);
    }

    /// <summary>한 칸을 고른다. 클릭과 같다.</summary>
    public void MoveTo(int row, int col)
    {
        Cursor = new CellAddress(Cursor.Sheet, Clamp(row, Sheet.Rows), Clamp(col, Sheet.Cols));
        Range = CellRange.One(Cursor);
    }

    /// <summary>커서를 둔 채 영역만 넓힌다. Shift+클릭, Shift+방향키와 같다.</summary>
    public void ExtendTo(int row, int col)
    {
        int r = Clamp(row, Sheet.Rows), c = Clamp(col, Sheet.Cols);
        Range = CellRange.Of(Cursor.Sheet, Cursor.Row, Cursor.Col, r, c);
    }

    public void SelectAll()
    {
        Range = new CellRange(Cursor.Sheet, 0, 0, Sheet.Rows - 1, Sheet.Cols - 1);
    }

    public void SelectRow(int row)
    {
        int r = Clamp(row, Sheet.Rows);
        Cursor = new CellAddress(Cursor.Sheet, r, 0);
        Range = new CellRange(Cursor.Sheet, r, 0, r, Sheet.Cols - 1);
    }

    public void SelectColumn(int col)
    {
        int c = Clamp(col, Sheet.Cols);
        Cursor = new CellAddress(Cursor.Sheet, 0, c);
        Range = new CellRange(Cursor.Sheet, 0, c, Sheet.Rows - 1, c);
    }

    /// <summary>방향키. extend 면 영역을 넓히고 아니면 한 칸을 고른다.</summary>
    public void Move(MoveDirection dir, bool extend = false)
    {
        (int dr, int dc) = Delta(dir);
        if (extend)
        {
            int r = Range.Top == Cursor.Row && Range.Bottom != Cursor.Row ? Range.Bottom : Range.Top;
            int c = Range.Left == Cursor.Col && Range.Right != Cursor.Col ? Range.Right : Range.Left;
            if (r == Cursor.Row && Range.Rows > 1) r = Range.Bottom;
            if (c == Cursor.Col && Range.Cols > 1) c = Range.Right;
            ExtendTo(r + dr, c + dc);
        }
        else
        {
            MoveTo(Cursor.Row + dr, Cursor.Col + dc);
        }
    }

    /// <summary>
    /// Ctrl+방향키. 엑셀과 같은 규칙이다 - 바로 옆이 값이 든 칸이면 이어진 값의 끝까지,
    /// 비어 있으면 다음에 값이 나오는 칸까지, 끝까지 없으면 시트 끝까지 간다.
    /// </summary>
    public void Jump(MoveDirection dir, Workbook book)
    {
        (int dr, int dc) = Delta(dir);
        int r = Cursor.Row, c = Cursor.Col;
        int nr = r + dr, nc = c + dc;
        if (!Sheet.InRange(nr, nc)) { MoveTo(r, c); return; }

        if (HasValue(book, Cursor.Sheet, nr, nc))
        {
            while (Sheet.InRange(nr, nc) && HasValue(book, Cursor.Sheet, nr, nc))
            {
                r = nr; c = nc; nr += dr; nc += dc;
            }
        }
        else
        {
            while (Sheet.InRange(nr, nc))
            {
                r = nr; c = nc;
                if (HasValue(book, Cursor.Sheet, r, c)) break;
                nr += dr; nc += dc;
            }
        }
        MoveTo(r, c);
    }

    /// <summary>Enter 와 Tab. 영역을 골라 두었으면 그 안에서만 돈다.</summary>
    public void Advance(MoveDirection dir)
    {
        if (Range.IsSingle) { Move(dir); return; }

        (int dr, int dc) = Delta(dir);
        int r = Cursor.Row + dr, c = Cursor.Col + dc;

        if (dc != 0)
        {
            if (c > Range.Right)  { c = Range.Left;  r++; }
            if (c < Range.Left)   { c = Range.Right; r--; }
            if (r > Range.Bottom) r = Range.Top;
            if (r < Range.Top)    r = Range.Bottom;
        }
        else
        {
            if (r > Range.Bottom) { r = Range.Top;    c++; }
            if (r < Range.Top)    { r = Range.Bottom; c--; }
            if (c > Range.Right)  c = Range.Left;
            if (c < Range.Left)   c = Range.Right;
        }

        CellRange keep = Range;
        Cursor = new CellAddress(Cursor.Sheet, r, c);
        Range = keep;
    }

    public void SetSheet(int sheet)
    {
        Cursor = Cursor with { Sheet = sheet };
        Range = CellRange.One(Cursor);
    }

    private static bool HasValue(Workbook book, int sheet, int row, int col) =>
        book.Read(new CellAddress(sheet, row, col)).Kind != ValueKind.Blank;

    private static (int, int) Delta(MoveDirection d) => d switch
    {
        MoveDirection.Up    => (-1, 0),
        MoveDirection.Down  => (1, 0),
        MoveDirection.Left  => (0, -1),
        _                   => (0, 1),
    };

    private static int Clamp(int v, int max) => Math.Clamp(v, 0, max - 1);
}
