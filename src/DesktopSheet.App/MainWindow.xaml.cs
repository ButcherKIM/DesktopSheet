using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopSheet.Core;

namespace DesktopSheet.App;

public partial class MainWindow : Window
{
    private readonly BookStore _store;
    private readonly SaveScheduler _scheduler = new();
    private readonly UndoStack _undo = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly ToolTip _cellTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };

    private Workbook _book;
    private WindowPlacement _windowState;
    private CellRange? _copySource;
    private string _copyClipboardText = "";
    private CellAddress? _tipCell;
    private bool _exiting;
    private int? _renamingSheet;
    private HelpWindow? _help;
    private bool _editing;
    private bool _suppressTextChanged;

    public MainWindow(BookStore store, LoadResult loaded)
    {
        InitializeComponent();

        _store = store;
        _book = loaded.Book;
        _windowState = loaded.Window;

        GridView.Book = _book;
        GridView.Selection = new Selection(Math.Clamp(_windowState.Sheet, 0, _book.Sheets.Count - 1));
        GridView.Selection.MoveTo(_windowState.Row, _windowState.Col);
        GridView.SelectionChanged += (_, _) => { BuildTabs(); GridView.InvalidateVisual(); FocusEditor(); };
        GridView.Scrolled += (_, _) => PositionEditor();
        GridView.EditRequested += (_, _) => BeginEdit();
        // 칸을 클릭하면 치고 있던 값을 먼저 확정한다. 그 뒤에 그리드가 커서를 옮긴다.
        GridView.PreviewMouseLeftButtonDown += (_, _) => CommitEdit(MoveDirection.Down, move: false);
        GridView.PreviewMouseRightButtonDown += (_, _) => CommitEdit(MoveDirection.Down, move: false);
        GridView.MouseMove += GridView_MouseMove;
        GridView.MouseRightButtonUp += GridView_MouseRightButtonUp;

        Width = _windowState.Width;
        Height = _windowState.Height;
        if (_windowState.X is null)                      // 자리를 잡은 적이 없으면 5열 20행이 보이는 크기로 연다
        {
            Width = 5 * SheetGrid.CellWidth + SheetGrid.RowHeaderWidth;
            Height = 20 * SheetGrid.RowHeight + SheetGrid.ColHeaderHeight + 24;
        }
        if (_windowState.X is { } x && _windowState.Y is { } y)
        {
            (double cx, double cy) = Win32.ClampToScreen(x, y, Width, Height);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = cx;
            Top = cy;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        SourceInitialized += (_, _) => Win32.HideFromTaskbarAndAltTab(this);
        Loaded += (_, _) => { BuildTabs(); ShowIdle(); FocusEditor(); };
        Activated += (_, _) => FocusEditor();

        _saveTimer.Tick += (_, _) => SaveIfDue();
        _saveTimer.Start();

        Editor.PreviewKeyDown += Editor_PreviewKeyDown;
        Editor.TextChanged += Editor_TextChanged;
        Editor.LostKeyboardFocus += (_, _) => CommitEdit(MoveDirection.Down, move: false);
        PreviewKeyDown += Window_PreviewKeyDown;
        // 14.1: 창에는 닫기 단추가 없다. 밖에서 닫으라고 해도 숨기기만 하되, 종료할 때는 막지 않는다.
        Closing += (_, e) => { if (!_exiting) { e.Cancel = true; Hide(); } };
    }

    private Selection Sel => GridView.Selection;
    private int SheetIndex => Sel.Cursor.Sheet;

    // --- 시트 탭 (12.2) ---

    private void BuildTabs()
    {
        TabPanel.Children.Clear();
        for (int i = 0; i < _book.Sheets.Count; i++)
        {
            int index = i;
            if (_renamingSheet == index) { TabPanel.Children.Add(RenameBox(index)); continue; }

            var tab = new Button
            {
                Content = _book.Sheets[i].Name,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0),
                BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = PaletteBrushes.Of("#D0D4DA"),
                Background = i == SheetIndex ? Brushes.White : PaletteBrushes.Of("#EFF1F4"),
                Foreground = PaletteBrushes.Of(i == SheetIndex ? "#15181D" : "#586070"),
                FontSize = 11,
                Focusable = false,
                ContextMenu = SheetMenu(index),
            };
            tab.Click += (_, _) => { Sel.SetSheet(index); GridView.Raise(); GridView.Focus(); };
            TabPanel.Children.Add(tab);
        }

        if (_book.Sheets.Count < Workbook.MaxSheets && _renamingSheet is null)
        {
            var add = new Button
            {
                Content = "+", Padding = new Thickness(8, 0, 8, 0), FontSize = 11, Focusable = false,
                BorderThickness = new Thickness(0), Background = PaletteBrushes.Of("#EFF1F4"),
                Foreground = PaletteBrushes.Of("#586070"),
                ToolTip = "시트 추가",
            };
            add.Click += (_, _) => { _book.AddSheet(); Touch(); BuildTabs(); };
            TabPanel.Children.Add(add);
        }
    }

    /// <summary>12.2 의 이름 변경과 삭제. 창에 대화상자를 띄우지 않으려고 탭 자리에서 바로 고친다.</summary>
    private ContextMenu SheetMenu(int index)
    {
        var menu = new ContextMenu();

        var rename = new MenuItem { Header = "이름 바꾸기" };
        rename.Click += (_, _) => { _renamingSheet = index; BuildTabs(); };
        menu.Items.Add(rename);

        var remove = new MenuItem { Header = "시트 지우기", IsEnabled = _book.Sheets.Count > 1 };
        remove.Click += (_, _) => RemoveSheet(index);
        menu.Items.Add(remove);

        return menu;
    }

    private TextBox RenameBox(int index)
    {
        var box = new TextBox
        {
            Text = _book.Sheets[index].Name,
            Width = 90, FontSize = 11,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderBrush = PaletteBrushes.Of("#0B48C8"), BorderThickness = new Thickness(1),
        };
        bool done = false;

        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            string name = box.Text.Trim();
            _renamingSheet = null;
            if (keep && name.Length > 0 && name != _book.Sheets[index].Name)
            {
                try { _book.RenameSheet(index, name); Touch(); }
                catch (InvalidOperationException e) { ShowTabError(e.Message); }
            }
            BuildTabs();
            GridView.Focus();
        }

        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        box.LostKeyboardFocus += (_, _) => Finish(keep: true);
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.ImeProcessed) return;          // 10.2: 한글 조합은 입력기가 맡는다
            if (e.Key == Key.Enter)  { Finish(keep: true);  e.Handled = true; }
            if (e.Key == Key.Escape) { Finish(keep: false); e.Handled = true; }
        };
        return box;
    }

    private void ShowTabError(string message) =>
        MessageBox.Show(this, message, "DesktopSheet", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>
    /// 시트를 지우면 그 안의 값이 사라지는데 되돌리기가 칸 단위라 이것만은 되살리지 못한다.
    /// 그래서 값이 든 시트를 지울 때만 한 번 묻는다.
    /// </summary>
    private void RemoveSheet(int index)
    {
        bool hasValues = _book.Sheets[index].Cells.Any(c => !c.Value.IsEmpty);
        if (hasValues)
        {
            var answer = MessageBox.Show(this,
                $"시트 '{_book.Sheets[index].Name}' 을 지웁니다. 되돌리기로는 되살릴 수 없습니다.",
                "DesktopSheet", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) return;
        }

        try { _book.RemoveSheet(index); }
        catch (InvalidOperationException e) { ShowTabError(e.Message); return; }

        Sel.SetSheet(Math.Clamp(SheetIndex, 0, _book.Sheets.Count - 1));
        Touch();
        BuildTabs();
        GridView.Raise();
    }

    private void TabBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        DragMove();
    }

    // --- 편집 (10.1, 10.2) ---
    //
    // 편집기는 늘 커서 칸 위에 있고 포커스를 놓지 않는다. 글자를 칠 때 만들어 포커스를 넘기면,
    // 입력기는 이미 그리드에 조합을 걸어 둔 뒤라 첫 글자가 한 박자씩 밀린다.
    // 값을 치기 전에는 테두리도 글자색도 없어 안 보이고, 첫 글자가 들어오면 그때 모양을 갖춘다.

    private void PositionEditor()
    {
        Rect r = GridView.CellRect(Sel.Cursor.Row, Sel.Cursor.Col);
        Editor.Margin = new Thickness(r.Left, r.Top, 0, 0);
        // 장평을 걸었으면 편집기에도 같은 배율을 걸어야 편집으로 들어갈 때 글자가 튀지 않는다.
        Editor.RenderTransform = SheetGrid.Squeeze == 1.0
            ? Transform.Identity
            : new ScaleTransform(SheetGrid.Squeeze, 1);
        Editor.Width = r.Width / SheetGrid.Squeeze;
        Editor.Height = r.Height;
        Editor.FontSize = SheetGrid.FontSize;
    }

    private void FocusEditor()
    {
        PositionEditor();
        if (!Editor.IsKeyboardFocused) Editor.Focus();
    }

    private void SetEditorText(string text)
    {
        _suppressTextChanged = true;
        Editor.Text = text;
        Editor.CaretIndex = text.Length;
        _suppressTextChanged = false;
    }

    /// <summary>값을 치기 전. 칸 위에 있지만 보이지 않고 마우스도 지나쳐 간다.</summary>
    private void ShowIdle()
    {
        _editing = false;
        Editor.BorderThickness = new Thickness(0);
        Editor.Padding = new Thickness(SheetGrid.CellPadding, 0, SheetGrid.CellPadding, 0);
        Editor.Background = Brushes.Transparent;
        Editor.Foreground = Brushes.Transparent;
        Editor.CaretBrush = Brushes.Transparent;
        Editor.IsHitTestVisible = false;
        SetEditorText("");
    }

    /// <summary>글자가 들어온 뒤. 테두리와 글자색을 갖춘다.</summary>
    private void ShowEditing()
    {
        _editing = true;
        Editor.BorderThickness = new Thickness(2);
        // 테두리가 생긴 만큼 안쪽 여백을 줄인다. 그러지 않으면 편집으로 들어갈 때 글자가 2px 밀린다.
        Editor.Padding = new Thickness(SheetGrid.CellPadding - 2, 0, SheetGrid.CellPadding - 2, 0);
        Editor.Background = Brushes.White;
        Editor.Foreground = PaletteBrushes.Of(Palette.DefaultInk);
        Editor.CaretBrush = PaletteBrushes.Of(Palette.DefaultInk);
        Editor.IsHitTestVisible = true;
    }

    /// <summary>F2 와 더블클릭. 칸에 있던 글자를 불러 놓고 편집으로 들어간다.</summary>
    private void BeginEdit()
    {
        PositionEditor();
        ShowEditing();
        SetEditorText(_book.FindCell(Sel.Cursor)?.Raw ?? "");
        Editor.Focus();
    }

    /// <summary>입력기가 편집기에 첫 글자를 넣은 순간이다. 글자는 이미 들어와 있으므로 모양만 갖춘다.</summary>
    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged || _editing || Editor.Text.Length == 0) return;
        ShowEditing();
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 10.2: 한글을 조합하는 중에는 입력기가 키를 가져간다. Enter 는 조합만 끝내고 커서는 그 칸에 남는다.
        if (e.Key == Key.ImeProcessed) return;
        if (!_editing) return;

        switch (e.Key)
        {
            case Key.Enter:
                CommitEdit((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? MoveDirection.Up : MoveDirection.Down);
                e.Handled = true;
                break;
            case Key.Tab:
                CommitEdit((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? MoveDirection.Left : MoveDirection.Right);
                e.Handled = true;
                break;
            case Key.Escape:
                CancelEdit();
                e.Handled = true;
                break;
        }
    }

    private void CommitEdit(MoveDirection dir, bool move = true)
    {
        if (!_editing) return;
        string text = Editor.Text;
        CellAddress at = Sel.Cursor;
        ShowIdle();

        UndoStep step = UndoStep.Begin(_book, new[] { at });
        _book.SetInput(at, text);
        _undo.Push(step.Commit(_book));
        Touch();

        if (move) Sel.Advance(dir);
        GridView.Raise();
        FocusEditor();
    }

    private void CancelEdit()
    {
        if (!_editing) return;
        ShowIdle();
        GridView.InvalidateVisual();
        FocusEditor();
    }

    // --- 키 (10.1, 10.3, 10.5, 15.1, 15.5) ---

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_editing) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        // 15.1: 음영은 Ctrl+Shift+숫자, 글자색은 Alt+Shift+숫자
        if (shift && (ctrl || alt) && TryDigit(key, out int digit))
        {
            PaletteEntry entry = Palette.Entries.First(p => p.Key == digit);
            ApplyToSelection(at => { if (ctrl) _book.SetShade(at, entry.Name); else _book.SetInk(at, entry.Name); });
            e.Handled = true;
            return;
        }

        if (ctrl && shift && key == Key.Space)          // 15.5: 서식 지우기
        {
            ApplyToSelection(_book.ClearFormat);
            e.Handled = true;
            return;
        }

        if (ctrl)
        {
            switch (key)
            {
                case Key.C: Copy(); e.Handled = true; return;
                case Key.X: Copy(cut: true); e.Handled = true; return;
                case Key.V: Paste(valuesOnly: shift); e.Handled = true; return;
                case Key.Z: if (_undo.Undo(_book)) { Touch(); GridView.Raise(); } e.Handled = true; return;
                case Key.Y: if (_undo.Redo(_book)) { Touch(); GridView.Raise(); } e.Handled = true; return;
                case Key.A: Sel.SelectAll(); GridView.Raise(); e.Handled = true; return;
                case Key.Up:    Sel.Jump(MoveDirection.Up, _book); GridView.Raise(); e.Handled = true; return;
                case Key.Down:  Sel.Jump(MoveDirection.Down, _book); GridView.Raise(); e.Handled = true; return;
                case Key.Left:  Sel.Jump(MoveDirection.Left, _book); GridView.Raise(); e.Handled = true; return;
                case Key.Right: Sel.Jump(MoveDirection.Right, _book); GridView.Raise(); e.Handled = true; return;
            }
        }

        switch (key)
        {
            case Key.Up:    Sel.Move(MoveDirection.Up, shift); break;
            case Key.Down:  Sel.Move(MoveDirection.Down, shift); break;
            case Key.Left:  Sel.Move(MoveDirection.Left, shift); break;
            case Key.Right: Sel.Move(MoveDirection.Right, shift); break;
            case Key.Enter: Sel.Advance(shift ? MoveDirection.Up : MoveDirection.Down); break;
            case Key.Tab:   Sel.Advance(shift ? MoveDirection.Left : MoveDirection.Right); break;
            case Key.F2:    BeginEdit(); e.Handled = true; return;
            case Key.Delete: ClearValues(); e.Handled = true; return;
            case Key.Escape: GridView.CopyMarquee = null; _copySource = null; GridView.InvalidateVisual(); e.Handled = true; return;
            case Key.Back:  BeginEdit(); SetEditorText(""); e.Handled = true; return;
            case Key.PageDown: GridView.ScrollBy(GridView.VisibleRows, 0); e.Handled = true; return;
            case Key.PageUp:   GridView.ScrollBy(-GridView.VisibleRows, 0); e.Handled = true; return;
            default: return;
        }

        GridView.Raise();
        e.Handled = true;
    }

    private static bool TryDigit(Key key, out int digit)
    {
        if (key is >= Key.D0 and <= Key.D9) { digit = key - Key.D0; return true; }
        if (key is >= Key.NumPad0 and <= Key.NumPad9) { digit = key - Key.NumPad0; return true; }
        digit = -1;
        return false;
    }

    private IEnumerable<CellAddress> SelectedCells()
    {
        CellRange r = Sel.Range;
        for (int row = r.Top; row <= r.Bottom; row++)
            for (int col = r.Left; col <= r.Right; col++)
                yield return new CellAddress(r.Sheet, row, col);
    }

    private void ApplyToSelection(Action<CellAddress> action)
    {
        CellAddress[] cells = SelectedCells().ToArray();
        UndoStep step = UndoStep.Begin(_book, cells);
        foreach (CellAddress at in cells) action(at);
        _undo.Push(step.Commit(_book));
        Touch();
        GridView.InvalidateVisual();
    }

    private void ClearValues() => ApplyToSelection(_book.ClearValue);

    // --- 클립보드 (10.3, 10.4) ---

    private void Copy(bool cut = false)
    {
        _copySource = Sel.Range;
        _copyClipboardText = DelimitedText.Write(_book, Sel.Range, DelimitedText.Tab);
        try { Clipboard.SetText(_copyClipboardText); } catch (System.Runtime.InteropServices.COMException) { }

        GridView.CopyMarquee = Sel.Range;
        if (cut) { ClearValues(); _copySource = null; GridView.CopyMarquee = null; }
        GridView.InvalidateVisual();
    }

    private void Paste(bool valuesOnly)
    {
        string clipboard = "";
        try { clipboard = Clipboard.GetText(); } catch (System.Runtime.InteropServices.COMException) { }

        // 밖에서 온 것이면 탭 구분 텍스트로 읽는다(10.4). 우리가 복사한 것이면 수식을 옮긴다(10.3).
        bool fromOutside = _copySource is null || clipboard != _copyClipboardText;
        if (fromOutside) PasteText(clipboard);
        else PasteCells(_copySource!.Value, valuesOnly);

        Touch();
        GridView.Raise();
    }

    private void PasteText(string text)
    {
        if (text.Length == 0) return;
        List<List<string>> rows = DelimitedText.Read(text, DelimitedText.Tab);
        CellAddress anchor = Sel.Cursor;

        var cells = new List<CellAddress>();
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < rows[r].Count; c++)
                if (Sheet.InRange(anchor.Row + r, anchor.Col + c))
                    cells.Add(new CellAddress(anchor.Sheet, anchor.Row + r, anchor.Col + c));

        UndoStep step = UndoStep.Begin(_book, cells);
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < rows[r].Count; c++)
            {
                int row = anchor.Row + r, col = anchor.Col + c;
                if (!Sheet.InRange(row, col)) continue;                 // 시트 경계를 넘는 만큼은 버린다
                _book.SetInput(new CellAddress(anchor.Sheet, row, col), rows[r][c], fromPaste: true);
            }
        _undo.Push(step.Commit(_book));
    }

    private void PasteCells(CellRange source, bool valuesOnly)
    {
        CellAddress anchor = Sel.Cursor;
        int dRow = anchor.Row - source.Top, dCol = anchor.Col - source.Left;

        // 한 칸을 복사해 여러 칸에 붙이면 고른 영역을 그 값으로 채운다(10.3).
        int repeatRows = source.IsSingle ? Sel.Range.Rows : 1;
        int repeatCols = source.IsSingle ? Sel.Range.Cols : 1;

        var targets = new List<CellAddress>();
        for (int rr = 0; rr < repeatRows; rr++)
            for (int cc = 0; cc < repeatCols; cc++)
                for (int r = source.Top; r <= source.Bottom; r++)
                    for (int c = source.Left; c <= source.Right; c++)
                    {
                        int row = r + dRow + rr, col = c + dCol + cc;
                        if (Sheet.InRange(row, col)) targets.Add(new CellAddress(anchor.Sheet, row, col));
                    }

        UndoStep step = UndoStep.Begin(_book, targets);

        for (int rr = 0; rr < repeatRows; rr++)
            for (int cc = 0; cc < repeatCols; cc++)
                for (int r = source.Top; r <= source.Bottom; r++)
                    for (int c = source.Left; c <= source.Right; c++)
                    {
                        int row = r + dRow + rr, col = c + dCol + cc;
                        if (!Sheet.InRange(row, col)) continue;

                        var from = new CellAddress(source.Sheet, r, c);
                        var to = new CellAddress(anchor.Sheet, row, col);
                        Cell? src = _book.FindCell(from);
                        string raw = src?.Raw ?? "";

                        if (valuesOnly)                                  // Ctrl+Shift+V: 결과를 값으로 굳힌다
                            raw = DelimitedText.CellText(_book, from);
                        else if (raw.StartsWith('='))
                            raw = FormulaRewriter.Shift(raw, row - r, col - c);

                        _book.SetInput(to, raw);
                    }

        _undo.Push(step.Commit(_book));
    }

    // --- 툴팁 (9장) ---

    private void GridView_MouseMove(object sender, MouseEventArgs e)
    {
        Point p = e.GetPosition(GridView);
        if (p.X < SheetGrid.RowHeaderWidth || p.Y < SheetGrid.ColHeaderHeight) { HideTip(); return; }

        int row = GridView.TopRow + (int)((p.Y - SheetGrid.ColHeaderHeight) / SheetGrid.RowHeight);
        int col = GridView.LeftCol + (int)((p.X - SheetGrid.RowHeaderWidth) / SheetGrid.CellWidth);
        if (!Sheet.InRange(row, col)) { HideTip(); return; }

        var at = new CellAddress(SheetIndex, row, col);
        if (_tipCell == at) return;
        _tipCell = at;

        string? full = TipText(at);
        if (full is null) { HideTip(); return; }

        _cellTip.Content = full;
        _cellTip.PlacementTarget = GridView;
        _cellTip.IsOpen = true;
    }

    /// <summary>9장. 표시가 원본과 다른 칸에만 띄운다.</summary>
    private string? TipText(CellAddress at)
    {
        Cell? cell = _book.FindCell(at);
        Value v = _book.Read(at);
        if (v.Kind == ValueKind.Blank) return null;

        (string shown, _) = SheetGrid.Render(cell, v);
        string full = v.Kind == ValueKind.Number
            ? v.Number.ToString("R", CultureInfo.InvariantCulture)
            : v.ToString();

        bool isFormula = cell?.Formula is not null;
        if (!isFormula && shown == full) return null;
        return isFormula ? $"{cell!.Raw}\n{full}" : full;
    }

    private void HideTip()
    {
        _tipCell = null;
        _cellTip.IsOpen = false;
    }

    // --- 우클릭 메뉴 (13.5, 15.1, 15.4, 15.5) ---

    private void GridView_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        menu.Items.Add(ColorMenu("셀 음영", shade: true));
        menu.Items.Add(ColorMenu("글자색", shade: false));
        menu.Items.Add(new Separator());

        var format = new MenuItem { Header = "숫자 서식…" };
        foreach (string code in new[] { "일반", "0", "0.00", "#,##0", "0.00%", "yyyy-mm-dd" })
        {
            string c = code == "일반" ? "" : code;
            var item = new MenuItem { Header = code };
            item.Click += (_, _) => ApplyToSelection(at => _book.SetFormat(at, c));
            format.Items.Add(item);
        }
        menu.Items.Add(format);

        var clear = new MenuItem { Header = "서식 지우기" };
        clear.Click += (_, _) => ApplyToSelection(_book.ClearFormat);
        menu.Items.Add(clear);
        menu.Items.Add(new Separator());

        var csv = new MenuItem { Header = "이 시트를 CSV로 내보내기" };
        csv.Click += (_, _) => ExportCsv();
        menu.Items.Add(csv);

        var folder = new MenuItem { Header = "저장 폴더 열기" };
        folder.Click += (_, _) => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(_store.Directory) { UseShellExecute = true });
        menu.Items.Add(folder);
        menu.Items.Add(new Separator());

        var help = new MenuItem { Header = "단축키 도움말" };
        help.Click += (_, _) => ShowHelp();
        menu.Items.Add(help);

        menu.Closed += (_, _) => FocusEditor();   // 메뉴가 가져간 포커스를 돌려준다
        menu.IsOpen = true;
    }

    private MenuItem ColorMenu(string header, bool shade)
    {
        var root = new MenuItem { Header = header };
        foreach (PaletteEntry entry in Palette.Entries)
        {
            var swatch = new System.Windows.Shapes.Rectangle
            {
                Width = 12, Height = 12,
                Fill = PaletteBrushes.Of(shade ? entry.Shade : entry.Ink),
                Stroke = PaletteBrushes.Of("#D0D4DA"), StrokeThickness = 1,
            };
            var item = new MenuItem { Header = entry.Name, Icon = swatch };
            PaletteEntry captured = entry;
            item.Click += (_, _) => ApplyToSelection(at =>
            {
                if (shade) _book.SetShade(at, captured.Name);
                else _book.SetInk(at, captured.Name);
            });
            root.Items.Add(item);
        }
        return root;
    }

    private void ShowHelp()
    {
        if (_help is { IsLoaded: true }) { _help.Activate(); return; }
        _help = new HelpWindow { Owner = this };
        _help.Closed += (_, _) => _help = null;
        _help.Show();
    }

    private void ExportCsv()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = _book.Sheets[SheetIndex].Name + ".csv",
            Filter = "CSV 파일 (*.csv)|*.csv",
        };
        if (dialog.ShowDialog() != true) return;

        var all = new CellRange(SheetIndex, 0, 0, Sheet.Rows - 1, Sheet.Cols - 1);
        string csv = DelimitedText.Write(_book, all, DelimitedText.Comma);
        // 13.5: BOM 이 없으면 엑셀이 한글을 깨뜨려 읽는다
        System.IO.File.WriteAllText(dialog.FileName, csv, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    // --- 저장 (13.2) ---

    private void Touch() => _scheduler.Touch(DateTime.Now);

    private void SaveIfDue()
    {
        if (!_scheduler.ShouldSave(DateTime.Now)) return;
        SaveNow();
    }

    public void SaveNow()
    {
        if (!_scheduler.IsDirty) return;
        CaptureWindowState();
        try { _store.Save(_book, _windowState); _scheduler.Saved(); }
        catch (System.IO.IOException) { /* 다음 차례에 다시 쓴다 */ }
    }

    private void CaptureWindowState()
    {
        if (System.Windows.WindowState.Normal == base.WindowState)
        {
            _windowState.X = Left;
            _windowState.Y = Top;
            _windowState.Width = Width;
            _windowState.Height = Height;
        }
        _windowState.Sheet = SheetIndex;
        _windowState.Row = Sel.Cursor.Row;
        _windowState.Col = Sel.Cursor.Col;
    }

    /// <summary>13.2. 윈도우가 꺼지거나 트레이에서 종료할 때 한 번 더 쓴다.</summary>
    public void FlushBeforeExit()
    {
        _exiting = true;
        _saveTimer.Stop();
        _scheduler.Touch(DateTime.Now);
        SaveNow();
    }
}
