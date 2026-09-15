using System;
using System.Collections.Generic;
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
    public const double FontSize = 16;          // 12pt = 96 DPI 에서 16px
    public const double ScrollBarWidth = 8;
    public const double ColHeaderHeight = 20;
    public const double RowHeight = 20;

    /// <summary>장평. 글자를 가로로 누르는 비율이다. 1 이면 누르지 않는다.</summary>
    public const double Squeeze = 0.9375;

    /// <summary>자간 보정(px). 음수면 글자 사이가 좁아진다. 글자 모양은 건드리지 않는다.</summary>
    public const double Tracking = -0.5;

    /// <summary>
    /// 영문 한 자가 차지하는 가로 자리. 한글은 이것의 두 배다.
    /// 글꼴이 주는 폭을 믿지 않고 우리가 정한 값으로 한 자씩 놓기 때문에, 글꼴이 조금 달라도 격자가 어긋나지 않는다.
    /// </summary>
    public const double CharAdvance = FontSize / 2 * Squeeze + Tracking;

    /// <summary>1장: 내용 10자 + 부호 1자 + 좌우 공백 2자.</summary>
    public const int CellChars = 13;
    public const double CellWidth = CellChars * CharAdvance;
    public const double CellPadding = CharAdvance;
    public const double RowHeaderWidth = 3 * CharAdvance + 16;   // 행 번호 세 자리와 좌우 여백

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

    /// <summary>화면 배율(14.5). 그릴 때마다 갱신해 글자와 선을 화면 픽셀에 붙이는 데 쓴다.</summary>
    private double _dpi = 1.0;
    private Pen _hairline = GridPen;

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
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi != _dpi)
        {
            _dpi = dpi <= 0 ? 1.0 : dpi;
            // 선을 화면 픽셀 하나 굵기로 맞춘다. 배율이 150% 면 1 단위가 1.5픽셀이라 그냥 두면 흐려진다.
            _hairline = Freeze(new Pen(GridLine, 1 / _dpi));
        }

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

        Brush ink = PaletteBrushes.Ink(cell?.Ink, cell?.Shade);
        double width = TextWidth.Of(text) * CharAdvance;
        double x = rightAligned ? rect.Right - CellPadding - width : rect.Left + CellPadding;
        DrawSlots(dc, text, x, rect.Top, ink);
    }

    /// <summary>
    /// 글자를 한 자씩 제 자리에 놓는다. 고정폭 격자라 자리가 이미 정해져 있으므로 이렇게 놓는 편이 정확하다.
    /// 자간은 자리 사이를 좁히는 것이고 장평은 글자를 누르는 것이라, 둘을 따로 조절할 수 있다.
    /// </summary>
    private void DrawSlots(DrawingContext dc, string text, double x, double top, Brush ink)
    {
        foreach (char ch in text)
        {
            FormattedText ft = Glyph(ch, ink);
            // 한 자 폭이 7px 이면 배율 150% 에서 10.5픽셀이라 글자가 반 픽셀씩 밀린다. 화면 픽셀에 붙여 놓는다.
            double px = SnapToPixel(x);
            double y = SnapToPixel(top + (RowHeight - ft.Height) / 2);

            if (Squeeze != 1.0)
            {
                dc.PushTransform(new ScaleTransform(Squeeze, 1, px, 0));
                dc.DrawText(ft, new Point(px, y));
                dc.Pop();
            }
            else dc.DrawText(ft, new Point(px, y));

            x += TextWidth.Of(ch) * CharAdvance;
        }
    }

    // 글자 모양은 몇 가지 안 되니 만들어 두고 돌려쓴다. 칸마다 다시 만들면 그리는 값이 그만큼 붙는다.
    private readonly Dictionary<(char, Brush), FormattedText> _glyphs = new();

    private FormattedText Glyph(char ch, Brush ink)
    {
        if (_glyphs.TryGetValue((ch, ink), out FormattedText? ft)) return ft;
        ft = new FormattedText(ch.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                               CellFace, FontSize, ink, 96);
        if (_glyphs.Count > 4096) _glyphs.Clear();
        _glyphs[(ch, ink)] = ft;
        return ft;
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
            double x = SnapToLine(RowHeaderWidth + c * CellWidth);
            dc.DrawLine(_hairline, new Point(x, 0), new Point(x, bottom));
        }
        for (int r = 0; r <= rows; r++)
        {
            double y = SnapToLine(ColHeaderHeight + r * RowHeight);
            dc.DrawLine(_hairline, new Point(0, y), new Point(right, y));
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
        dc.DrawRectangle(ScrollThumb, null, VerticalThumb());
        dc.DrawRectangle(ScrollThumb, null, HorizontalThumb());
    }

    // 손잡이 자리를 한 곳에서 셈한다. 그리는 자리와 잡는 자리가 어긋나지 않게 하려는 것이다.
    private double VerticalTrack => Math.Max(1, ActualHeight - ColHeaderHeight - ScrollBarWidth);
    private double HorizontalTrack => Math.Max(1, ActualWidth - RowHeaderWidth - ScrollBarWidth);
    private int MaxTopRow => Math.Max(0, Sheet.Rows - VisibleRows);
    private int MaxLeftCol => Math.Max(0, Sheet.Cols - VisibleCols);

    private Rect VerticalThumb()
    {
        double height = Math.Max(20, VerticalTrack * VisibleRows / Sheet.Rows);
        double span = Math.Max(0, VerticalTrack - height);
        double top = MaxTopRow == 0 ? 0 : span * TopRow / MaxTopRow;
        return new Rect(ActualWidth - ScrollBarWidth, ColHeaderHeight + top, ScrollBarWidth, height);
    }

    private Rect HorizontalThumb()
    {
        double width = Math.Max(20, HorizontalTrack * VisibleCols / Sheet.Cols);
        double span = Math.Max(0, HorizontalTrack - width);
        double left = MaxLeftCol == 0 ? 0 : span * LeftCol / MaxLeftCol;
        return new Rect(RowHeaderWidth + left, ActualHeight - ScrollBarWidth, width, ScrollBarWidth);
    }

    private enum ScrollDrag { None, Vertical, Horizontal }
    private ScrollDrag _scrollDrag;
    private double _scrollGrabOffset;

    /// <summary>스크롤바를 눌렀으면 참을 돌려준다. 손잡이면 끌기가 시작되고 빈 곳이면 한 화면씩 넘긴다.</summary>
    private bool TryStartScroll(Point p)
    {
        if (p.X >= ActualWidth - ScrollBarWidth && p.Y >= ColHeaderHeight)
        {
            Rect thumb = VerticalThumb();
            if (p.Y >= thumb.Top && p.Y <= thumb.Bottom)
            {
                _scrollDrag = ScrollDrag.Vertical;
                _scrollGrabOffset = p.Y - thumb.Top;
                CaptureMouse();
            }
            else ScrollBy(p.Y < thumb.Top ? -VisibleRows : VisibleRows, 0);
            return true;
        }

        if (p.Y >= ActualHeight - ScrollBarWidth && p.X >= RowHeaderWidth)
        {
            Rect thumb = HorizontalThumb();
            if (p.X >= thumb.Left && p.X <= thumb.Right)
            {
                _scrollDrag = ScrollDrag.Horizontal;
                _scrollGrabOffset = p.X - thumb.Left;
                CaptureMouse();
            }
            else ScrollBy(0, p.X < thumb.Left ? -VisibleCols : VisibleCols);
            return true;
        }

        return false;
    }

    private void DragScroll(Point p)
    {
        if (_scrollDrag == ScrollDrag.Vertical)
        {
            double span = Math.Max(1, VerticalTrack - VerticalThumb().Height);
            double at = p.Y - _scrollGrabOffset - ColHeaderHeight;
            TopRow = (int)Math.Round(Math.Clamp(at / span, 0, 1) * MaxTopRow);
        }
        else
        {
            double span = Math.Max(1, HorizontalTrack - HorizontalThumb().Width);
            double at = p.X - _scrollGrabOffset - RowHeaderWidth;
            LeftCol = (int)Math.Round(Math.Clamp(at / span, 0, 1) * MaxLeftCol);
        }
        InvalidateVisual();
    }

    private static void DrawCentered(DrawingContext dc, string text, Rect rect, Brush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            HeaderFace, 11, brush, 96);
        dc.DrawText(ft, new Point(rect.Left + (rect.Width - ft.Width) / 2, rect.Top + (rect.Height - ft.Height) / 2));
    }

    /// <summary>화면 픽셀 경계에 붙인다.</summary>
    private double SnapToPixel(double v) => Math.Round(v * _dpi) / _dpi;

    /// <summary>한 픽셀 굵기 선은 픽셀 한가운데에 놓아야 두 픽셀에 걸쳐 흐려지지 않는다.</summary>
    private double SnapToLine(double v) => (Math.Round(v * _dpi) + 0.5) / _dpi;

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

        if (TryStartScroll(p)) { e.Handled = true; return; }

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
        if (_scrollDrag != ScrollDrag.None) { DragScroll(e.GetPosition(this)); return; }
        if (!_dragging) return;
        Point p = e.GetPosition(this);
        Selection.ExtendTo(RowAt(p.Y), ColAt(p.X));
        Raise();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_scrollDrag != ScrollDrag.None) { _scrollDrag = ScrollDrag.None; ReleaseMouseCapture(); return; }
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    /// <summary>
    /// 우클릭한 칸으로 커서를 옮긴다. 그러지 않으면 메뉴가 아까 고른 칸에 걸린다.
    /// 이미 고른 영역 안을 눌렀으면 영역을 그대로 둔다 - 여러 칸에 한꺼번에 색을 칠할 때 쓴다.
    /// </summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        Point p = e.GetPosition(this);
        if (p.X < RowHeaderWidth || p.Y < ColHeaderHeight) return;

        int row = RowAt(p.Y), col = ColAt(p.X);
        var at = new CellAddress(Selection.Cursor.Sheet, row, col);
        if (!Selection.Range.Contains(at)) Selection.MoveTo(row, col);
        Raise();
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
