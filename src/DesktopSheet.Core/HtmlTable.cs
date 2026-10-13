using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DesktopSheet.Core;

/// <summary>
/// 사양서 10.4 의 HTML 표. 탭 구분 텍스트와 함께 클립보드에 실어 파워포인트와 워드에도 표로 붙게 한다.
/// 색은 싣지 않는다 - 색은 그 자리의 셀에 속한 것이라(10.3) 밖으로 넘기면 규칙이 둘로 갈린다.
/// </summary>
public static class HtmlTable
{
    /// <summary>윈도우 클립보드의 HTML 형식은 머리글에 조각이 시작하고 끝나는 바이트 자리를 적어야 한다.</summary>
    private const string Header =
        "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";

    private const string Open = "<html><body>\r\n<!--StartFragment-->";
    private const string Close = "<!--EndFragment-->\r\n</body></html>";

    private const string TableStyle =
        "border-collapse:collapse;font-family:'Nanum Gothic Coding',Consolas,monospace;font-size:12pt";

    /// <summary>클립보드에 실을 한 덩어리. 머리글과 조각 표시를 갖춘 CF_HTML 이다.</summary>
    public static string Write(Workbook book, CellRange range)
    {
        string fragment = Fragment(book, range);
        int startHtml = string.Format(CultureInfo.InvariantCulture, Header, 0, 0, 0, 0).Length;
        int startFragment = startHtml + Open.Length;
        int endFragment = startFragment + fragment.Length;
        int endHtml = endFragment + Close.Length;

        // 자리는 바이트로 세는데 몸통을 ASCII 로만 채워 두었으므로 글자 수가 곧 바이트 수다.
        // 한글을 그대로 실으면 한 자가 UTF-8 로 3바이트라 받는 쪽이 조각의 끝을 잘못 짚는다.
        return string.Format(CultureInfo.InvariantCulture, Header, startHtml, endHtml, startFragment, endFragment)
             + Open + fragment + Close;
    }

    /// <summary>표 뼈대만 만든다. 숫자는 오른쪽, 텍스트는 왼쪽에 붙여 화면과 같은 쪽에 세운다(6장).</summary>
    public static string Fragment(Workbook book, CellRange range)
    {
        var sb = new StringBuilder();
        sb.Append("<table border=\"1\" cellspacing=\"0\" cellpadding=\"2\" style=\"").Append(TableStyle).Append("\">");
        for (int r = range.Top; r <= range.Bottom; r++)
        {
            sb.Append("<tr>");
            for (int c = range.Left; c <= range.Right; c++)
            {
                var at = new CellAddress(range.Sheet, r, c);
                string text = DelimitedText.CellText(book, at);
                bool right = book.Read(at).Kind != ValueKind.Text;
                sb.Append("<td style=\"text-align:").Append(right ? "right" : "left").Append("\">");
                // 빈 칸을 <td></td> 로 두면 붙여넣는 쪽에서 칸이 접혀 표가 어긋난다.
                sb.Append(text.Length == 0 ? "&nbsp;" : Escape(text));
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if (ch is >= ' ' and <= '~') sb.Append(ch);
                    else sb.Append("&#x").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture)).Append(';');
                    break;
            }
        return sb.ToString();
    }

    /// <summary>10.4. 받은 HTML 에서 첫 표를 꺼내 행과 열로 돌려준다. 표가 없으면 빈 목록이다.</summary>
    public static List<List<string>> Read(string html)
    {
        if (string.IsNullOrEmpty(html)) return new List<List<string>>();
        int start = IndexOfTag(html, "table");
        if (start < 0) return new List<List<string>>();

        var reader = new Reader();
        int depth = 0;
        int i = start;

        while (i < html.Length)
        {
            if (html[i] != '<') { reader.Append(html[i]); i++; continue; }

            Tag tag = ReadTag(html, i);
            i = tag.End;
            if (tag.Name.Length == 0) continue;                       // 주석과 <!DOCTYPE>

            if (tag.Name is "style" or "script")
            {
                if (!tag.Closing) i = SkipTo(html, tag.Name, i);
                continue;
            }

            switch (tag.Name)
            {
                // 표 안에 든 표는 바깥 표의 한 칸으로 삼는다. 글자만 받아 적고 행과 열은 세지 않는다.
                case "table":
                    if (!tag.Closing) { depth++; break; }
                    if (--depth == 0) { reader.CloseCell(); return reader.Rows(); }
                    break;
                case "tr" when depth == 1:
                    if (!tag.Closing) { reader.CloseCell(); reader.StartRow(); }
                    break;
                case "td" or "th" when depth == 1:
                    if (tag.Closing) reader.CloseCell();
                    else reader.StartCell(Span(tag.Attributes, "colspan"), Span(tag.Attributes, "rowspan"));
                    break;
                case "br" or "p" or "div":
                    reader.Append(' ');                               // 한 칸은 한 줄이므로 줄바꿈은 공백 한 자다(6장)
                    break;
            }
        }

        reader.CloseCell();
        return reader.Rows();                                         // </table> 이 잘린 조각도 받아들인다
    }

    /// <summary>
    /// 칸을 자리에 놓아 가며 표를 읽는다. 합쳐진 칸(colspan, rowspan)이 차지한 자리를 기억해야
    /// 그다음 행의 칸이 왼쪽으로 밀리지 않는다.
    /// </summary>
    private sealed class Reader
    {
        private readonly List<List<string?>> _grid = new();           // null 은 아직 아무도 차지하지 않은 자리
        private readonly StringBuilder _text = new();
        private int _row = -1;
        private bool _inCell;
        private int _colspan = 1, _rowspan = 1;

        public void StartRow()
        {
            _row++;
            Ensure(_row);
        }

        public void StartCell(int colspan, int rowspan)
        {
            CloseCell();
            if (_row < 0) StartRow();
            _inCell = true;
            _colspan = colspan;
            _rowspan = rowspan;
        }

        public void Append(char ch)
        {
            if (_inCell) _text.Append(ch);
        }

        public void CloseCell()
        {
            if (!_inCell) return;
            _inCell = false;

            string value = Clean(_text.ToString());
            _text.Clear();

            int col = FirstFree(_row);
            for (int r = _row; r < _row + _rowspan; r++)
            {
                Ensure(r);
                for (int c = col; c < col + _colspan; c++)
                {
                    List<string?> line = _grid[r];
                    while (line.Count <= c) line.Add(null);
                    // 합쳐진 칸은 왼쪽 위에만 값을 두고 나머지는 빈 칸으로 둔다.
                    line[c] = r == _row && c == col ? value : "";
                }
            }
            _colspan = _rowspan = 1;
        }

        public List<List<string>> Rows()
        {
            int width = 0;
            foreach (List<string?> line in _grid) width = Math.Max(width, line.Count);

            var rows = new List<List<string>>(_grid.Count);
            foreach (List<string?> line in _grid)
            {
                var row = new List<string>(width);
                for (int c = 0; c < width; c++) row.Add(c < line.Count ? line[c] ?? "" : "");
                rows.Add(row);
            }
            return rows;
        }

        private void Ensure(int row)
        {
            while (_grid.Count <= row) _grid.Add(new List<string?>());
        }

        private int FirstFree(int row)
        {
            List<string?> line = _grid[row];
            int c = 0;
            while (c < line.Count && line[c] is not null) c++;
            return c;
        }
    }

    private readonly record struct Tag(string Name, string Attributes, int End, bool Closing);

    private static Tag ReadTag(string html, int start)
    {
        int i = start + 1;
        bool closing = i < html.Length && html[i] == '/';
        if (closing) i++;

        if (i < html.Length && html[i] == '!')                        // 주석과 선언은 이름이 없다
        {
            if (string.Compare(html, start, "<!--", 0, 4, StringComparison.Ordinal) == 0)
            {
                int end = html.IndexOf("-->", start, StringComparison.Ordinal);
                return new Tag("", "", end < 0 ? html.Length : end + 3, false);
            }
            int gt = html.IndexOf('>', start);
            return new Tag("", "", gt < 0 ? html.Length : gt + 1, false);
        }

        int nameStart = i;
        while (i < html.Length && char.IsAsciiLetterOrDigit(html[i])) i++;
        string name = html[nameStart..i].ToLowerInvariant();

        // 속성 값에 든 > 에 속지 않게 따옴표를 센다. style="a>b" 같은 것이 있다.
        var attrs = new StringBuilder();
        char quote = '\0';
        while (i < html.Length)
        {
            char ch = html[i++];
            if (quote != '\0')
            {
                if (ch == quote) quote = '\0';
                attrs.Append(ch);
                continue;
            }
            if (ch is '"' or '\'') { quote = ch; attrs.Append(ch); continue; }
            if (ch == '>') break;
            attrs.Append(ch);
        }
        return new Tag(name, attrs.ToString(), i, closing);
    }

    private static int SkipTo(string html, string name, int from)
    {
        for (int i = html.IndexOf('<', from); i >= 0; i = html.IndexOf('<', i + 1))
        {
            Tag tag = ReadTag(html, i);
            if (tag.Closing && tag.Name == name) return tag.End;
        }
        return html.Length;
    }

    private static int IndexOfTag(string html, string name)
    {
        for (int i = html.IndexOf('<'); i >= 0; i = html.IndexOf('<', i + 1))
        {
            int j = i + 1;
            if (j >= html.Length || html[j] == '/') continue;
            if (string.Compare(html, j, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
            int after = j + name.Length;
            if (after >= html.Length || !char.IsAsciiLetterOrDigit(html[after])) return i;
        }
        return -1;
    }

    private static int Span(string attrs, string name)
    {
        int i = attrs.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return 1;
        i += name.Length;
        while (i < attrs.Length && attrs[i] is ' ' or '=' or '"' or '\'') i++;
        int start = i;
        while (i < attrs.Length && char.IsAsciiDigit(attrs[i])) i++;
        return i > start
            && int.TryParse(attrs[start..i], NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            && n is > 0 and <= CellRef.MaxRows
                ? n : 1;
    }

    /// <summary>칸 안의 글자를 다듬는다. 엔티티를 풀고 공백을 한 자로 줄인다.</summary>
    private static string Clean(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char ch = raw[i];
            if (ch != '&') { sb.Append(ch); continue; }

            int semi = raw.IndexOf(';', i + 1);
            string? one = semi > 0 && semi - i <= 10 ? Entity(raw[(i + 1)..semi]) : null;
            if (one is null) { sb.Append(ch); continue; }
            sb.Append(one);
            i = semi;
        }
        return Collapse(sb.ToString());
    }

    private static string? Entity(string name) => name switch
    {
        "amp" => "&",
        "lt" => "<",
        "gt" => ">",
        "quot" => "\"",
        "apos" => "'",
        "nbsp" => " ",
        _ when name.StartsWith("#x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex)
            => FromCode(hex),
        _ when name.StartsWith('#')
            && int.TryParse(name[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int dec)
            => FromCode(dec),
        _ => null,
    };

    private static string? FromCode(int code) =>
        code > 0 && code <= 0x10FFFF && code is < 0xD800 or > 0xDFFF ? char.ConvertFromUtf32(code) : null;

    private static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char ch in s)
        {
            if (char.IsWhiteSpace(ch) || ch == ' ') { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
