using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DesktopSheet.App;

/// <summary>사양서 14.2 와 14.6 이 쓰는 윈도우 호출들.</summary>
internal static class Win32
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    /// <summary>14.2. 작업 표시줄에서도 Alt+Tab 목록에서도 감춘다.</summary>
    public static void HideFromTaskbarAndAltTab(Window window)
    {
        nint h = new WindowInteropHelper(window).Handle;
        if (h == 0) return;
        int ex = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
    }

    public static void BringToFront(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        nint h = new WindowInteropHelper(window).Handle;
        if (h != 0) SetForegroundWindow(h);
    }

    /// <summary>14.6. 저장된 자리가 화면 밖이면 주 모니터 안으로 끌어들인다.</summary>
    public static (double X, double Y) ClampToScreen(double x, double y, double width, double height)
    {
        var work = SystemParameters.WorkArea;
        double cx = System.Math.Clamp(x, work.Left, System.Math.Max(work.Left, work.Right - width));
        double cy = System.Math.Clamp(y, work.Top, System.Math.Max(work.Top, work.Bottom - height));

        bool visible = x + width > work.Left + 40 && x < work.Right - 40
                    && y + height > work.Top + 40 && y < work.Bottom - 40;
        return visible ? (x, y) : (cx, cy);
    }
}
