using System;
using System.Collections.Generic;

namespace DesktopSheet.Core;

/// <summary>
/// 사양서 8.1 의 우선순위표를 그대로 옮긴 내림차순 파서.
/// 위에 있는 것을 먼저 묶는다: 괄호, 범위, 단항 마이너스, 백분율, 제곱, 곱셈, 덧셈, 비교.
/// 단항 마이너스가 제곱보다 위에 있어 =-2^2 가 4 이고, 제곱을 왼쪽부터 묶어 =2^3^2 가 64 다.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _t;
    private int _i;

    private Parser(List<Token> tokens) => _t = tokens;

    public static Node Parse(string formula)
    {
        var p = new Parser(Tokenizer.Scan(formula));
        Node n = p.ParseComparison();
        if (p.Peek.Kind != TokenKind.End) throw new FormulaException(CellError.Name, "식이 끝나지 않았습니다");
        return n;
    }

    private Token Peek => _t[_i];
    private Token Take() => _t[_i++];
    private bool Match(TokenKind k)
    {
        if (Peek.Kind != k) return false;
        _i++;
        return true;
    }

    private Node ParseComparison()
    {
        Node left = ParseAdditive();
        while (Peek.Kind is TokenKind.Equal or TokenKind.NotEqual or TokenKind.Less
                         or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
        {
            TokenKind op = Take().Kind;
            left = new BinaryNode(op, left, ParseAdditive());
        }
        return left;
    }

    private Node ParseAdditive()
    {
        Node left = ParseMultiplicative();
        while (Peek.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            TokenKind op = Take().Kind;
            left = new BinaryNode(op, left, ParseMultiplicative());
        }
        return left;
    }

    private Node ParseMultiplicative()
    {
        Node left = ParsePower();
        while (Peek.Kind is TokenKind.Star or TokenKind.Slash)
        {
            TokenKind op = Take().Kind;
            left = new BinaryNode(op, left, ParsePower());
        }
        return left;
    }

    private Node ParsePower()
    {
        Node left = ParseUnary();
        while (Match(TokenKind.Caret))          // 왼쪽부터 묶는다
            left = new BinaryNode(TokenKind.Caret, left, ParseUnary());
        return left;
    }

    private Node ParseUnary()
    {
        if (Match(TokenKind.Minus)) return new UnaryNode(TokenKind.Minus, ParseUnary());
        if (Match(TokenKind.Plus))  return ParseUnary();
        return ParsePostfix();
    }

    private Node ParsePostfix()
    {
        Node n = ParsePrimary();
        while (Match(TokenKind.Percent)) n = new PercentNode(n);
        return n;
    }

    private Node ParsePrimary()
    {
        Token t = Take();
        switch (t.Kind)
        {
            case TokenKind.Number:
                return new NumberNode(t.Number);

            case TokenKind.Text:
                return new TextNode(t.Lexeme);

            case TokenKind.LParen:
            {
                Node inner = ParseComparison();
                if (!Match(TokenKind.RParen)) throw new FormulaException(CellError.Name, "닫는 괄호가 없습니다");
                return inner;
            }

            case TokenKind.Ref:
            {
                CellRef.TryParse(t.Lexeme, out CellRef a);
                if (Match(TokenKind.Colon))
                {
                    Token e = Take();
                    if (e.Kind != TokenKind.Ref || !CellRef.TryParse(e.Lexeme, out CellRef b))
                        throw new FormulaException(CellError.Name, "범위의 끝이 셀 주소가 아닙니다");
                    return new RangeNode(a, b);
                }
                return new RefNode(a);
            }

            case TokenKind.Name:
            {
                string name = t.Lexeme.ToUpperInvariant();
                if (name == "TRUE")  return new BoolNode(true);
                if (name == "FALSE") return new BoolNode(false);
                if (!Match(TokenKind.LParen)) throw new FormulaException(CellError.Name, $"모르는 이름 {t.Lexeme}");

                var args = new List<Node>();
                if (Peek.Kind != TokenKind.RParen)
                {
                    do { args.Add(ParseComparison()); } while (Match(TokenKind.Comma));
                }
                if (!Match(TokenKind.RParen)) throw new FormulaException(CellError.Name, "닫는 괄호가 없습니다");
                return new CallNode(name, args);
            }

            default:
                throw new FormulaException(CellError.Name, $"식이 잘못되었습니다: {t.Lexeme}");
        }
    }
}
