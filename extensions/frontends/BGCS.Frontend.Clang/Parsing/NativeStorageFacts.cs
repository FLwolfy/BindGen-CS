namespace BGCS.Frontend.Clang.Parsing;

internal readonly record struct NativeStorageFacts(
    long? Size,
    int? Alignment,
    long? ArrayLength,
    string? TypeSpelling = null
);
