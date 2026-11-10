using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DesktopSheet.Core;
using static DesktopSheet.UiCheck.Driver;

namespace DesktopSheet.UiCheck;

/// <summary>
/// 사용: DesktopSheet.UiCheck &lt;DesktopSheet.exe&gt; &lt;결과 폴더&gt;
/// 장면은 칸이 겹치지 않게 늘어놓았다. 창은 처음 뜰 때 A~E 열, 1~19 행이 보이므로 클릭은 그 안에서만 한다.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("사용: DesktopSheet.UiCheck <DesktopSheet.exe> <결과 폴더>");
            return 2;
        }
        string exe = Path.GetFullPath(args[0]);
        string outDir = Path.GetFullPath(args[1]);
        Console.OutputEncoding = Encoding.UTF8;

        var report = new Report();
        using (Driver d = Driver.Launch(exe, outDir))
        {
            report.Shot(d.Shot("시작"), "처음 뜬 창");
            Run(d, report);
        }

        report.Write(Path.Combine(outDir, "report.md"));
        Console.WriteLine(File.ReadAllText(Path.Combine(outDir, "report.md")));
        return report.Failures == 0 ? 0 : 1;
    }

    private static void Run(Driver d, Report r)
    {
        r.Scene("숫자를 치고 Enter 를 누르면 값이 들어가고 커서가 내려간다", () =>
        {
            d.Click(0, 0);
            d.Type("123");
            r.Shot(d.Shot("숫자-치는-중"), "A1 에 123 을 치는 중. 편집기 테두리가 보여야 함");
            d.Press(VK.Return);
            d.Settle();
            r.Expect("A1", "123", d.Raw(0, 0));
            LoadResult saved = d.Load();
            r.Expect("커서 자리", "A2", Name(saved.Window.Row, saved.Window.Col));
        });

        r.Scene("한글 텍스트가 들어간다 (입력기는 거치지 않음)", () =>
        {
            d.Click(2, 0);
            d.Type("가나다");
            d.Press(VK.Return);
            d.Settle();
            r.Expect("A3", "가나다", d.Raw(2, 0));
            r.Note("글자를 직접 넣었으므로 한글 입력기의 조합 과정은 이 시험에 들어 있지 않다");
        });

        r.Scene("등호 뒤 방향키가 참조를 찍고, 다시 누르면 찍은 참조가 옮겨 간다", () =>
        {
            d.Click(0, 2);
            d.Type("=SUM(");
            d.Press(VK.Down);
            r.Shot(d.Shot("방향키-한번"), "C1 에서 =SUM( 뒤 ↓ 한 번. C2 에 빨간 점선, 식은 =SUM(C2");
            d.Press(VK.Down);
            d.Press(VK.Down, shift: true);
            r.Shot(d.Shot("방향키-범위"), "↓ 한 번 더, Shift+↓. C3:C4 에 빨간 점선, 식은 =SUM(C3:C4");
            d.Type(")");
            d.Press(VK.Return);
            d.Settle();
            r.Expect("C1", "=SUM(C3:C4)", d.Raw(0, 2));
        });

        r.Scene("등호 뒤 클릭이 참조를 넣는다", () =>
        {
            d.Click(0, 3);
            d.Type("=");
            d.Click(0, 0);
            r.Shot(d.Shot("클릭-찍기"), "D1 에서 = 뒤에 A1 클릭. A1 에 빨간 점선, 식은 =A1");
            d.Type("*2");
            d.Press(VK.Return);
            d.Settle();
            r.Expect("D1", "=A1*2", d.Raw(0, 3));
        });

        r.Scene("등호 뒤에 끌면 범위가 들어간다", () =>
        {
            d.Click(0, 4);
            d.Type("=SUM(");
            d.Drag(0, 0, 2, 0);
            r.Shot(d.Shot("끌어-찍기"), "E1 에서 =SUM( 뒤에 A1 부터 A3 까지 끌기");
            d.Type(")");
            d.Press(VK.Return);
            d.Settle();
            r.Expect("E1", "=SUM(A1:A3)", d.Raw(0, 4));
        });

        r.Scene("찍어 둔 참조는 다음 클릭이 갈아치운다", () =>
        {
            d.Click(0, 1);
            d.Type("=");
            d.Click(0, 0);
            d.Click(0, 2);
            d.Press(VK.Return);
            d.Settle();
            r.Expect("B1", "=C1", d.Raw(0, 1));
        });

        r.Scene("F2 로 들어오면 방향키가 글자 사이를 오가고 참조를 찍지 않는다", () =>
        {
            d.Click(0, 3);
            d.Press(VK.F2);
            d.Press(VK.Home);
            d.Press(VK.Right, times: 2);
            d.Type("$");
            r.Shot(d.Shot("편집-모드"), "D1 을 F2 로 열어 A 뒤에 $ 를 끼운 모습. 빨간 점선이 없어야 함");
            d.Press(VK.Return);
            d.Settle();
            r.Expect("D1", "=A$1*2", d.Raw(0, 3));
        });

        r.Scene("편집 중 F2 가 입력과 편집을 번갈아 바꾼다", () =>
        {
            d.Click(2, 1);
            d.Type("=1+");
            d.Press(VK.F2);                    // 편집 모드: ← 는 글자 사이를 오간다
            d.Press(VK.Left);
            d.Press(VK.F2);                    // 입력 모드: 끝으로 가서 ↓ 를 누르면 찍는다
            d.Press(VK.End);
            d.Press(VK.Down);
            d.Press(VK.Return);
            d.Settle();
            r.Expect("B3", "=1+B4", d.Raw(2, 1));
        });

        r.Scene("복사하면 탭 구분 텍스트와 HTML 표가 같이 실린다", () =>
        {
            d.Click(0, 0);
            d.Click(1, 1, shift: true);
            d.Press(VK.C, ctrl: true);
            r.Shot(d.Shot("복사"), "A1:B2 를 복사한 모습. 파란 점선");

            IDataObject? data = Clipboard.GetDataObject();
            string text = Clipboard.GetText();
            r.Expect("탭 구분 텍스트", "123\t0\r\n\t\r\n", text);

            string? html = ReadHtml(data);
            r.Expect("HTML 형식이 실렸는가", "있음", html is null ? "없음" : "있음");
            if (html is null) return;

            List<List<string>> rows = HtmlTable.Read(html);
            r.Expect("HTML 표의 모양", "2x2", rows.Count == 0 ? "표 없음" : $"{rows.Count}x{rows[0].Count}");
            if (rows.Count > 0) r.Expect("HTML 표 A1", "123", rows[0][0]);

            byte[] bytes = Encoding.UTF8.GetBytes(html);
            r.Expect("StartFragment 가 가리키는 글자", "<table", Slice(bytes, Offset(html, "StartFragment"), 6));
            r.Expect("EndFragment 가 가리키는 글자", "<!--EndFragment-->", Slice(bytes, Offset(html, "EndFragment"), 18));
            d.Press(VK.Escape);
        });

        r.Scene("파워포인트 표처럼 생긴 HTML 을 붙이면 합친 칸까지 제자리에 들어간다", () =>
        {
            // 파워포인트는 텍스트 쪽에 행과 열의 경계를 남기지 않는다. 텍스트를 읽으면 세로로 한 줄이 된다.
            string fragment = "<table><tr><td colspan=\"2\"><p>제목</p></td><td>끝</td></tr>" +
                              "<tr><td>가</td><td>1</td><td>2</td></tr></table>";
            var data = new DataObject();
            data.SetText("제목\r\n끝\r\n가\r\n1\r\n2", TextDataFormat.UnicodeText);
            data.SetText(CfHtml(fragment), TextDataFormat.Html);
            Clipboard.SetDataObject(data, true);

            d.Click(5, 2);
            d.Press(VK.V, ctrl: true);
            d.Settle();
            r.Shot(d.Shot("표-붙여넣기"), "C6 에 붙인 표. 제목 / 빈 칸 / 끝, 가 / 1 / 2");
            r.Expect("C6", "제목", d.Raw(5, 2));
            r.Expect("D6 (합친 칸의 나머지)", null, d.Raw(5, 3));
            r.Expect("E6", "끝", d.Raw(5, 4));
            r.Expect("C7", "가", d.Raw(6, 2));
            r.Expect("E7", "2", d.Raw(6, 4));
            r.Expect("C8 (텍스트로 읽었으면 여기에 값이 생김)", null, d.Raw(7, 2));
        });

        r.Scene("편집 중에 휠로 굴려도 편집기가 머리글 위로 올라오지 않는다", () =>
        {
            d.Click(8, 1);
            d.Type("abc");
            d.Wheel(-4);                       // 열두 행 내려간다. B9 는 화면 위로 빠진다
            r.Shot(d.Shot("굴린-편집"), "B9 를 치다가 아래로 굴린 모습. 편집기가 머리글과 탭 위에 보이면 안 됨");
            d.Press(VK.Escape);
            d.ScrollHome();
            d.Settle();
            r.Expect("B9 (Esc 로 취소)", null, d.Raw(8, 1));
        });

        r.Scene("방향키로 찍다가 화면 밖까지 가면 화면이 따라간다", () =>
        {
            d.Click(11, 0);
            d.Type("=");
            d.Press(VK.Down, times: 15);
            r.Shot(d.Shot("멀리-찍기"), "A12 에서 = 뒤에 ↓ 열다섯 번. A27 에 빨간 점선이 보이게 화면이 내려가 있어야 함");
            d.Press(VK.Return);
            d.Settle();
            r.Expect("A12", "=A27", d.Raw(11, 0));
            d.ScrollHome();
        });

        r.Scene("치던 값은 시트 탭을 눌러도 원래 시트에 들어간다", () =>
        {
            d.Click(d.Button("+"));
            d.Click(14, 0);
            d.Type("x");
            d.Click(d.Button("Sheet2"));
            d.Settle();
            r.Shot(d.Shot("시트-넘기기"), "A15 에 x 를 치다가 Sheet2 탭을 누른 모습");
            r.Expect("Sheet1 의 A15", "x", d.Raw(14, 0, sheet: 0));
            r.Expect("Sheet2 의 A15", null, d.Raw(14, 0, sheet: 1));
            r.Expect("보고 있는 시트", "1", d.Load().Window.Sheet.ToString());
            d.Click(d.Button("Sheet1"));
        });

        r.Scene("마지막 모습", () =>
        {
            d.ScrollHome();
            r.Shot(d.Shot("끝"), "모든 장면을 마친 Sheet1");
        });
    }

    private static string Name(int row, int col) => $"{(char)('A' + col)}{row + 1}";

    private static string? ReadHtml(IDataObject? data)
    {
        object? o = data?.GetData(DataFormats.Html);
        return o switch
        {
            string s => s,
            MemoryStream m => Encoding.UTF8.GetString(m.ToArray()).TrimEnd('\0'),
            _ => null,
        };
    }

    private static int Offset(string html, string name)
    {
        int at = html.IndexOf(name + ":", StringComparison.Ordinal);
        return at < 0 ? -1 : int.Parse(html.Substring(at + name.Length + 1, 10));
    }

    private static string Slice(byte[] bytes, int start, int length) =>
        start < 0 || start + length > bytes.Length ? "(자리 밖)" : Encoding.UTF8.GetString(bytes, start, length);

    /// <summary>시험용 클립보드 HTML. 자리는 바이트로 센다.</summary>
    private static string CfHtml(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string open = "<html><body>\r\n<!--StartFragment-->";
        const string close = "<!--EndFragment-->\r\n</body></html>";
        int startHtml = string.Format(header, 0, 0, 0, 0).Length;
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(open);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(close);
        return string.Format(header, startHtml, endHtml, startFragment, endFragment) + open + fragment + close;
    }
}
