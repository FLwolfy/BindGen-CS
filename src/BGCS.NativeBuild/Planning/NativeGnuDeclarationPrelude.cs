using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.IO;

namespace BGCS.NativeBuild.Planning;

// This is a compiler-directive boundary reader, never a C++ declaration rewriter.
// Only complete leading authenticated SDK include trees are replaced by a prelude.
internal static class NativeGnuDeclarationPrelude
{
    internal static (string source, NativeDeclarationPrelude? prelude) Extract(
        string trace,
        ImmutableArray<string> authenticatedRoots,
        ImmutableArray<string> dependencyFiles,
        CancellationToken cancellationToken = default
    ) {
        StringComparer paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        string[] roots = authenticatedRoots.Select(PhysicalPathResolver.Resolve).Select(path => path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar).Distinct(paths).ToArray();
        HashSet<string> dependencies = dependencyFiles.Select(Path.GetFullPath).ToHashSet(paths);
        List<NativeLineDirectiveReader.Directive> markers = [];
        _ = NativeLineDirectiveReader.Rewrite(trace, directive => { markers.Add(directive); return null; });
        var macros = NativeMacroDirectiveReader.ReadDirectives(trace);
        var events = markers.Select(marker => (marker.start, marker.length, marker: (NativeLineDirectiveReader.Directive?)marker, macro: (NativeMacroDirectiveReader.Directive?)null))
            .Concat(macros.Select(macro => (macro.start, macro.length, marker: (NativeLineDirectiveReader.Directive?)null, macro: (NativeMacroDirectiveReader.Directive?)macro)))
            .OrderBy(item => item.start).ToArray();
        StringBuilder frozen = new(), prelude = new();
        List<string> headers = [];
        List<(string file, int line)> stack = [];
        Stack<string?> sdkPack = new();
        HashSet<string> checkedUsers = new(paths), compilerMacros = new(StringComparer.Ordinal), sdkMacros = new(StringComparer.Ordinal), userChanges = new(StringComparer.Ordinal);
        Dictionary<string, NativeMacroDirectiveReader.Directive> userMacros = new(StringComparer.Ordinal);
        string current = string.Empty;
        int position = 0, line = 0, foldDepth = -1;
        bool semantic = false;
        foreach (var item in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Chunk(item.start);
            bool folding = foldDepth >= 0;
            if (item.marker is { } marker)
            {
                if (marker.msvc) throw Refuse("mixed line directive dialects");
                bool enter = (marker.flags & 1) != 0, leave = (marker.flags & 2) != 0;
                if (enter && leave) throw Refuse("contradictory include entry/return metadata");
                string next = Normalize(marker.file);
                if (!Pseudo(next) && !dependencies.Contains(next)) throw Refuse("a line marker names an unobserved dependency: " + next);
                bool sdk = !Pseudo(next) && roots.Any(root => PhysicalPathResolver.Resolve(next).StartsWith(root, Comparison));
                if (enter)
                {
                    if (folding && !sdk) throw Refuse("a user header is nested inside a toolchain header: " + next);
                    if (sdk && !folding)
                    {
                        if (semantic) throw Refuse("a toolchain include follows user semantic tokens");
                        if ((marker.flags & 4) == 0) throw Refuse("a toolchain entry lacks native system-header evidence");
                        foreach (var ancestor in stack) ValidateUserPrefix(ancestor.file, ancestor.line);
                        ValidateUserPrefix(current, line);
                        foreach (string changed in userChanges)
                            if (compilerMacros.Contains(changed) || DynamicBuiltin(changed))
                                throw Refuse("a user define/undef changes a native compiler builtin: " + changed);
                        foreach (var definition in userMacros.Values)
                        {
                            if (HasMacroOperator(definition.text)) throw Refuse("a replayed user definition contains an unproved stringification or token-pasting operator: " + definition.name);
                            if (compilerMacros.Contains(definition.name)) throw Refuse("a user definition replaces a native compiler builtin: " + definition.name);
                            foreach (string word in Words(definition.text))
                                if (word != definition.name && (compilerMacros.Contains(word) || sdkMacros.Contains(word)
                                    || DynamicBuiltin(word)))
                                    throw Refuse("an include-entry user definition depends on compiler or SDK macro state: " + definition.name);
                        }
                        foldDepth = stack.Count;
                        headers.Add(next);
                        prelude.Append("#include \"").Append(Escape(next)).AppendLine("\"");
                    }
                    stack.Add((current, line));
                }
                else if (leave)
                {
                    if (stack.Count == 0 || !paths.Equals(stack[^1].file, next)) throw Refuse("an unmatched include return");
                    stack.RemoveAt(stack.Count - 1);
                    if (folding && stack.Count == foldDepth)
                    {
                        if (sdkPack.Count != 0) throw Refuse("unbalanced SDK pragma-pack state at an include return");
                        foldDepth = -1;
                    }
                }
                else if (!paths.Equals(next, current) && stack.Count != 0)
                    throw Refuse("a file transition has no include entry/return evidence");
                current = next;
                line = int.TryParse(marker.lineNumber, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed - 1 : throw Refuse("invalid native source line");
                if (foldDepth < 0 && !Pseudo(current)) frozen.Append(trace, item.start, item.length);
            }
            else if (item.macro is { } directive)
            {
                if (directive.operation == "pragma")
                {
                    if (folding) ObserveSdkPack(directive.text);
                    if (!folding && !semantic && !directive.text.Trim().Equals("#pragma once", StringComparison.Ordinal))
                        throw Refuse("unproved leading user pragma state");
                    if (!folding && !Pseudo(current)) frozen.Append(directive.text);
                }
                else
                {
                    if (current == "<built-in>") compilerMacros.Add(directive.name);
                    else if (folding) sdkMacros.Add(directive.name);
                    else if (!semantic)
                    {
                        userChanges.Add(directive.name);
                        if (directive.operation == "define") userMacros[directive.name] = directive;
                        else userMacros.Remove(directive.name);
                        prelude.AppendLine(directive.text);
                    }
                    // Native user tokens were already expanded. No live macro
                    // definition or undef may be replayed around those tokens.
                    if (!folding && !Pseudo(current)) frozen.Append('\n', directive.text.Count(character => character == '\n'));
                }
                line += directive.text.Count(character => character == '\n');
            }
            position = item.start + item.length;
        }
        cancellationToken.ThrowIfCancellationRequested();
        Chunk(trace.Length);
        if (stack.Count != 0 || foldDepth >= 0) throw Refuse("an incomplete include tree");
        return (frozen.ToString(), headers.Count == 0 ? null : new(prelude.ToString(), headers.ToImmutableArray()));

        void Chunk(int end)
        {
            string text = trace[position..end];
            if (foldDepth < 0 && !Pseudo(current))
            {
                if (HasSemantic(text)) semantic = true;
                frozen.Append(text);
            }
            line += text.Count(character => character == '\n');
        }
        void ValidateUserPrefix(
            string path,
            int beforeLine
        ) {
            if (Pseudo(path)) return;
            cancellationToken.ThrowIfCancellationRequested();
            string original = File.ReadAllText(path);
            if (checkedUsers.Add(path))
                _ = NativeLineDirectiveReader.Rewrite(original, _ => throw Refuse("user-authored line metadata cannot authenticate a toolchain boundary"));
            string prefix = string.Join('\n', original.Split('\n').Take(Math.Max(0, beforeLine)));
            foreach (string changed in NativeMacroDirectiveReader.ChangedNames(prefix, true))
                if (compilerMacros.Contains(changed) || DynamicBuiltin(changed)) throw Refuse("original user define/undef changes a native compiler builtin: " + changed);
            if (Words(prefix).Any(word => word == "__COUNTER__")) throw Refuse("a consumed original user counter changes the folded SDK entry state");
            if (Words(prefix).Any(word => word is "_Pragma" or "__pragma")) throw Refuse("unproved original user pragma operator before a toolchain entry");
            if (NativeMacroDirectiveReader.ReadDirectives(prefix).Any(directive => directive.operation == "pragma"
                && !directive.text.Trim().Equals("#pragma once", StringComparison.Ordinal)))
                throw Refuse("unproved original user pack, push/pop or pragma history before a toolchain entry");
        }
        void ObserveSdkPack(string directive)
        {
            // Reprocessing is allowed only when native SDK packing is restored
            // to its entry state. Bare resets and implementation-specific forms
            // cannot establish that relationship and remain unsupported.
            string tokens = WithoutComments(directive);
            int keyword = tokens.IndexOf("pragma", StringComparison.Ordinal);
            string body = keyword < 0 ? string.Empty : tokens[(keyword + 6)..].Trim();
            if (!body.StartsWith("pack", StringComparison.Ordinal)) return;
            body = body[4..].Trim();
            if (body.Length < 2 || body[0] != '(' || body[^1] != ')') throw Refuse("unproved SDK pragma-pack spelling");
            string[] arguments = body[1..^1].Split(',', StringSplitOptions.TrimEntries);
            if (arguments.Length == 0 || arguments[0] is not ("push" or "pop")) throw Refuse("unproved SDK pragma-pack reset or assignment");
            if (arguments[0] == "push")
            {
                if (arguments.Length > 3 || arguments.Skip(1).Any(argument => argument.Length == 0)) throw Refuse("unproved SDK pragma-pack push");
                string? label = arguments.Length > 1 && !arguments[1].All(char.IsAsciiDigit) ? arguments[1] : null;
                if (label is not null && !Identifier(label)) throw Refuse("unproved SDK pragma-pack label");
                if (arguments.Length == 3 && (label is null || !arguments[2].All(char.IsAsciiDigit))) throw Refuse("unproved SDK pragma-pack size");
                sdkPack.Push(label);
            }
            else
            {
                if (arguments.Length > 2 || sdkPack.Count == 0) throw Refuse("unbalanced SDK pragma-pack pop");
                if (arguments.Length == 1) sdkPack.Pop();
                else
                {
                    string label = arguments[1];
                    if (!Identifier(label) || !sdkPack.Contains(label)) throw Refuse("unmatched SDK pragma-pack label");
                    while (sdkPack.Pop() != label) { }
                }
            }
        }
    }

    private static bool HasMacroOperator(string directive)
    {
        // ReadDirectives begins at the directive's own '#'. Every remaining
        // nonliteral hash, including its digraph, is an unproved macro operator.
        for (int index = 1; index < directive.Length;)
        {
            if (NativeMacroDirectiveReader.CommentEnd(directive, index) is int comment) index = comment;
            else if (NativeMacroDirectiveReader.LiteralEnd(directive, index) is int literal) index = literal;
            else if (directive[index] == '#' || directive.AsSpan(index).StartsWith("%:", StringComparison.Ordinal)) return true;
            else index++;
        }
        return false;
    }

    private static string WithoutComments(string text)
    {
        StringBuilder result = new();
        for (int index = 0; index < text.Length;)
            if (NativeMacroDirectiveReader.CommentEnd(text, index) is int end) { result.Append(' '); index = end; }
            else result.Append(text[index++]);
        return result.ToString();
    }
    private static bool Identifier(string text) => text.Length != 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_')
        && text.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    private static bool DynamicBuiltin(string name) => name.StartsWith("__has_", StringComparison.Ordinal)
        || name is "_Pragma" or "__BASE_FILE__" or "__COUNTER__" or "__DATE__" or "__FILE_NAME__" or "__FILE__"
        or "__FLT_EVAL_METHOD__" or "__INCLUDE_LEVEL__" or "__LINE__" or "__MODULE__" or "__TIMESTAMP__" or "__TIME__"
        or "__building_module" or "__identifier" or "__is_identifier" or "__is_target_arch" or "__is_target_environment"
        or "__is_target_os" or "__is_target_variant_environment" or "__is_target_variant_os" or "__is_target_vendor" or "__pragma";

    private static IEnumerable<string> Words(string text)
    {
        for (int index = 0; index < text.Length;)
        {
            if (NativeMacroDirectiveReader.CommentEnd(text, index) is int comment) { index = comment; continue; }
            if (NativeMacroDirectiveReader.LiteralEnd(text, index) is int literal) { index = literal; continue; }
            if (NativeMacroDirectiveReader.WordEnd(text, index) is int end) { yield return text[index..end]; index = end; }
            else index++;
        }
    }
    private static bool HasSemantic(string text)
    {
        for (int index = 0; index < text.Length;)
        {
            if (char.IsWhiteSpace(text[index])) index++;
            else if (NativeMacroDirectiveReader.CommentEnd(text, index) is int comment) index = comment;
            else return true;
        }
        return false;
    }
    private static bool Pseudo(string path) => path.Length == 0 || path.StartsWith('<');
    private static string Normalize(string path) => Pseudo(path) ? path : Path.GetFullPath(path);
    private static string Escape(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static NotSupportedException Refuse(string reason) => new("The GNU declaration prelude cannot prove " + reason + ". Preserve the complete native source; this include/state shape needs an explicit native frontend.");
}
