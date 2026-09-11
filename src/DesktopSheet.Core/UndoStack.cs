using System;
using System.Collections.Generic;

namespace DesktopSheet.Core;

/// <summary>칸 하나를 되돌리는 데 필요한 전부. 값과 수식은 Raw 에, 서식은 나머지에 들어 있다.</summary>
public readonly record struct CellSnapshot(string Raw, string FormatCode, string? Shade, string? Ink);

/// <summary>한 동작이 바꾼 칸들. 붙여넣기 한 번과 지우기 한 번이 각각 한 단계다(10.5).</summary>
public sealed class UndoStep
{
    private readonly List<(CellAddress At, CellSnapshot Before, CellSnapshot After)> _changes = new();

    public static UndoStep Begin(Workbook book, IEnumerable<CellAddress> touched)
    {
        var step = new UndoStep();
        foreach (CellAddress a in touched) step._changes.Add((a, book.Snapshot(a), default));
        return step;
    }

    public UndoStep Commit(Workbook book)
    {
        for (int i = 0; i < _changes.Count; i++)
            _changes[i] = (_changes[i].At, _changes[i].Before, book.Snapshot(_changes[i].At));
        return this;
    }

    public bool ChangedAnything()
    {
        foreach (var c in _changes) if (!c.Before.Equals(c.After)) return true;
        return false;
    }

    internal void Undo(Workbook book) { foreach (var c in _changes) book.Restore(c.At, c.Before); }
    internal void Redo(Workbook book) { foreach (var c in _changes) book.Restore(c.At, c.After); }
}

/// <summary>
/// 사양서 10.5. Ctrl+Z 로 되돌리고 Ctrl+Y 로 다시 실행하며 20단계를 기억한다.
/// 저장 절차가 없어 이것이 실수를 되돌리는 유일한 수단이다.
/// </summary>
public sealed class UndoStack
{
    public const int MaxSteps = 20;

    private readonly List<UndoStep> _steps = new();
    private int _at;   // 다음에 되돌릴 자리. 0 이면 되돌릴 것이 없다.

    public int Count => _steps.Count;
    public bool CanUndo => _at > 0;
    public bool CanRedo => _at < _steps.Count;

    public void Push(UndoStep step)
    {
        if (!step.ChangedAnything()) return;
        _steps.RemoveRange(_at, _steps.Count - _at);       // 되돌린 뒤 새로 고치면 앞의 것은 버린다
        _steps.Add(step);
        if (_steps.Count > MaxSteps) _steps.RemoveAt(0);
        _at = _steps.Count;
    }

    public bool Undo(Workbook book)
    {
        if (!CanUndo) return false;
        _steps[--_at].Undo(book);
        return true;
    }

    public bool Redo(Workbook book)
    {
        if (!CanRedo) return false;
        _steps[_at++].Redo(book);
        return true;
    }
}
