using System;

namespace DesktopSheet.Core;

/// <summary>
/// 사양서 10.6. 식을 치는 중에 칸을 클릭하면 그 자리에 참조 글자가 들어간다.
/// 글자를 어떻게 끼우는지만 여기서 정하고, 어느 칸을 클릭했는지는 화면 쪽에서 넘겨 준다.
/// </summary>
public static class FormulaPoint
{
    /// <summary>참조가 들어갈 수 있는 자리는 연산자와 여는 괄호, 콤마, 콜론, 등호 다음이다.</summary>
    private const string Openers = "=+-*/^(,:<>";

    /// <summary>
    /// 커서 자리에 참조를 끼울 수 있는가. 식이어야 하고, 커서 앞이 연산자 자리여야 한다.
    /// `=A1` 뒤에서 끼우면 `=A1B3` 이 되므로 숫자나 글자 다음에는 끼우지 않는다.
    /// </summary>
    public static bool Accepts(string text, int caret)
    {
        if (text.Length == 0 || text[0] != '=') return false;
        if (caret < 1 || caret > text.Length) return false;
        if (InsideText(text, caret)) return false;

        int i = caret - 1;
        while (i >= 0 && text[i] == ' ') i--;
        return i >= 0 && Openers.IndexOf(text[i]) >= 0;
    }

    /// <summary>
    /// 참조 글자를 넣은 결과와 그 글자가 식 안에서 차지한 자리를 돌려준다.
    /// start 가 0 이상이면 아까 찍어 둔 참조를 갈아치우고, 음수면 커서 자리에 끼운다.
    /// </summary>
    public static (string Text, int Start, int Length) Put(
        string text, int caret, int start, int length, string reference)
    {
        if (start >= 0 && length >= 0 && start + length <= text.Length)
            return (text[..start] + reference + text[(start + length)..], start, reference.Length);

        int at = Math.Clamp(caret, 0, text.Length);
        return (text[..at] + reference + text[at..], at, reference.Length);
    }

    /// <summary>
    /// 방향키로 찍을 칸. 처음 누르면 식을 치는 칸에서 한 칸 떼고, 그다음부터는 찍어 둔 칸에서 한 칸씩 옮긴다.
    /// 시트 밖으로는 나가지 않는다(10.1).
    /// </summary>
    public static CellAddress Step(CellAddress at, MoveDirection dir)
    {
        int row = at.Row;
        int col = at.Col;
        switch (dir)
        {
            case MoveDirection.Up:    row--; break;
            case MoveDirection.Down:  row++; break;
            case MoveDirection.Left:  col--; break;
            default:                  col++; break;
        }
        // 이름이 from 이면 안 된다 - 뒤에 with 를 쓰면 컴파일러가 LINQ 질의로 읽는다.
        return at with { Row = Math.Clamp(row, 0, Sheet.Rows - 1), Col = Math.Clamp(col, 0, Sheet.Cols - 1) };
    }

    /// <summary>한 칸은 `A1`, 여러 칸은 `A1:B3` 으로 적는다(8.3).</summary>
    public static string Reference(CellRange range) =>
        range.IsSingle
            ? A1(range.Top, range.Left)
            : A1(range.Top, range.Left) + ":" + A1(range.Bottom, range.Right);

    private static string A1(int row, int col) => new CellRef(null, row, col, false, false).ToA1();

    /// <summary>문자열 "..." 안이면 참조가 아니라 글자일 뿐이다. 따옴표 두 개는 글자 하나다.</summary>
    private static bool InsideText(string text, int caret)
    {
        bool inside = false;
        for (int i = 1; i < caret && i < text.Length; i++)
        {
            if (text[i] != '"') continue;
            if (inside && i + 1 < caret && text[i + 1] == '"') { i++; continue; }
            inside = !inside;
        }
        return inside;
    }
}
