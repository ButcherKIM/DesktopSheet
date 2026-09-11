using System.Collections.Generic;

namespace DesktopSheet.Core;

/// <summary>사양서 8.1 이 정한 순서대로 묶인 식의 나무.</summary>
public abstract record Node;

public sealed record NumberNode(double Value) : Node;
public sealed record TextNode(string Value) : Node;
public sealed record BoolNode(bool Value) : Node;
public sealed record RefNode(CellRef Ref) : Node;
public sealed record RangeNode(CellRef From, CellRef To) : Node;
public sealed record UnaryNode(TokenKind Op, Node Operand) : Node;
public sealed record PercentNode(Node Operand) : Node;
public sealed record BinaryNode(TokenKind Op, Node Left, Node Right) : Node;
public sealed record CallNode(string Name, IReadOnlyList<Node> Args) : Node;
