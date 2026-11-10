using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using DesktopSheet.Core;

namespace DesktopSheet.UiCheck;

/// <summary>
/// 실행 파일을 띄우고 사람처럼 키와 마우스를 넣는다. 결과는 저장 파일을 읽어 확인한다 -
/// 화면에 무엇이 보이는지는 찍어 둔 그림으로 사람이(또는 Claude 가) 본다.
/// </summary>
internal sealed class Driver : IDisposable
{
    // 창 안쪽 자리. 탭 줄 24px, 열 머리글 20px, 행 머리글 40px, 칸 104 x 20px (사양서 1장, 12.3).
    // 창 테두리 1px 은 그리드 위에 겹쳐 그리므로 자리를 차지하지 않는다.
    private const double Border = 0, TabBar = 24, ColHeader = 20, RowHeader = 40, CellW = 104, CellH = 20;

    private readonly Process _process;
    private readonly string _shots;
    private double _scale = 1;
    private int _shotCount;

    public IntPtr Window { get; private set; }
    public string DataDir { get; } = BookStore.DefaultDirectory();

    private Driver(Process process, string shots)
    {
        _process = process;
        _shots = shots;
    }

    public static Driver Launch(string exe, string shots)
    {
        SetProcessDpiAwarenessContext(-4);                       // 화면 픽셀로 셈한다(PerMonitorV2)

        string data = BookStore.DefaultDirectory();
        if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        Directory.CreateDirectory(shots);

        var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })
                      ?? throw new InvalidOperationException("실행 파일을 띄우지 못함");
        var driver = new Driver(process, shots);
        driver.Window = WaitForWindow(process, TimeSpan.FromSeconds(60));
        driver._scale = GetDpiForWindow(driver.Window) / 96.0;
        Thread.Sleep(1500);                                      // 첫 그리기가 끝나기를 기다린다
        return driver;
    }

    public void Dispose()
    {
        try { if (!_process.HasExited) _process.Kill(); } catch (InvalidOperationException) { }
    }

    // --- 자리 ---

    public Point Cell(int row, int col)
    {
        var origin = new POINT();
        ClientToScreen(Window, ref origin);
        double x = Border + RowHeader + col * CellW + CellW / 2;
        double y = Border + TabBar + ColHeader + row * CellH + CellH / 2;
        return new Point(origin.X + (int)Math.Round(x * _scale), origin.Y + (int)Math.Round(y * _scale));
    }

    /// <summary>탭 단추는 글자 폭에 따라 자리가 달라 UI 자동화로 찾는다.</summary>
    public Point Button(string name)
    {
        AutomationElement root = AutomationElement.FromHandle(Window);
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, name));
        AutomationElement found = root.FindFirst(TreeScope.Descendants, condition)
                                  ?? throw new InvalidOperationException($"'{name}' 단추를 찾지 못함");
        System.Windows.Rect b = found.Current.BoundingRectangle;
        return new Point((int)(b.X + b.Width / 2), (int)(b.Y + b.Height / 2));
    }

    // --- 마우스 ---

    public void Click(int row, int col, bool shift = false) => Click(Cell(row, col), shift);

    public void Click(Point p, bool shift = false)
    {
        MoveTo(p);
        if (shift) KeyDown(VK.Shift);
        Mouse(MOUSEEVENTF_LEFTDOWN);
        Mouse(MOUSEEVENTF_LEFTUP);
        if (shift) KeyUp(VK.Shift);
        Pause(200);
    }

    public void DoubleClick(int row, int col)
    {
        MoveTo(Cell(row, col));
        Mouse(MOUSEEVENTF_LEFTDOWN); Mouse(MOUSEEVENTF_LEFTUP);
        Mouse(MOUSEEVENTF_LEFTDOWN); Mouse(MOUSEEVENTF_LEFTUP);
        Pause(250);
    }

    public void Drag(int r1, int c1, int r2, int c2)
    {
        Point a = Cell(r1, c1), b = Cell(r2, c2);
        MoveTo(a);
        Mouse(MOUSEEVENTF_LEFTDOWN);
        for (int i = 1; i <= 8; i++)                              // 한 번에 건너뛰지 않고 끌고 간다
        {
            MoveTo(new Point(a.X + (b.X - a.X) * i / 8, a.Y + (b.Y - a.Y) * i / 8));
            Pause(40);
        }
        Mouse(MOUSEEVENTF_LEFTUP);
        Pause(200);
    }

    /// <summary>휠 한 칸이 세 행이다(12.3). 아래로 굴리려면 음수.</summary>
    public void Wheel(int notches, int row = 5, int col = 2)
    {
        MoveTo(Cell(row, col));
        for (int i = 0; i < Math.Abs(notches); i++)
        {
            Send(MouseInput(MOUSEEVENTF_WHEEL, notches > 0 ? 120 : -120));
            Pause(60);
        }
        Pause(200);
    }

    /// <summary>화면을 맨 위 왼쪽으로 돌린다. 칸 자리는 굴리지 않은 화면을 기준으로 셈하기 때문이다.</summary>
    public void ScrollHome()
    {
        Wheel(80);
        MoveTo(Cell(5, 2));
        KeyDown(VK.Shift);
        for (int i = 0; i < 10; i++) { Send(MouseInput(MOUSEEVENTF_WHEEL, 120)); Pause(30); }
        KeyUp(VK.Shift);
        Pause(200);
    }

    /// <summary>그림을 찍기 전에 마우스를 창 밖으로 치운다. 툴팁이 그림을 가리지 않게 한다.</summary>
    public void Park()
    {
        GetWindowRect(Window, out RECT r);
        MoveTo(new Point(r.Right + 40, r.Bottom + 40));
        Pause(150);
    }

    private void MoveTo(Point p)
    {
        int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN);
        var input = MouseInput(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, 0);
        input.u.mi.dx = (int)Math.Round(p.X * 65535.0 / (w - 1));
        input.u.mi.dy = (int)Math.Round(p.Y * 65535.0 / (h - 1));
        Send(input);
        Pause(30);
    }

    private static void Mouse(uint flags)
    {
        Send(MouseInput(flags, 0));
        Thread.Sleep(30);
    }

    // --- 키 ---

    /// <summary>글자를 그대로 넣는다. 입력기를 거치지 않으므로 한글 조합은 이것으로 시험하지 못한다.</summary>
    public void Type(string text)
    {
        foreach (char ch in text)
        {
            Send(KeyInput(0, ch, KEYEVENTF_UNICODE));
            Send(KeyInput(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            Thread.Sleep(40);
        }
        Pause(150);
    }

    public void Press(ushort vk, bool shift = false, bool ctrl = false, int times = 1)
    {
        for (int i = 0; i < times; i++)
        {
            if (ctrl) KeyDown(VK.Control);
            if (shift) KeyDown(VK.Shift);
            KeyDown(vk);
            KeyUp(vk);
            if (shift) KeyUp(VK.Shift);
            if (ctrl) KeyUp(VK.Control);
            Thread.Sleep(80);
        }
        Pause(150);
    }

    private static void KeyDown(ushort vk) => Send(KeyInput(vk, '\0', Extended(vk)));
    private static void KeyUp(ushort vk) => Send(KeyInput(vk, '\0', Extended(vk) | KEYEVENTF_KEYUP));

    // 방향키와 Home/End 는 확장 키로 보내야 숫자판 키로 읽히지 않는다. 숫자판으로 읽히면 Shift 가 풀린다.
    private static uint Extended(ushort vk) =>
        vk is VK.Left or VK.Up or VK.Right or VK.Down or VK.Home or VK.End or VK.Delete ? KEYEVENTF_EXTENDEDKEY : 0;

    // --- 결과 ---

    /// <summary>저장 파일이 쓰일 때까지 기다린다. 앱은 마지막 입력 500ms 뒤에 쓴다(13.2).</summary>
    public void Settle() => Thread.Sleep(1200);

    public LoadResult Load()
    {
        for (int i = 0; ; i++)
        {
            try { return new BookStore(DataDir).Load(); }
            catch (IOException) when (i < 20) { Thread.Sleep(100); }
        }
    }

    public string? Raw(int row, int col, int sheet = 0)
    {
        Workbook book = Load().Book;
        if (sheet >= book.Sheets.Count) return null;
        string? raw = book.FindCell(new CellAddress(sheet, row, col))?.Raw;
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    public string Shot(string name)
    {
        Park();
        GetWindowRect(Window, out RECT r);
        using var bmp = new Bitmap(r.Right - r.Left, r.Bottom - r.Top);
        using (Graphics g = Graphics.FromImage(bmp))
            g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size);
        string file = $"{++_shotCount:00}-{name}.png";
        bmp.Save(Path.Combine(_shots, file), ImageFormat.Png);
        return file;
    }

    private static void Pause(int ms) => Thread.Sleep(ms);

    private static IntPtr WaitForWindow(Process process, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != process.Id || !IsWindowVisible(h)) return true;
                var title = new StringBuilder(64);
                GetWindowText(h, title, title.Capacity);
                if (title.ToString() != "DesktopSheet") return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) return found;
            if (process.HasExited) throw new InvalidOperationException($"실행 파일이 끝나 버림(코드 {process.ExitCode})");
            Thread.Sleep(250);
        }
        throw new TimeoutException("창이 뜨지 않음");
    }

    // --- Win32 ---

    internal static class VK
    {
        public const ushort Back = 0x08, Tab = 0x09, Return = 0x0D, Shift = 0x10, Control = 0x11, Escape = 0x1B;
        public const ushort End = 0x23, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Delete = 0x2E;
        public const ushort C = 0x43, V = 0x56, Z = 0x5A, F2 = 0x71;
    }

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
    private const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4;
    private const uint MOUSEEVENTF_WHEEL = 0x800, MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static INPUT MouseInput(uint flags, int data) =>
        new() { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = unchecked((uint)data) } } };

    private static INPUT KeyInput(ushort vk, char scan, uint flags) =>
        new() { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } } };

    private static void Send(INPUT input)
    {
        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) != 1)
            throw new InvalidOperationException($"SendInput 실패: {Marshal.GetLastWin32Error()}");
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    private static bool SetProcessDpiAwarenessContext(int value) => SetProcessDpiAwarenessContext((IntPtr)value);
}
