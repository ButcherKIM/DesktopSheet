using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopSheet.Core;

/// <summary>칸 하나. 서식은 값과 따로 셀에 붙박여 있다(10.3).</summary>
public sealed class Cell
{
    public string Raw { get; set; } = "";
    public Value Value { get; set; } = Value.Blank;
    public string FormatCode { get; set; } = "";
    public string? Shade { get; set; }
    public string? Ink { get; set; }
    public Node? Formula { get; set; }
    /// <summary>이 칸이 읽는 칸들의 평평한 번호(8.6). 값이 바뀌면 다시 모은다.</summary>
    public int[] Precedents { get; set; } = Array.Empty<int>();

    public bool IsEmpty => Raw.Length == 0 && Shade is null && Ink is null && FormatCode.Length == 0;
}

/// <summary>사양서 12.1. 시트 한 장은 200행 x 26열이다. 값이 든 칸만 들고 있는다.</summary>
public sealed class Sheet(string name)
{
    public const int Rows = CellRef.MaxRows;
    public const int Cols = CellRef.MaxCols;

    public string Name { get; set; } = name;
    private readonly Dictionary<int, Cell> _cells = new();

    public IEnumerable<KeyValuePair<int, Cell>> Cells => _cells;

    public static bool InRange(int row, int col) => row >= 0 && row < Rows && col >= 0 && col < Cols;

    public Cell? Find(int row, int col) =>
        _cells.TryGetValue(row * Cols + col, out Cell? c) ? c : null;

    public Cell GetOrCreate(int row, int col)
    {
        int k = row * Cols + col;
        if (!_cells.TryGetValue(k, out Cell? c)) _cells[k] = c = new Cell();
        return c;
    }

    public void Remove(int row, int col) => _cells.Remove(row * Cols + col);
}

/// <summary>
/// 사양서 12장. 시트 다섯 장이 상한이다.
/// 8.6 대로 값이 바뀐 칸과 그 뒤에 달린 칸만 다시 셈하고, 순환에 낀 칸에는 #CIRC! 를 찍는다.
/// </summary>
public sealed class Workbook
{
    public const int MaxSheets = 5;

    /// <summary>칸 하나에 번호 하나를 준다. 시트 다섯 장 x 200행 x 26열 = 26,000개다.</summary>
    private const int SheetStride = Sheet.Rows * Sheet.Cols;
    private const int Capacity = MaxSheets * SheetStride;

    private readonly List<Sheet> _sheets = new();
    private readonly List<Evaluator> _evaluators = new();      // 시트마다 하나씩 두고 돌려쓴다

    // 그래프는 해시 대신 평평한 배열로 든다. 칸 번호가 곧 자리라 훑는 값이 배열 읽기 한 번으로 끝난다.
    private readonly List<int>?[] _dependents = new List<int>?[Capacity];
    private readonly int[] _stamp = new int[Capacity];         // 이번 재계산에 든 칸인지 표시
    private readonly int[] _waiting = new int[Capacity];        // 아직 기다리는 선행의 수
    private int _epoch;

    private static int Id(CellAddress a) => a.Sheet * SheetStride + a.Row * Sheet.Cols + a.Col;
    private static int Id(int sheet, int row, int col) => sheet * SheetStride + row * Sheet.Cols + col;
    private static CellAddress FromId(int id) =>
        new(id / SheetStride, id % SheetStride / Sheet.Cols, id % Sheet.Cols);

    public Workbook(int sheets = 1)
    {
        for (int i = 0; i < Math.Clamp(sheets, 1, MaxSheets); i++) AddSheet();
    }

    public IReadOnlyList<Sheet> Sheets => _sheets;

    public Sheet AddSheet(string? name = null)
    {
        if (_sheets.Count >= MaxSheets) throw new InvalidOperationException($"시트는 {MaxSheets}장이 상한입니다");
        name ??= NextDefaultName();
        if (_sheets.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"시트 이름 {name} 이 이미 있습니다");
        var sheet = new Sheet(name);
        _sheets.Add(sheet);
        _evaluators.Add(new Evaluator(new SheetScopedSource(this, _sheets.Count - 1)));
        return sheet;
    }

    private string NextDefaultName()
    {
        for (int n = 1; ; n++)
        {
            string candidate = "Sheet" + n;
            if (!_sheets.Any(s => s.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
    }

    public int IndexOf(string sheetName) =>
        _sheets.FindIndex(s => s.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));

    public Value Read(CellAddress a)
    {
        if (a.Sheet < 0 || a.Sheet >= _sheets.Count || !Sheet.InRange(a.Row, a.Col)) return Value.Err(CellError.Reference);
        return _sheets[a.Sheet].Find(a.Row, a.Col)?.Value ?? Value.Blank;
    }

    /// <summary>7장의 입력 인식을 거쳐 칸에 넣고, 8.6 대로 달린 칸까지 다시 셈한다.</summary>
    public void SetInput(CellAddress at, string raw, bool fromPaste = false)
    {
        if (at.Sheet < 0 || at.Sheet >= _sheets.Count || !Sheet.InRange(at.Row, at.Col))
            throw new ArgumentOutOfRangeException(nameof(at));

        Cell cell = _sheets[at.Sheet].GetOrCreate(at.Row, at.Col);
        cell.Raw = raw ?? "";
        ClearPrecedents(at, cell);

        ParsedInput p = InputParser.Parse(cell.Raw, fromPaste);
        switch (p.Kind)
        {
            case InputKind.Formula:
                try
                {
                    cell.Formula = Parser.Parse(cell.Raw);
                    var pres = new HashSet<int>();
                    CollectPrecedents(at.Sheet, cell.Formula, pres);
                    cell.Precedents = pres.ToArray();
                    int me = Id(at);
                    foreach (int pre in cell.Precedents)
                        (_dependents[pre] ??= new List<int>()).Add(me);
                }
                catch (FormulaException e)
                {
                    cell.Formula = null;
                    cell.Value = Value.Err(e.Error);
                }
                break;

            case InputKind.Number:
                cell.Formula = null;
                cell.Value = Value.Num(p.Value);
                if (p.FormatCode.Length > 0) cell.FormatCode = p.FormatCode;
                break;

            case InputKind.Text:
                cell.Formula = null;
                cell.Value = Value.Str(p.Text);
                break;

            default:
                cell.Formula = null;
                cell.Value = Value.Blank;
                break;
        }

        Recalculate(at);
    }

    /// <summary>되돌리기가 쓸 수 있게 칸의 상태를 통째로 뜬다(10.5).</summary>
    public CellSnapshot Snapshot(CellAddress at)
    {
        Cell? cell = FindCell(at);
        return cell is null
            ? new CellSnapshot("", "", null, null)
            : new CellSnapshot(cell.Raw, cell.FormatCode, cell.Shade, cell.Ink);
    }

    /// <summary>뜬 상태를 그대로 되돌린다. 값을 먼저 넣고 서식을 덮는다 - 입력 인식이 붙인 서식을 지우기 위해서다.</summary>
    public void Restore(CellAddress at, CellSnapshot snap)
    {
        SetInput(at, snap.Raw);
        Cell cell = _sheets[at.Sheet].GetOrCreate(at.Row, at.Col);
        cell.FormatCode = snap.FormatCode;
        cell.Shade = snap.Shade;
        cell.Ink = snap.Ink;
    }

    public Cell? FindCell(CellAddress a) =>
        a.Sheet >= 0 && a.Sheet < _sheets.Count && Sheet.InRange(a.Row, a.Col)
            ? _sheets[a.Sheet].Find(a.Row, a.Col) : null;

    /// <summary>15.1 의 색 칠하기. 서식은 값과 따로 셀에 붙박여 있다(10.3).</summary>
    public void SetShade(CellAddress at, string? paletteName)
    {
        _sheets[at.Sheet].GetOrCreate(at.Row, at.Col).Shade = paletteName;
    }

    public void SetInk(CellAddress at, string? paletteName)
    {
        _sheets[at.Sheet].GetOrCreate(at.Row, at.Col).Ink = paletteName;
    }

    public void SetFormat(CellAddress at, string formatCode)
    {
        _sheets[at.Sheet].GetOrCreate(at.Row, at.Col).FormatCode = formatCode ?? "";
    }

    /// <summary>15.5 의 서식 지우기. 값과 수식은 건드리지 않는다.</summary>
    public void ClearFormat(CellAddress at)
    {
        Cell? cell = FindCell(at);
        if (cell is null) return;
        cell.Shade = null;
        cell.Ink = null;
        cell.FormatCode = "";
    }

    /// <summary>10.3 의 Delete. 값과 수식만 지우고 서식은 남긴다.</summary>
    public void ClearValue(CellAddress at)
    {
        Cell? cell = _sheets[at.Sheet].Find(at.Row, at.Col);
        if (cell is null) return;
        cell.Raw = "";
        cell.Formula = null;
        cell.Value = Value.Blank;
        ClearPrecedents(at, cell);
        Recalculate(at);
    }

    private void ClearPrecedents(CellAddress at, Cell cell)
    {
        int me = Id(at);
        foreach (int pre in cell.Precedents) _dependents[pre]?.Remove(me);
        cell.Precedents = Array.Empty<int>();
    }

    private void CollectPrecedents(int sheet, Node node, HashSet<int> into)
    {
        switch (node)
        {
            case RefNode r:
            {
                int s = SheetIndex(sheet, r.Ref.SheetName);
                if (s >= 0 && Sheet.InRange(r.Ref.Row, r.Ref.Col)) into.Add(Id(s, r.Ref.Row, r.Ref.Col));
                break;
            }
            case RangeNode g:
            {
                int r1 = Math.Min(g.From.Row, g.To.Row), r2 = Math.Max(g.From.Row, g.To.Row);
                int c1 = Math.Min(g.From.Col, g.To.Col), c2 = Math.Max(g.From.Col, g.To.Col);
                int s = SheetIndex(sheet, g.From.SheetName);
                if (s < 0) break;
                for (int r = Math.Max(0, r1); r <= Math.Min(Sheet.Rows - 1, r2); r++)
                    for (int c = Math.Max(0, c1); c <= Math.Min(Sheet.Cols - 1, c2); c++)
                        into.Add(Id(s, r, c));
                break;
            }
            case UnaryNode u: CollectPrecedents(sheet, u.Operand, into); break;
            case PercentNode p: CollectPrecedents(sheet, p.Operand, into); break;
            case BinaryNode b:
                CollectPrecedents(sheet, b.Left, into);
                CollectPrecedents(sheet, b.Right, into);
                break;
            case CallNode c:
                foreach (Node arg in c.Args) CollectPrecedents(sheet, arg, into);
                break;
        }
    }

    private int SheetIndex(int current, string? name) => name is null ? current : IndexOf(name);

    /// <summary>
    /// 8.6. 바뀐 칸과 그 뒤에 달린 칸만 모아 선행이 끝난 순서대로 셈한다.
    /// 칸 번호를 자리로 쓰는 배열 셋으로 훑으므로 해시를 타지 않고, 칸이 사슬로 이어져도 한 칸을 한 번씩만 건드린다.
    /// </summary>
    private void Recalculate(CellAddress changed)
    {
        _epoch++;
        var dirty = new List<int>(64);

        // 1. 바뀐 칸에 달린 칸을 모은다. _stamp 가 이번 회차 번호면 이미 담은 칸이다.
        var stack = new Stack<int>();
        stack.Push(Id(changed));
        while (stack.Count > 0)
        {
            int a = stack.Pop();
            if (_stamp[a] == _epoch) continue;
            _stamp[a] = _epoch;
            dirty.Add(a);
            List<int>? ds = _dependents[a];
            if (ds is null) continue;
            for (int i = 0; i < ds.Count; i++) stack.Push(ds[i]);
        }

        // 2. 모은 칸 안에서 각자 몇 개를 기다리는지 센다. 자기를 가리키는 칸은 스스로를 기다려 풀리지 않는다.
        var ready = new Queue<int>(dirty.Count);
        foreach (int a in dirty)
        {
            Cell? cell = FindCell(a);
            int n = 0;
            if (cell?.Formula is not null)
            {
                int[] pres = cell.Precedents;
                for (int i = 0; i < pres.Length; i++)
                    if (_stamp[pres[i]] == _epoch) n++;
            }
            _waiting[a] = n;
            if (n == 0) ready.Enqueue(a);
        }

        // 3. 기다릴 것이 없는 칸부터 셈하고, 그 칸을 기다리던 칸의 수를 하나씩 줄인다.
        int done = 0;
        while (ready.Count > 0)
        {
            int a = ready.Dequeue();
            Cell? cell = FindCell(a);
            if (cell?.Formula is not null) cell.Value = _evaluators[a / SheetStride].Evaluate(cell.Formula);
            done++;
            List<int>? ds = _dependents[a];
            if (ds is null) continue;
            for (int i = 0; i < ds.Count; i++)
            {
                int d = ds[i];
                if (_stamp[d] != _epoch) continue;
                if (--_waiting[d] == 0) ready.Enqueue(d);
            }
        }

        // 4. 끝까지 차례가 오지 않은 칸들은 서로를 물고 있다.
        if (done == dirty.Count) return;
        foreach (int a in dirty)
        {
            if (_waiting[a] <= 0) continue;
            Cell? cell = FindCell(a);
            if (cell is not null) cell.Value = Value.Err(CellError.Circular);
        }
    }

    private Cell? FindCell(int id)
    {
        int sheet = id / SheetStride;
        return sheet < _sheets.Count ? _sheets[sheet].Find(id % SheetStride / Sheet.Cols, id % Sheet.Cols) : null;
    }

    /// <summary>시트 이름을 적지 않은 주소는 식이 있는 시트의 칸을 뜻한다.</summary>
    private sealed class SheetScopedSource(Workbook book, int sheet) : ICellSource
    {
        public Value Read(CellRef r)
        {
            if (!r.IsValid) return Value.Err(CellError.Reference);
            int s = r.SheetName is null ? sheet : book.IndexOf(r.SheetName);
            if (s < 0) return Value.Err(CellError.Reference);
            return book.Read(new CellAddress(s, r.Row, r.Col));
        }
    }
}
