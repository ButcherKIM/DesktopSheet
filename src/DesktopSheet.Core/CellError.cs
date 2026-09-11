namespace DesktopSheet.Core;

/// <summary>사양서 8.4 의 오류 값. 가장 긴 #DIV/0! 이 7자라 표시 예산 10자에 모두 들어간다.</summary>
public enum CellError
{
    DivideByZero,   // #DIV/0!
    Value,          // #VALUE!
    Name,           // #NAME?
    Reference,      // #REF!
    Number,         // #NUM!
    NotAvailable,   // #N/A
    Circular,       // #CIRC!  엑셀에 없는 이름. 8.4 참고
}

public static class CellErrorText
{
    public static string Of(CellError e) => e switch
    {
        CellError.DivideByZero => "#DIV/0!",
        CellError.Value        => "#VALUE!",
        CellError.Name         => "#NAME?",
        CellError.Reference    => "#REF!",
        CellError.Number       => "#NUM!",
        CellError.NotAvailable => "#N/A",
        CellError.Circular     => "#CIRC!",
        _ => "#VALUE!",
    };
}
