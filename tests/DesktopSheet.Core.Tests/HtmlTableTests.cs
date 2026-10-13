using System.Collections.Generic;
using System.Linq;
using System.Text;
using DesktopSheet.Core;
using Xunit;

namespace DesktopSheet.Core.Tests;

/// <summary>사양서 10.4 의 HTML 표. 내보낼 때는 머리글의 자리가 맞아야 하고, 받을 때는 합쳐진 칸이 밀리지 않아야 한다.</summary>
public class HtmlTableTests
{
    private static Workbook Book(params (int Row, int Col, string Raw)[] cells)
    {
        var book = new Workbook();
        foreach ((int row, int col, string raw) in cells)
            book.SetInput(new CellAddress(0, row, col), raw);
        return book;
    }

    [Fact]
    public void 머리글의_바이트_자리가_조각의_시작과_끝을_정확히_가리킨다()
    {
        string html = HtmlTable.Write(Book((0, 0, "가")), CellRange.One(new CellAddress(0, 0, 0)));

        int startHtml = int.Parse(Field(html, "StartHTML"));
        int endHtml = int.Parse(Field(html, "EndHTML"));
        int startFragment = int.Parse(Field(html, "StartFragment"));
        int endFragment = int.Parse(Field(html, "EndFragment"));

        byte[] bytes = Encoding.UTF8.GetBytes(html);
        Assert.Equal(bytes.Length, endHtml);
        Assert.Equal(html.Length, bytes.Length);                       // 몸통이 ASCII 라 글자 수가 곧 바이트 수다
        Assert.Equal("<html>", html.Substring(startHtml, 6));
        Assert.Equal("<table", html.Substring(startFragment, 6));
        Assert.Equal("<!--EndFragment-->", html.Substring(endFragment, 18));
    }

    private static string Field(string html, string name)
    {
        int at = html.IndexOf(name + ":", System.StringComparison.Ordinal) + name.Length + 1;
        return html.Substring(at, 10);
    }

    [Fact]
    public void 한글은_엔티티로_바꿔_싣는다()
    {
        string html = HtmlTable.Fragment(Book((0, 0, "가")), CellRange.One(new CellAddress(0, 0, 0)));
        Assert.Contains("&#xAC00;", html);
        Assert.DoesNotContain("가", html);
    }

    [Fact]
    public void 숫자는_오른쪽_텍스트는_왼쪽에_붙인다()
    {
        string html = HtmlTable.Fragment(Book((0, 0, "12"), (0, 1, "ab")), CellRange.Of(0, 0, 0, 0, 1));
        Assert.Contains("text-align:right\">12</td>", html);
        Assert.Contains("text-align:left\">ab</td>", html);
    }

    [Fact]
    public void 빈_칸은_접히지_않게_공백_한_자를_둔다()
    {
        string html = HtmlTable.Fragment(new Workbook(), CellRange.One(new CellAddress(0, 0, 0)));
        Assert.Contains("&nbsp;</td>", html);
    }

    [Fact]
    public void 내보낸_표를_그대로_다시_읽는다()
    {
        Workbook book = Book((0, 0, "1"), (0, 1, "가나"), (1, 0, "=A1+1"), (1, 1, "a<b&c"));
        List<List<string>> rows = HtmlTable.Read(HtmlTable.Write(book, CellRange.Of(0, 0, 0, 1, 1)));

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "1", "가나" }, rows[0]);
        Assert.Equal(new[] { "2", "a<b&c" }, rows[1]);                 // 수식은 계산 결과로 실린다
    }

    [Fact]
    public void 표가_없는_HTML_은_빈_목록이다()
    {
        Assert.Empty(HtmlTable.Read("<html><body><p>표가 아니다</p></body></html>"));
        Assert.Empty(HtmlTable.Read(""));
    }

    [Fact]
    public void 가로로_합친_칸은_왼쪽에_값을_두고_나머지는_빈_칸이다()
    {
        List<List<string>> rows = HtmlTable.Read(
            "<table><tr><td colspan=\"2\">머리</td><td>끝</td></tr><tr><td>1</td><td>2</td><td>3</td></tr></table>");

        Assert.Equal(new[] { "머리", "", "끝" }, rows[0]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
    }

    [Fact]
    public void 세로로_합친_칸은_다음_행을_왼쪽으로_밀지_않는다()
    {
        List<List<string>> rows = HtmlTable.Read(
            "<table><tr><td rowspan=2>왼쪽</td><td>a</td></tr><tr><td>b</td></tr></table>");

        Assert.Equal(new[] { "왼쪽", "a" }, rows[0]);
        Assert.Equal(new[] { "", "b" }, rows[1]);                      // 두 번째 행의 b 는 둘째 열에 선다
    }

    [Fact]
    public void 엑셀이_실어_보내는_꼴을_읽는다()
    {
        // 엑셀은 조각 앞에 style 블록을 두고, td 에 제 속성을 붙여 보낸다.
        string html =
            "Version:1.0\r\nStartHTML:000000001\r\n<html><head><style>td{mso-number-format:General;}</style></head>" +
            "<body><!--StartFragment--><table border=0 cellpadding=0 cellspacing=0 width=128>" +
            "<tr height=19><td height=19 align=right x:num>1,234</td><td class=xl65>&#44032;</td></tr>" +
            "</table><!--EndFragment--></body></html>";

        List<List<string>> rows = HtmlTable.Read(html);
        Assert.Single(rows);
        Assert.Equal(new[] { "1,234", "가" }, rows[0]);
    }

    [Fact]
    public void 칸_안의_줄바꿈과_공백은_한_자로_줄인다()
    {
        List<List<string>> rows = HtmlTable.Read(
            "<table><tr><td>  첫 줄<br>둘째 줄  </td><td>&nbsp;</td></tr></table>");

        Assert.Equal(new[] { "첫 줄 둘째 줄", "" }, rows[0]);
    }

    [Fact]
    public void 표_안에_든_표는_바깥_표의_한_칸으로_삼는다()
    {
        List<List<string>> rows = HtmlTable.Read(
            "<table><tr><td><table><tr><td>속</td><td>안</td></tr></table></td><td>밖</td></tr></table>");

        Assert.Single(rows);
        Assert.Equal(2, rows[0].Count);
        Assert.Equal("밖", rows[0][1]);
    }

    [Fact]
    public void 속성_값에_든_부등호에_속지_않는다()
    {
        List<List<string>> rows = HtmlTable.Read("<table><tr><td title=\"a>b\">값</td></tr></table>");
        Assert.Equal(new[] { "값" }, rows[0]);
    }

    [Fact]
    public void 파워포인트처럼_th_와_p_로_싸인_표도_읽는다()
    {
        List<List<string>> rows = HtmlTable.Read(
            "<table><tr><th><p>이름</p></th><th><p>값</p></th></tr><tr><td><p>가</p></td><td><p>1</p></td></tr></table>");

        Assert.Equal(new[] { "이름", "값" }, rows[0]);
        Assert.Equal(new[] { "가", "1" }, rows[1]);
    }

    [Fact]
    public void 행마다_칸_수가_다르면_넓은_쪽에_맞춰_채운다()
    {
        List<List<string>> rows = HtmlTable.Read("<table><tr><td>1</td></tr><tr><td>2</td><td>3</td></tr></table>");
        Assert.True(rows.All(r => r.Count == 2));
        Assert.Equal(new[] { "1", "" }, rows[0]);
    }
}
