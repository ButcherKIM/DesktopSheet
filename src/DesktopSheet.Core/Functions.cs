using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopSheet.Core;

/// <summary>
/// 사양서 8.2 의 함수 스무 개. 집계 함수는 범위 안의 텍스트와 빈 칸을 무시하고,
/// 인수로 직접 준 텍스트는 #VALUE! 다.
/// </summary>
internal static class Functions
{
    internal static Value Call(Evaluator ev, CallNode call)
    {
        string n = call.Name;
        var a = call.Args;

        // IF 만 고른 가지를 셈한다. 나머지는 인수를 먼저 셈한다.
        if (n == "IF") return If(ev, a);

        switch (n)
        {
            case "SUM":     return Aggregate(ev, a, v => v.Sum());
            case "AVERAGE": return Aggregate(ev, a, v => v.Count == 0 ? throw new FormulaException(CellError.DivideByZero) : v.Average());
            case "MIN":     return Aggregate(ev, a, v => v.Count == 0 ? 0 : v.Min());
            case "MAX":     return Aggregate(ev, a, v => v.Count == 0 ? 0 : v.Max());
            case "COUNT":   return Count(ev, a);
            case "TODAY":   return Arity(a, 0) ?? Value.Num(SerialDate.FromDate(DateOnly.FromDateTime(DateTime.Now)));
        }

        // 나머지는 숫자 인수를 받는다.
        var args = new List<Value>(a.Count);
        foreach (Node node in a)
        {
            Value v = ev.EvalArg(node);
            if (v.IsError) return v;
            args.Add(v);
        }

        try
        {
            switch (n)
            {
                case "ROUND":      return Fixed(args, 2, (x, d) => Round(x, (int)d, MidpointRounding.AwayFromZero));
                case "ROUNDUP":    return Fixed(args, 2, (x, d) => Scale(x, (int)d, up: true));
                case "ROUNDDOWN":  return Fixed(args, 2, (x, d) => Scale(x, (int)d, up: false));
                case "INT":        return Fixed(args, 1, (x, _) => Math.Floor(x));
                case "MOD":        return Fixed(args, 2, (x, y) => y == 0 ? throw new FormulaException(CellError.DivideByZero) : x - y * Math.Floor(x / y));
                case "ABS":        return Fixed(args, 1, (x, _) => Math.Abs(x));
                case "SQRT":       return Fixed(args, 1, (x, _) => x < 0 ? throw new FormulaException(CellError.Number) : Math.Sqrt(x));
                case "EXP":        return Fixed(args, 1, (x, _) => Math.Exp(x));
                case "POWER":      return Fixed(args, 2, Math.Pow);
                case "LN":         return Fixed(args, 1, (x, _) => x <= 0 ? throw new FormulaException(CellError.Number) : Math.Log(x));
                case "LOG10":      return Fixed(args, 1, (x, _) => x <= 0 ? throw new FormulaException(CellError.Number) : Math.Log10(x));
                case "LOG":        return Log(args);
                case "DATE":       return Date(args);
                default:           return Value.Err(CellError.Name);
            }
        }
        catch (FormulaException e) { return Value.Err(e.Error); }
    }

    private static Value? Arity(IReadOnlyList<Node> a, int n) =>
        a.Count == n ? null : Value.Err(CellError.Value);

    /// <summary>범위 안의 숫자만 모은다. 텍스트와 빈 칸은 무시하고, 인수로 직접 준 텍스트는 #VALUE! 다.</summary>
    private static Value Aggregate(Evaluator ev, IReadOnlyList<Node> args, Func<List<double>, double> fold)
    {
        var nums = new List<double>();
        foreach (Node arg in args)
        {
            bool isRange = Evaluator.IsRange(arg);
            foreach (Value v in ev.Spread(arg))
            {
                if (v.IsError) return v;
                if (isRange)
                {
                    if (v.Kind == ValueKind.Number) nums.Add(v.Number);   // 텍스트·빈 칸·참거짓은 건너뛴다
                }
                else
                {
                    if (!v.TryNumber(out double x)) return Value.Err(CellError.Value);
                    nums.Add(x);
                }
            }
        }
        try { return Value.Num(fold(nums)); }
        catch (FormulaException e) { return Value.Err(e.Error); }
    }

    private static Value Count(Evaluator ev, IReadOnlyList<Node> args)
    {
        int n = 0;
        foreach (Node arg in args)
            foreach (Value v in ev.Spread(arg))
            {
                if (v.IsError) return v;
                if (v.Kind == ValueKind.Number) n++;
            }
        return Value.Num(n);
    }

    private static Value If(Evaluator ev, IReadOnlyList<Node> a)
    {
        if (a.Count is < 2 or > 3) return Value.Err(CellError.Value);
        Value cond = ev.EvalArg(a[0]);
        if (cond.IsError) return cond;
        if (!cond.TryNumber(out double c)) return Value.Err(CellError.Value);
        if (c != 0) return ev.EvalArg(a[1]);
        return a.Count == 3 ? ev.EvalArg(a[2]) : Value.Bool(false);   // 8.2: 세 번째를 생략하면 FALSE
    }

    private static Value Log(List<Value> args)
    {
        if (args.Count is < 1 or > 2) return Value.Err(CellError.Value);
        if (!args[0].TryNumber(out double x)) return Value.Err(CellError.Value);
        double b = 10;                                                 // 8.2: 밑의 기본은 엑셀과 같은 10
        if (args.Count == 2 && !args[1].TryNumber(out b)) return Value.Err(CellError.Value);
        if (x <= 0 || b <= 0 || b == 1) return Value.Err(CellError.Number);
        return Value.Num(Math.Log(x) / Math.Log(b));
    }

    private static Value Date(List<Value> args)
    {
        if (args.Count != 3) return Value.Err(CellError.Value);
        if (!args[0].TryNumber(out double y) || !args[1].TryNumber(out double m) || !args[2].TryNumber(out double d))
            return Value.Err(CellError.Value);
        try
        {
            var date = new DateOnly((int)y, 1, 1).AddMonths((int)m - 1).AddDays((int)d - 1);
            int serial = SerialDate.FromDate(date);
            if (serial is < SerialDate.MinSerial or > SerialDate.MaxSerial) return Value.Err(CellError.Number);
            return Value.Num(serial);
        }
        catch (ArgumentOutOfRangeException) { return Value.Err(CellError.Number); }
    }

    private static Value Fixed(List<Value> args, int arity, Func<double, double, double> f)
    {
        if (args.Count > arity || args.Count < arity - (arity == 2 ? 1 : 0)) return Value.Err(CellError.Value);
        if (!args[0].TryNumber(out double x)) return Value.Err(CellError.Value);
        double y = 0;
        if (args.Count > 1 && !args[1].TryNumber(out y)) return Value.Err(CellError.Value);
        double r = f(x, y);
        if (double.IsNaN(r) || double.IsInfinity(r)) return Value.Err(CellError.Number);
        return Value.Num(r);
    }

    private static double Round(double x, int digits, MidpointRounding mode)
    {
        if (digits is < -15 or > 15) throw new FormulaException(CellError.Number);
        return digits >= 0 ? Math.Round(x, digits, mode)
                           : Math.Round(x / Math.Pow(10, -digits), 0, mode) * Math.Pow(10, -digits);
    }

    private static double Scale(double x, int digits, bool up)
    {
        double f = Math.Pow(10, digits);
        double v = x * f;
        v = up ? (x >= 0 ? Math.Ceiling(v) : Math.Floor(v))
               : (x >= 0 ? Math.Floor(v)   : Math.Ceiling(v));
        return v / f;
    }
}
