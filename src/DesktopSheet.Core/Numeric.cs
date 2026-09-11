using System;

namespace DesktopSheet.Core;

/// <summary>사양서 8.5. 계산은 원본 값으로 하고 반올림은 표시할 때만 한다.</summary>
public static class Numeric
{
    /// <summary>
    /// 더하기와 빼기의 결과가 피연산자에 견주어 아주 작으면 0으로 맞춘다.
    /// 이것이 없으면 =0.1+0.2-0.3 이 이진 부동소수점의 찌꺼기 5.55e-17 을 남기고,
    /// 그 값이 2장의 지수 규칙에 걸려 5.5511E-17 로 화면에 뜬다.
    /// </summary>
    public static double SnapNearZero(double result, double left, double right)
    {
        double scale = Math.Max(Math.Abs(left), Math.Abs(right));
        return scale > 0 && Math.Abs(result) < scale * 1e-15 ? 0.0 : result;
    }
}
