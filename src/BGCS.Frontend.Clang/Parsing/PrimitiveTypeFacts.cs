namespace BGCS.Frontend.Clang.Parsing;

internal readonly record struct PrimitiveTypeFacts(
    int? Alignment,
    bool? IsSigned
);
