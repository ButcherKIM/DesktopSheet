using System;
using System.Collections.Generic;

namespace DesktopSheet.Core;

/// <summary>칸을 읽어 주는 쪽. 없는 시트나 범위를 벗어난 주소는 #REF! 를 돌려준다.</summary>
public interface ICellSource
{
    Value Read(CellRef reference);
}

/// <summary>사양서 8장. 식을 셈한다. 계산은 원본 값으로 하고 반올림은 표시할 때만 한다(8.5).</summary>
public sealed class Evaluator(ICellSource source)
{
    private readonly ICellSource _source = source;

    public Value Evaluate(Node node)
    {
        try { return Eval(node); }
        catch (FormulaException e) { return Value.Err(e.Error); }
        catch (OverflowException) { return Value.Err(CellError.Number); }
    }

    public Value Evaluate(string formula) => Evaluate(ParseOrThrow(formula));

    public static Node ParseOrThrow(string formula) => Parser.Parse(formula);

    private Value Eval(Node node) => node switch
    {
        NumberNode n  => Value.Num(n.Value),
        TextNode t    => Value.Str(t.Value),
        BoolNode b    => Value.Bool(b.Value),
        RefNode r     => _source.Read(r.Ref),
        RangeNode     => Value.Err(CellError.Value),      // 8.3: 범위는 함수의 인수 자리에서만 쓴다
        UnaryNode u   => EvalUnary(u),
        PercentNode p => EvalPercent(p),
        BinaryNode b  => EvalBinary(b),
        CallNode c    => Functions.Call(this, c),
        _ => Value.Err(CellError.Value),
    };

    private Value EvalUnary(UnaryNode u)
    {
        Value v = Eval(u.Operand);
        if (v.IsError) return v;
        if (!v.TryNumber(out double n)) return Value.Err(CellError.Value);
        return Value.Num(-n);
    }

    private Value EvalPercent(PercentNode p)
    {
        Value v = Eval(p.Operand);
        if (v.IsError) return v;
        if (!v.TryNumber(out double n)) return Value.Err(CellError.Value);
        return Value.Num(n / 100.0);
    }

    private Value EvalBinary(BinaryNode b)
    {
        Value l = Eval(b.Left);
        if (l.IsError) return l;
        Value r = Eval(b.Right);
        if (r.IsError) return r;

        if (b.Op is TokenKind.Equal or TokenKind.NotEqual or TokenKind.Less
                 or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
            return Compare(b.Op, l, r);

        if (!l.TryNumber(out double x) || !r.TryNumber(out double y)) return Value.Err(CellError.Value);

        switch (b.Op)
        {
            case TokenKind.Plus:  return Value.Num(Numeric.SnapNearZero(x + y, x, y));
            case TokenKind.Minus: return Value.Num(Numeric.SnapNearZero(x - y, x, y));
            case TokenKind.Star:  return Value.Num(x * y);
            case TokenKind.Slash:
                if (y == 0) return Value.Err(CellError.DivideByZero);
                return Value.Num(x / y);
            case TokenKind.Caret:
            {
                double p = Math.Pow(x, y);
                if (double.IsNaN(p) || double.IsInfinity(p)) return Value.Err(CellError.Number);
                return Value.Num(p);
            }
            default: return Value.Err(CellError.Value);
        }
    }

    /// <summary>8.4. 종류가 다르면 숫자가 텍스트보다 작고 텍스트가 참·거짓보다 작다. 텍스트끼리는 대소문자를 가리지 않는다.</summary>
    private static Value Compare(TokenKind op, Value l, Value r)
    {
        int c;
        if (l.TypeRank != r.TypeRank) c = l.TypeRank.CompareTo(r.TypeRank);
        else if (l.TypeRank == 1)     c = string.Compare(Textish(l), Textish(r), StringComparison.OrdinalIgnoreCase);
        else
        {
            l.TryNumber(out double a);
            r.TryNumber(out double b);
            c = a.CompareTo(b);
        }

        return Value.Bool(op switch
        {
            TokenKind.Equal        => c == 0,
            TokenKind.NotEqual     => c != 0,
            TokenKind.Less         => c < 0,
            TokenKind.LessEqual    => c <= 0,
            TokenKind.Greater      => c > 0,
            TokenKind.GreaterEqual => c >= 0,
            _ => false,
        });
    }

    private static string Textish(Value v) => v.Kind == ValueKind.Text ? v.Text : "";

    /// <summary>함수 인수 하나를 값 여럿으로 편다. 범위면 칸을 훑고 아니면 값 하나다.</summary>
    internal IEnumerable<Value> Spread(Node arg)
    {
        if (arg is RangeNode range)
        {
            int r1 = Math.Min(range.From.Row, range.To.Row), r2 = Math.Max(range.From.Row, range.To.Row);
            int c1 = Math.Min(range.From.Col, range.To.Col), c2 = Math.Max(range.From.Col, range.To.Col);
            for (int r = r1; r <= r2; r++)
                for (int c = c1; c <= c2; c++)
                    yield return _source.Read(range.From with { Row = r, Col = c });
        }
        else
        {
            yield return Eval(arg);
        }
    }

    internal Value EvalArg(Node arg) => Eval(arg);
    internal static bool IsRange(Node arg) => arg is RangeNode;
}
