using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopSheet.Core;

namespace DesktopSheet.App;

/// <summary>
/// 사양서 12.3 의 그리드. 칸마다 컨트롤을 만들지 않고 화면에 보이는 칸만 직접 그린다(16장).
/// </summary>
public sealed class SheetGrid : FrameworkElement
{
    // 1장과 12.3 이 정한 치수. DPI 배율은 WPF 가 알아서 곱한다(14.5).
    public const double CellWidth = 104;
    public const double RowHeight = 20;
    public const double RowHeaderWidth = 40;
    public const double ColHeaderHeight = 20;
    public const double CellPadding = 8;
    public const double FontSize = 16;          // 12pt = 96 DPI 에서 16px
    public const double ScrollBarWidth = 8;

    private static readonly Typeface CellFace =
        new(new FontFamily("Nanum Gothic Coding, D2Coding, Consolas, Global Monospace"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface HeaderFace =
        new(new FontFamily("Segoe UI, Malgun Gothic"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Brush GridLine = Freeze(new SolidColorBrush(Color.FromRgb(0xD0, 0xD4, 0xDA)));
    private static readonly Brush HeaderBack = Freeze(new SolidColorBrush(Color.FromRgb(0xEF, 0xF1, 0xF4)));
    private static readonly Brush HeaderInk = Freeze(new SolidColorBrush(Color.FromRgb(0x58, 0x60, 0x70)));
    private static readonly Brush SelectionEdge = Freeze(new SolidColorBrush(Color.FromRgb(0x0B, 0x48, 0xC8)));
    private static readonly Brush ScrollThumb = Freeze(new SolidColorBrush(Color.FromArgb(0x60, 0x58, 0x60, 0x70)));
    private static readonly Pen GridPen = Freeze(new Pen(GridLine, 1));
    private static readonly Pen SelectionPen = Freeze(new Pen(SelectionEdge, 2));
    private static readonly Pen CursorPen = Freeze(new Pen(SelectionEdge, 1));
    private static readonly Pen CopyPen = Freeze(new Pen(SelectionEdge, 1) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) });

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public Workbook Book { get; set; } = new();
    public Selection Selection { get; set; } = new();
    public CellRange? CopyMarquee { get; set; }

    public int TopRow { get; private set; }
    public int LeftCol { get; private set; }

    public event EventHandler? SelectionChanged;
    public event EventHandler? EditRequested;

    private bool _dragging;

    public SheetGrid()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
    }

    public int VisibleRows => Math.Max(1, (int)((ActualHeight - ColHeaderHeight) / RowHeight));
    public int VisibleCols => Math.Max(1, (int)((ActualWidth - RowHeaderWidth) / CellWidth));

    /// <summary>커서가 보이는 범위를 벗어나면 화면이 따라간다(12.3).</summary>
    public void ScrollIntoView()
    {
        CellAddress c = Selection.Cursor;
        if (c.Row < TopRow) TopRow = c.Row;
        else if (c.Row >= TopRow + VisibleRows) TopRow = c.Row - VisibleRows + 1;
        if (c.Col < LeftCol) LeftCol = c.Col;
        else if (c.Col >= LeftCol + VisibleCols) LeftCol = c.Col - VisibleCols + 1;

        TopRow = Math.Clamp(TopRow, 0, Math.Max(0, Sheet.Rows - VisibleRows));
        LeftCol = Math.Clamp(LeftCol, 0, Math.Max(0, Sheet.Cols - VisibleCols));
        InvalidateVisual();
    }

    public void ScrollBy(int rows, int cols)
    {
        TopRow = Math.Clamp(TopRow + rows, 0, Math.Max(0, Sheet.Rows - VisibleRows));
        LeftCol = Math.Clamp(LeftCol + cols, 0, Math.Max(0, Sheet.Cols - VisibleCols));
        InvalidateVisual();
    }

    /// <summary>칸 하나가 차지하는 자리. 편집기를 얹을 때도 쓴다.</summary>
    public Rect CellRect(int row, int col) => new(
        RowHeaderWidth + (col - LeftCol) * CellWidth,
        ColHeaderHeight + (row - TopRow) * RowHeight,
        CellWidth, RowHeight);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));

        int rows = Math.Min(VisibleRows + 1, Sheet.Rows - TopRow);
        int cols = Math.Min(VisibleCols + 1, Sheet.Cols - LeftCol);

        DrawHeaders(dc, rows, cols);

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                DrawCell(dc, TopRow + r, LeftCol + c);

        DrawGridLines(dc, rows, cols);
        DrawSelection(dc);
        DrawScrollBars(dc);
    }

    private void DrawHeaders(DrawingContext dc, int rows, int cols)
    {
        dc.DrawRectangle(HeaderBack, null, new Rect(0, 0, ActualWidth, ColHeaderHeight));
        dc.DrawRectangle(HeaderBack, null, new Rect(0, 0, RowHeaderWidth, ActualHeight));

        for (int c = 0; c < cols; c++)
        {
            int col = LeftCol + c;
            var rect = new Rect(RowHeaderWidth + c * CellWidth, 0, CellWidth, ColHeaderHeight);
            bool inSelection = col >= Selection.Range.Left && col <= Selection.Range.Right;
            if (inSelection) dc.DrawRectangle(PaletteBrushes.Of("#DCE4F2"), null, rect);
            DrawCentered(dc, ((char)('A' + col)).ToString(), rect, HeaderInk);
        }

        for (int r = 0; r < rows; r++)
        {
            int row = TopRow + r;
            var rect = new Rect(0, ColHeaderHeight + r * RowHeight, RowHeaderWidth, RowHeight);
            bool inSelection = row >= Selection.Range.Top && row <= Selection.Range.Bottom;
            if (inSelection) dc.DrawRectangle(PaletteBrushes.Of("#DCE4F2"), null, rect);
            DrawCentered(dc, (row + 1).ToString(CultureInfo.InvariantCulture), rect, HeaderInk);
        }
    }

    private void DrawCell(DrawingContext dc, int row, int col)
    {
        var at = new CellAddress(Selection.Cursor.Sheet, row, col);
        Cell? cell = Book.FindCell(at);
        Rect rect = CellRect(row, col);

        if (cell?.Shade is not null)
            dc.DrawRectangle(PaletteBrushes.Shade(cell.Shade), null, rect);

        Value v = Book.Read(at);
        if (v.Kind == ValueKind.Blank) return;

        (string text, bool rightAligned) = Render(cell, v);
        if (text.Length == 0) return;

        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            CellFace, FontSize, PaletteBrushes.Ink(cell?.Ink, cell?.Shade), 96)
        { MaxTextWidth = CellWidth - CellPadding * 2, MaxLineCount = 1, Trimming = TextTrimming.None };

        double x = rightAligned
            ? rect.Right - CellPadding - ft.WidthIncludingTrailingWhitespace
            : rect.Left + CellPadding;
        dc.DrawText(ft, new Point(x, rect.Top + (RowHeight - ft.Height) / 2));
    }

    /// <summary>사양서 2~6장의 표시 규칙. 숫자는 오른쪽, 텍스트는 왼쪽에 붙는다.</summary>
    public static (string Text, bool RightAligned) Render(Cell? cell, Value v) => v.Kind switch
    {
        ValueKind.Blank  => ("", true),
        ValueKind.Error  => (CellErrorText.Of(v.Error), true),
        ValueKind.Bool   => (v.Number != 0 ? "TRUE" : "FALSE", true),
        ValueKind.Text   => (DisplayFormatter.Text(v.Text), false),
        _                => (cell is null || NumberFormat.IsGeneral(cell.FormatCode)
                                ? DisplayFormatter.General(v.Number)
                                : DisplayFormatter.WithFormat(v.Number, cell.FormatCode), true),
    };

    private void DrawGridLines(DrawingContext dc, int rows, int cols)
    {
        double right = Math.Min(ActualWidth, RowHeaderWidth + cols * CellWidth);
        double bottom = Math.Min(ActualHeight, ColHeaderHeight + rows * RowHeight);

        for (int c = 0; c <= cols; c++)
        {
            double x = Snap(RowHeaderWidth + c * CellWidth);
            dc.DrawLine(GridPen, new Point(x, 0), new Point(x, bottom));
        }
        for (int r = 0; r <= rows; r++)
        {
            double y = Snap(ColHeaderHeight + r * RowHeight);
            dc.DrawLine(GridPen, new Point(0, y), new Point(right, y));
        }
    }

    /// <summary>10.1. 고른 영역은 굵은 테두리로만 보인다. 반투명으로 덮으면 칸에 칠한 음영 색이 섞인다.</summary>
    private void DrawSelection(DrawingContext dc)
    {
        CellRange sel = Selection.Range;
        Rect topLeft = CellRect(sel.Top, sel.Left);
        Rect bottomRight = CellRect(sel.Bottom, sel.Right);
        var box = new Rect(topLeft.Left, topLeft.Top, bottomRight.Right - topLeft.Left, bottomRight.Bottom - topLeft.Top);
        dc.DrawRectangle(null, SelectionPen, box);

        if (!sel.IsSingle)
        {
            Rect cursor = CellRect(Selection.Cursor.Row, Selection.Cursor.Col);
            dc.DrawRectangle(null, CursorPen, Rect.Inflate(cursor, -2, -2));
        }

        if (CopyMarquee is { } m && m.Sheet == Selection.Cursor.Sheet)
        {
            Rect a = CellRect(m.Top, m.Left), b = CellRect(m.Bottom, m.Right);
            dc.DrawRectangle(null, CopyPen, new Rect(a.Left, a.Top, b.Right - a.Left, b.Bottom - a.Top));
        }
    }

    /// <summary>12.3. 스크롤바는 그리드 위에 겹쳐 그려 창 크기를 먹지 않는다.</summary>
    private void DrawScrollBars(DrawingContext dc)
    {
        double trackH = ActualHeight - ColHeaderHeight;
        double thumbH = Math.Max(20, trackH * VisibleRows / Sheet.Rows);
        double posH = trackH * TopRow / Sheet.Rows;
        dc.DrawRectangle(ScrollThumb, null,
            new Rect(ActualWidth - ScrollBarWidth, ColHeaderHeight + posH, ScrollBarWidth, thumbH));

        double trackW = ActualWidth - RowHeaderWidth;
        double thumbW = Math.Max(20, trackW * VisibleCols / Sheet.Cols);
        double posW = trackW * LeftCol / Sheet.Cols;
        dc.DrawRectangle(ScrollThumb, null,
            new Rect(RowHeaderWidth + posW, ActualHeight - ScrollBarWidth, thumbW, ScrollBarWidth));
    }

    private static void DrawCentered(DrawingContext dc, string text, Rect rect, Brush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            HeaderFace, 11, brush, 96);
        dc.DrawText(ft, new Point(rect.Left + (rect.Width - ft.Width) / 2, rect.Top + (rect.Height - ft.Height) / 2));
    }

    private static double Snap(double v) => Math.Round(v) + 0.5;

    // --- 마우스 (10.1) ---

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ClickCount == 2)                       // 더블클릭은 편집으로 들어간다(8.6)
        {
            EditRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        Point p = e.GetPosition(this);
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (p.X < RowHeaderWidth && p.Y >= ColHeaderHeight)
        {
            Selection.SelectRow(RowAt(p.Y));
        }
        else if (p.Y < ColHeaderHeight && p.X >= RowHeaderWidth)
        {
            Selection.SelectColumn(ColAt(p.X));
        }
        else if (p.X < RowHeaderWidth && p.Y < ColHeaderHeight)
        {
            Selection.SelectAll();
        }
        else
        {
            if (shift) Selection.ExtendTo(RowAt(p.Y), ColAt(p.X));
            else Selection.MoveTo(RowAt(p.Y), ColAt(p.X));
            _dragging = true;
            CaptureMouse();
        }

        Raise();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        Point p = e.GetPosition(this);
        Selection.ExtendTo(RowAt(p.Y), ColAt(p.X));
        Raise();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        int steps = e.Delta > 0 ? -3 : 3;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) ScrollBy(0, steps);
        else ScrollBy(steps, 0);
        e.Handled = true;
    }

    private int RowAt(double y) => Math.Clamp(TopRow + (int)((y - ColHeaderHeight) / RowHeight), 0, Sheet.Rows - 1);
    private int ColAt(double x) => Math.Clamp(LeftCol + (int)((x - RowHeaderWidth) / CellWidth), 0, Sheet.Cols - 1);

    public void Raise()
    {
        ScrollIntoView();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
