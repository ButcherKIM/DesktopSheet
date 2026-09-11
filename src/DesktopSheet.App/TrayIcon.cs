using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopSheet.App;

/// <summary>
/// 사양서 14.3. 작업 표시줄에도 Alt+Tab 에도 뜨지 않으므로 이 아이콘이 창을 부르는 유일한 길이다.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DesktopSheet";

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _autoStartItem;
    private nint _iconHandle;

    public event Action? OpenRequested;
    public event Action? HideRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("창 숨기기", null, (_, _) => HideRequested?.Invoke());
        _autoStartItem = new ToolStripMenuItem("로그온할 때 자동 시작", null, (_, _) => ToggleAutoStart())
        {
            CheckOnClick = false,
            Checked = AutoStartEnabled,
        };
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = MakeIcon(out _iconHandle),
            Text = "DesktopSheet",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    /// <summary>13.4 가 파일을 되살렸을 때 띄우는 알림.</summary>
    public void Notify(string title, string body) =>
        _icon.ShowBalloonTip(5000, title, body, ToolTipIcon.Info);

    /// <summary>14.4. 레지스트리의 실행 항목에 등록한다.</summary>
    public static bool AutoStartEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        set
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;
            if (value)
            {
                string exe = Environment.ProcessPath ?? "";
                if (exe.Length > 0) key.SetValue(RunValueName, $"\"{exe}\" --tray");
            }
            else key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    private void ToggleAutoStart()
    {
        AutoStartEnabled = !AutoStartEnabled;
        _autoStartItem.Checked = AutoStartEnabled;
    }

    /// <summary>칸이 그려진 작은 표. 파일로 들고 다니지 않으려고 그때그때 그린다.</summary>
    private static Icon MakeIcon(out nint handle)
    {
        using var bmp = new Bitmap(16, 16);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.FillRectangle(Brushes.White, 1, 1, 14, 14);
            using var pen = new Pen(Color.FromArgb(0x2F, 0x36, 0x42));
            g.DrawRectangle(pen, 1, 1, 13, 13);
            g.DrawLine(pen, 1, 6, 14, 6);
            g.DrawLine(pen, 6, 1, 6, 14);
            using var accent = new SolidBrush(Color.FromArgb(0x0B, 0x48, 0xC8));
            g.FillRectangle(accent, 7, 7, 7, 7);
        }
        handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_iconHandle != 0) { DestroyIcon(_iconHandle); _iconHandle = 0; }
    }
}
