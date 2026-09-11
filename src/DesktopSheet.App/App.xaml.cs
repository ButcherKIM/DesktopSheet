using System;
using System.Threading;
using System.Windows;
using DesktopSheet.Core;

namespace DesktopSheet.App;

public partial class App : System.Windows.Application
{
    private const string InstanceName = "DesktopSheet.SingleInstance";
    private const string WakeName = "DesktopSheet.Wake";

    private Mutex? _instance;
    private EventWaitHandle? _wake;
    private TrayIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string[] args = e.Args;

        // 14.1: 창은 하나로 고정이다. 두 번 실행하면 이미 떠 있는 창을 앞으로 가져온다.
        _instance = new Mutex(initiallyOwned: true, InstanceName, out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(WakeName).Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        var store = new BookStore(BookStore.DefaultDirectory());
        LoadResult loaded = store.Load();

        _window = new MainWindow(store, loaded);
        _tray = new TrayIcon();
        _tray.OpenRequested += () => Win32.BringToFront(_window);
        _tray.HideRequested += () => _window.Hide();
        _tray.ExitRequested += ExitCleanly;

        // 13.2: 윈도우가 꺼지거나 로그오프할 때 한 번 더 쓴다.
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => _window.FlushBeforeExit();
        Exit += (_, _) => { _window.FlushBeforeExit(); _tray?.Dispose(); };

        // 다른 실행이 깨우면 창을 앞으로 가져온다.
        _wake = new EventWaitHandle(false, EventResetMode.AutoReset, WakeName);
        var waiter = new Thread(WaitForWake) { IsBackground = true };
        waiter.Start();

        // 14.4: 로그온으로 뜬 것이면 트레이 아이콘만 올리고 창은 띄우지 않는다.
        bool trayOnly = Array.Exists(args, a => a is "--tray" or "-tray");
        if (!trayOnly) _window.Show();

        if (loaded.Outcome == LoadOutcome.RestoredFromPrev)
            _tray.Notify("DesktopSheet", "저장 파일을 읽지 못해 직전 복사본으로 되살렸습니다.");
        else if (loaded.Outcome == LoadOutcome.StartedEmpty && loaded.BrokenFile is not null)
            _tray.Notify("DesktopSheet", "저장 파일을 읽지 못해 빈 시트로 시작했습니다. 읽지 못한 파일은 저장 폴더에 남겨 두었습니다.");
    }

    private void WaitForWake()
    {
        while (_wake is not null && _wake.WaitOne())
            Dispatcher.Invoke(() => { if (_window is not null) Win32.BringToFront(_window); });
    }

    private void ExitCleanly()
    {
        _window?.FlushBeforeExit();
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }
}
