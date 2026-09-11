using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DesktopSheet.Core;

/// <summary>
/// 사양서 10.4 의 클립보드와 13.5 의 CSV. 둘 다 화면에 보이는 문자열이 아니라 저장된 값을 싣는다.
/// 1.2345E+08 이나 ########## 을 실으면 받는 쪽에서 쓸모가 없기 때문이다.
/// </summary>
public static class DelimitedText
{
    public const char Tab = '\t';
    public const char Comma = ',';

    public static string Write(Workbook book, CellRange range, char separator)
    {
        var sb = new StringBuilder();
        for (int r = range.Top; r <= range.Bottom; r++)
        {
            for (int c = range.Left; c <= range.Right; c++)
            {
                if (c > range.Left) sb.Append(separator);
                sb.Append(Escape(CellText(book, new CellAddress(range.Sheet, r, c)), separator));
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>수식은 계산 결과를, 날짜는 일련번호가 아니라 yyyy-mm-dd 를 쓴다(13.5).</summary>
    public static string CellText(Workbook book, CellAddress at)
    {
        Cell? cell = book.FindCell(at);
        Value v = book.Read(at);
        switch (v.Kind)
        {
            case ValueKind.Blank: return "";
            case ValueKind.Text:  return v.Text;
            case ValueKind.Bool:  return v.Number != 0 ? "TRUE" : "FALSE";
            case ValueKind.Error: return CellErrorText.Of(v.Error);
            default:
                if (cell is not null && NumberFormat.IsDateSection(cell.FormatCode))
                {
                    int s = (int)Math.Floor(v.Number);
                    if (s is >= SerialDate.MinSerial and <= SerialDate.MaxSerial)
                        return SerialDate.ToDate(s).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                return v.Number.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    private static string Escape(string s, char separator)
    {
        if (s.IndexOf(separator) < 0 && s.IndexOf('"') < 0 && s.IndexOf('\n') < 0 && s.IndexOf('\r') < 0)
            return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>받을 때는 구분자를 열, 줄바꿈을 행으로 나눈다(10.4).</summary>
    public static List<List<string>> Read(string text, char separator)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(ch);
                continue;
            }

            if (ch == '"' && field.Length == 0) { quoted = true; continue; }
            if (ch == separator) { row.Add(field.ToString()); field.Clear(); continue; }
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = new List<string>();
                continue;
            }
            field.Append(ch);
        }

        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
