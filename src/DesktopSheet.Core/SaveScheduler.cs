using System;

namespace DesktopSheet.Core;

/// <summary>
/// 사양서 13.2. 마지막 입력에서 500ms 가 지나면 쓰고, 계속 치고 있더라도 마지막으로 쓴 지 5초가 지나면 한 번 쓴다.
/// 시계를 밖에서 넣으므로 시험에서 시간을 마음대로 돌릴 수 있다.
/// </summary>
public sealed class SaveScheduler
{
    public static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    private DateTime? _firstUnsavedEdit;
    private DateTime _lastEdit;

    public bool IsDirty => _firstUnsavedEdit.HasValue;

    /// <summary>칸이 바뀌었다고 알린다.</summary>
    public void Touch(DateTime now)
    {
        _firstUnsavedEdit ??= now;
        _lastEdit = now;
    }

    public bool ShouldSave(DateTime now)
    {
        if (_firstUnsavedEdit is not { } first) return false;
        return now - _lastEdit >= IdleDelay || now - first >= MaxDelay;
    }

    public void Saved() => _firstUnsavedEdit = null;
}
