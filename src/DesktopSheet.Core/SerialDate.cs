using System;

namespace DesktopSheet.Core;

/// <summary>
/// 사양서 5장. 날짜를 엑셀식 일련번호로 저장한다. 1900-01-01 이 1 이다.
/// 엑셀은 있지도 않은 1900-02-29 를 60 번으로 세는데, 그 뒤 날짜를 엑셀과 맞추려면 같은 규칙을 따라야 한다.
/// </summary>
public static class SerialDate
{
    private static readonly DateOnly Epoch = new(1899, 12, 31);
    private static readonly DateOnly LeapBugStart = new(1900, 3, 1);

    public const int MinSerial = 1;          // 1900-01-01
    public const int MaxSerial = 2958465;    // 9999-12-31

    public static int FromDate(DateOnly d)
    {
        int days = d.DayNumber - Epoch.DayNumber;
        if (d >= LeapBugStart) days += 1;    // 엑셀이 세는 없는 날 1900-02-29
        return days;
    }

    public static DateOnly ToDate(int serial)
    {
        if (serial >= 61) serial -= 1;
        return Epoch.AddDays(serial);
    }
}
