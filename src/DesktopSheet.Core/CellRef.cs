using System;
using System.Globalization;

namespace DesktopSheet.Core;

/// <summary>시트 안의 한 칸을 가리키는 자리. 행과 열은 0 부터 센다.</summary>
public readonly record struct CellAddress(int Sheet, int Row, int Col);

/// <summary>
/// 사양서 8.3 의 셀 참조. 열은 A~Z 한 글자, 행은 1~200 이다(12.1).
/// 절대 참조 $A$1 과 혼합 참조 $A1, A$1 을 구분해 들고 있어야 붙여넣을 때 밀 자리를 안다(10.3).
/// </summary>
public readonly record struct CellRef(string? SheetName, int Row, int Col, bool RowAbs, bool ColAbs)
{
    public const int MaxRows = 200;
    public const int MaxCols = 26;

    public bool IsValid => Row >= 0 && Row < MaxRows && Col >= 0 && Col < MaxCols;

    public static bool TryParse(string text, out CellRef result)
    {
        result = default;
        if (string.IsNullOrEmpty(text)) return false;

        string? sheet = null;
        int bang = text.LastIndexOf('!');
        if (bang >= 0)
        {
            sheet = text[..bang].Trim();
            if (sheet.Length >= 2 && sheet[0] == '\'' && sheet[^1] == '\'')
                sheet = sheet[1..^1].Replace("''", "'");
            if (sheet.Length == 0) return false;
            text = text[(bang + 1)..];
        }

        int i = 0;
        bool colAbs = i < text.Length && text[i] == '$';
        if (colAbs) i++;
        if (i >= text.Length || !char.IsAsciiLetter(text[i])) return false;
        int col = char.ToUpperInvariant(text[i]) - 'A';
        i++;

        bool rowAbs = i < text.Length && text[i] == '$';
        if (rowAbs) i++;
        int start = i;
        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        if (i == start || i != text.Length) return false;

        if (!int.TryParse(text[start..i], NumberStyles.None, CultureInfo.InvariantCulture, out int rowNumber)
            || rowNumber < 1) return false;

        result = new CellRef(sheet, rowNumber - 1, col, rowAbs, colAbs);
        return true;
    }

    /// <summary>10.3. 붙여넣을 때 상대 참조만 민다. 절대 참조는 고정이다.</summary>
    public CellRef Offset(int deltaRow, int deltaCol) =>
        this with { Row = RowAbs ? Row : Row + deltaRow, Col = ColAbs ? Col : Col + deltaCol };

    public string ToA1()
    {
        string body = (ColAbs ? "$" : "") + (char)('A' + Col) + (RowAbs ? "$" : "") + (Row + 1).ToString(CultureInfo.InvariantCulture);
        if (SheetName is null) return body;
        string name = SheetName.Contains(' ') ? "'" + SheetName.Replace("'", "''") + "'" : SheetName;
        return name + "!" + body;
    }

    public override string ToString() => ToA1();
}
