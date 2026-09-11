using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DesktopSheet.Core;

public enum TokenKind
{
    Number, Text, Name, Ref,
    Plus, Minus, Star, Slash, Caret, Percent,
    LParen, RParen, Comma, Colon,
    Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual,
    End,
}

public readonly record struct Token(TokenKind Kind, string Lexeme, double Number, int Position);

/// <summary>사양서 8.1 의 식을 토막으로 끊는다. 공백은 버린다(8.3).</summary>
public static class Tokenizer
{
    public static List<Token> Scan(string formula)
    {
        string s = formula.StartsWith('=') ? formula[1..] : formula;
        var tokens = new List<Token>();
        int i = 0;

        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])))
            {
                int start = i;
                while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.')) i++;
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    int save = i;
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    if (i < s.Length && char.IsAsciiDigit(s[i])) { while (i < s.Length && char.IsAsciiDigit(s[i])) i++; }
                    else i = save;           // 1E 뒤에 숫자가 없으면 지수가 아니다
                }
                string lex = s[start..i];
                double.TryParse(lex, NumberStyles.Float, CultureInfo.InvariantCulture, out double n);
                tokens.Add(new Token(TokenKind.Number, lex, n, start));
                continue;
            }

            if (c == '"')
            {
                int start = i++;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    if (s[i] == '"')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        i++; break;
                    }
                    sb.Append(s[i++]);
                }
                tokens.Add(new Token(TokenKind.Text, sb.ToString(), 0, start));
                continue;
            }

            if (c == '\'' || c == '$' || char.IsLetter(c) || c == '_')
            {
                int start = i;
                if (c == '\'')                      // '내 시트'!A1
                {
                    i++;
                    while (i < s.Length && !(s[i] == '\'' && (i + 1 >= s.Length || s[i + 1] != '\''))) i += s[i] == '\'' ? 2 : 1;
                    if (i < s.Length) i++;
                }
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '$' or '.')) i++;
                if (i < s.Length && s[i] == '!')    // 시트 이름 뒤의 주소까지 한 토막으로 묶는다
                {
                    i++;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '$')) i++;
                }
                string lex = s[start..i];
                tokens.Add(CellRef.TryParse(lex, out _)
                    ? new Token(TokenKind.Ref, lex, 0, start)
                    : new Token(TokenKind.Name, lex, 0, start));
                continue;
            }

            TokenKind kind;
            int len = 1;
            switch (c)
            {
                case '+': kind = TokenKind.Plus; break;
                case '-': kind = TokenKind.Minus; break;
                case '*': kind = TokenKind.Star; break;
                case '/': kind = TokenKind.Slash; break;
                case '^': kind = TokenKind.Caret; break;
                case '%': kind = TokenKind.Percent; break;
                case '(': kind = TokenKind.LParen; break;
                case ')': kind = TokenKind.RParen; break;
                case ',': kind = TokenKind.Comma; break;
                case ':': kind = TokenKind.Colon; break;
                case '=': kind = TokenKind.Equal; break;
                case '<':
                    if (i + 1 < s.Length && s[i + 1] == '>') { kind = TokenKind.NotEqual; len = 2; }
                    else if (i + 1 < s.Length && s[i + 1] == '=') { kind = TokenKind.LessEqual; len = 2; }
                    else kind = TokenKind.Less;
                    break;
                case '>':
                    if (i + 1 < s.Length && s[i + 1] == '=') { kind = TokenKind.GreaterEqual; len = 2; }
                    else kind = TokenKind.Greater;
                    break;
                default:
                    throw new FormulaException(CellError.Name, $"모르는 글자 '{c}'");
            }
            tokens.Add(new Token(kind, s.Substring(i, len), 0, i));
            i += len;
        }

        tokens.Add(new Token(TokenKind.End, "", 0, s.Length));
        return tokens;
    }
}

/// <summary>식을 읽거나 셈하다 멈출 때 던진다. 셀에는 Error 가 찍힌다.</summary>
public sealed class FormulaException(CellError error, string? detail = null)
    : Exception(detail ?? CellErrorText.Of(error))
{
    public CellError Error { get; } = error;
}
