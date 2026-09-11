using System;
using System.Globalization;

namespace DesktopSheet.Core;

/// <summary>사양서 8.4. 값의 종류는 숫자(날짜 포함), 텍스트, 참·거짓 셋이고 빈 칸과 오류가 따로 있다.</summary>
public enum ValueKind { Blank, Number, Text, Bool, Error }

public readonly struct Value : IEquatable<Value>
{
    public ValueKind Kind { get; }
    public double Number { get; }
    public string Text { get; }
    public CellError Error { get; }

    private Value(ValueKind kind, double number, string text, CellError error)
        => (Kind, Number, Text, Error) = (kind, number, text ?? "", error);

    public static readonly Value Blank = new(ValueKind.Blank, 0, "", default);
    public static Value Num(double v) => new(ValueKind.Number, v, "", default);
    public static Value Str(string v) => new(ValueKind.Text, 0, v, default);
    public static Value Bool(bool v) => new(ValueKind.Bool, v ? 1 : 0, "", default);
    public static Value Err(CellError e) => new(ValueKind.Error, 0, "", e);

    public bool IsError => Kind == ValueKind.Error;

    /// <summary>숫자 자리에 쓸 때. 빈 칸은 0 이고 참·거짓은 1 과 0 이며 텍스트는 #VALUE! 다(8.3, 8.4).</summary>
    public bool TryNumber(out double n)
    {
        switch (Kind)
        {
            case ValueKind.Number: n = Number; return true;
            case ValueKind.Bool:   n = Number; return true;
            case ValueKind.Blank:  n = 0;      return true;
            default:               n = 0;      return false;
        }
    }

    /// <summary>비교할 때의 자리. 숫자가 텍스트보다 항상 작고 텍스트가 참·거짓보다 작다(8.4).</summary>
    public int TypeRank => Kind switch
    {
        ValueKind.Number => 0, ValueKind.Blank => 0,
        ValueKind.Text => 1,
        ValueKind.Bool => 2,
        _ => 3,
    };

    public bool Equals(Value other) =>
        Kind == other.Kind && Number.Equals(other.Number) && Text == other.Text && Error == other.Error;
    public override bool Equals(object? o) => o is Value v && Equals(v);
    public override int GetHashCode() => HashCode.Combine((int)Kind, Number, Text, (int)Error);

    public override string ToString() => Kind switch
    {
        ValueKind.Blank  => "",
        ValueKind.Number => Number.ToString("R", CultureInfo.InvariantCulture),
        ValueKind.Text   => Text,
        ValueKind.Bool   => Number != 0 ? "TRUE" : "FALSE",
        _                => CellErrorText.Of(Error),
    };
}
