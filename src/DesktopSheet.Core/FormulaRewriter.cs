using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopSheet.Core;

/// <summary>
/// 식의 글자를 고치는 자리. 붙여넣을 때 참조를 밀고(10.3), 시트 이름이 바뀌면 식 안의 이름도 따라 바꾼다(12.2).
/// 토막을 다시 이어 붙이므로 공백은 사라지는데, 8.3 이 어차피 공백을 지운다.
/// </summary>
public static class FormulaRewriter
{
    /// <summary>10.3. 상대 참조만 민다. 절대 참조는 고정이고 시트 밖으로 나가면 #REF! 가 된다.</summary>
    public static string Shift(string formula, int deltaRow, int deltaCol) =>
        Rewrite(formula, r =>
        {
            CellRef moved = r.Offset(deltaRow, deltaCol);
            return moved.IsValid ? moved.ToA1() : CellErrorText.Of(CellError.Reference);
        });

    /// <summary>12.2. 시트 이름이 바뀌면 그 이름을 쓰던 식도 함께 고친다.</summary>
    public static string RenameSheet(string formula, string oldName, string newName) =>
        Rewrite(formula, r =>
            string.Equals(r.SheetName, oldName, StringComparison.OrdinalIgnoreCase)
                ? (r with { SheetName = newName }).ToA1()
                : r.ToA1());

    private static string Rewrite(string formula, Func<CellRef, string> map)
    {
        if (!formula.StartsWith('=')) return formula;

        List<Token> tokens;
        try { tokens = Tokenizer.Scan(formula); }
        catch (FormulaException) { return formula; }   // 읽지 못하는 식은 그대로 둔다

        var sb = new StringBuilder("=");
        foreach (Token t in tokens)
        {
            if (t.Kind == TokenKind.End) break;
            switch (t.Kind)
            {
                case TokenKind.Ref when CellRef.TryParse(t.Lexeme, out CellRef r):
                    sb.Append(map(r));
                    break;
                case TokenKind.Text:
                    sb.Append('"').Append(t.Lexeme.Replace("\"", "\"\"")).Append('"');
                    break;
                default:
                    sb.Append(t.Lexeme);
                    break;
            }
        }
        return sb.ToString();
    }
}
