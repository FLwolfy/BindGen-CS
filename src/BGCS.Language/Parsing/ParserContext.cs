using System.Collections.Generic;
using BGCS.Language.Diagnostics;
using BGCS.Language.Lexing;
using BGCS.Language.Syntax;

namespace BGCS.Language.Parsing
{
    using System;

    /// <summary>
    /// Coordinates the token cursor, syntax scopes, diagnostics, and ordered analyzers of one parse operation.
    /// </summary>
    public class ParserContext
    {
        private readonly DiagnosticBag m_diagnostics;
        private readonly RootNode m_root;
        private readonly ParserOptions m_options;
        private readonly IList<ISyntaxAnalyzer> m_analyzers;
        private readonly IList<Token> m_tokens;
        private readonly Stack<SyntaxNode> m_scopeStack = new();
        private int m_currentTokenIndex;
        private SyntaxNode m_current;
        private SyntaxNode? m_last;
        /// <summary>
        /// Retains the mutable objects used by a single parser invocation.
        /// </summary>
        /// <param name="root">
        /// The document root to receive top-level nodes.
        /// </param>
        /// <param name="options">
        /// The policy controlling scoped comment handling.
        /// </param>
        /// <param name="analyzers">
        /// The ordered analyzers dispatched by this context.
        /// </param>
        /// <param name="tokens">
        /// The token stream addressed by the cursor.
        /// </param>
        /// <param name="diagnostics">
        /// The diagnostic bag to receive analysis failures.
        /// </param>
        public ParserContext(
            RootNode root,
            ParserOptions options,
            IList<ISyntaxAnalyzer> analyzers,
            IList<Token> tokens,
            DiagnosticBag diagnostics
        ) {
            m_diagnostics = diagnostics;
            this.m_root = root;
            this.m_options = options;
            this.m_analyzers = analyzers;
            this.m_tokens = tokens;
            this.m_current = root;
            this.m_last = root;
        }

        /// <summary>
        /// Gets whether this invocation has reported an error diagnostic.
        /// </summary>
        public bool hasError => diagnostics.hasErrors;
        /// <summary>
        /// Gets the diagnostic bag shared by this invocation's analyzers.
        /// </summary>
        public DiagnosticBag diagnostics => m_diagnostics;
        /// <summary>
        /// Gets the document root receiving top-level declarations.
        /// </summary>
        public SyntaxNode root => this.m_root;
        /// <summary>
        /// The current scope.
        /// </summary>
        public SyntaxNode current => this.m_current;
        /// <summary>
        /// The current node that has been added.
        /// </summary>
        public SyntaxNode? last => this.m_last;
        /// <summary>
        /// Gets the number of enclosing scopes retained by this parse operation.
        /// </summary>
        public int scopeDepth => m_scopeStack.Count;
        /// <summary>
        /// Gets the token at the current cursor; callers must first check isEnd or InBounds.
        /// </summary>
        public Token currentToken => this.m_tokens[this.m_currentTokenIndex];
        /// <summary>
        /// Gets the zero-based token cursor, which can also point before or after the stream.
        /// </summary>
        public int currentTokenIndex => this.m_currentTokenIndex;
        /// <summary>
        /// Gets the number of tokens retained by this invocation.
        /// </summary>
        public int tokenCount => this.m_tokens.Count;
        /// <summary>
        /// Gets whether the cursor has reached or passed the end of the token stream.
        /// </summary>
        public bool isEnd => this.m_currentTokenIndex >= this.m_tokens.Count;

        /// <summary>
        /// Reads an absolute token position without changing the cursor.
        /// </summary>
        /// <param name="index">
        /// The zero-based token position.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The position is outside the token stream.
        /// </exception>
        public Token this[int index] { get => this.m_tokens[index]; }

        /// <summary>
        /// Checks an absolute token position without accessing the stream.
        /// </summary>
        /// <param name="index">
        /// The zero-based position, including negative candidates.
        /// </param>
        /// <returns>
        /// True when the position identifies an existing token; otherwise false.
        /// </returns>
        public bool InBounds(int index)
        {
            return unchecked((uint)index) < this.m_tokens.Count;
        }

        /// <summary>
        /// Checks a cursor-relative token position without moving the cursor.
        /// </summary>
        /// <param name="offset">
        /// The signed displacement from the current token cursor.
        /// </param>
        /// <returns>
        /// True when the displaced position identifies an existing token; otherwise false.
        /// </returns>
        public bool SeekInBounds(int offset)
        {
            long position = (long)m_currentTokenIndex + offset;
            return position >= 0 && position < m_tokens.Count;
        }

        /// <summary>
        /// Appends a scope node to the current scope and makes it the active scope.
        /// </summary>
        /// <param name="node">
        /// The new scope node receiving subsequent declarations.
        /// </param>
        /// <returns>
        /// The same node, now used as the current scope.
        /// </returns>
        public SyntaxNode PushScope(SyntaxNode node)
        {
            this.m_current.AddChild(node);
            this.m_scopeStack.Push(this.m_current);
            this.m_current = node;
            return node;
        }

        /// <summary>
        /// Pops the last scope from the stack and sets the last node as the popped scope.
        /// </summary>
        /// <param name = "location">the location of the causing token (used if the operation fails)</param>
        /// <returns>true if the operation was successfully, otherwise false.<br/>
        /// WARNING: exit parse immediately after false is returned, false indicates that the scope open and close are unbalanced.</returns>
        public bool PopScope(SourceLocation? location = null)
        {
            if (this.m_scopeStack.Count == 0)
            {
                diagnostics.Error("Syntax error", location);
                return false;
            }

            this.m_current = this.m_scopeStack.Pop();
            this.m_last = this.m_current;
            return true;
        }

        /// <summary>
        /// Appends a non-null node to the current scope and records it as the last appended node.
        /// </summary>
        /// <param name="node">
        /// The node to append; null is ignored.
        /// </param>
        public void AppendNode(SyntaxNode node)
        {
            if (node == null)
                return;
            this.m_current.AddChild(node);
            this.m_last = node;
        }

        /// <summary>
        /// Increments the CurrentTokenIndex.
        /// </summary>
        public void MoveNext()
        {
            this.m_currentTokenIndex++;
        }

        /// <summary>
        /// Decrements the CurrentTokenIndex.
        /// </summary>
        public void MoveBack()
        {
            this.m_currentTokenIndex--;
        }

        /// <summary>
        /// Gets a token by the given <paramref name = "offset"/> without moving the pointer.
        /// </summary>
        /// <param name = "offset">the offset</param>
        /// <returns>the token</returns>
        public Token Seek(int offset)
        {
            return this.m_tokens[this.m_currentTokenIndex + offset];
        }

        /// <summary>
        /// Evaluates a predicate only when the current cursor identifies an existing token.
        /// </summary>
        /// <param name="compare">
        /// The predicate invoked once when a token is available.
        /// </param>
        /// <returns>
        /// The predicate result, or false when the cursor is out of bounds.
        /// </returns>
        public bool CurrentCompare(Func<Token, bool> compare)
        {
            return InBounds(this.m_currentTokenIndex) && compare(this.currentToken);
        }

        /// <summary>
        /// Gets a token by the given <paramref name = "offset"/> without moving the pointer.
        /// </summary>
        /// <param name = "offset">the offset</param>
        /// <param name="compare">Predicate evaluated when a token exists at the requested offset.</param>
        /// <returns>True when the token exists and satisfies the predicate; otherwise false.</returns>
        public bool SeekCompare(
            int offset,
            Func<Token, bool> compare
        ) {
            return SeekInBounds(offset) && compare(Seek(offset));
        }

        /// <summary>
        /// Tries to get the current token and increments the CurrentTokenIndex.
        /// </summary>
        /// <param name = "current">the current token.</param>
        /// <returns>false if is at end, true if operation was successfully done</returns>
        public bool TryMoveNext(out Token current)
        {
            current = default;
            if (!InBounds(m_currentTokenIndex))
                return false;
            current = this.m_tokens[this.m_currentTokenIndex];
            this.m_currentTokenIndex++;
            return true;
        }

        /// <summary>
        /// Moves the current token pointer to <paramref name = "index"/>
        /// </summary>
        /// <param name = "index">new offset</param>
        public void MoveTo(int index)
        {
            this.m_currentTokenIndex = index;
        }

        /// <summary>
        /// Offsets the current token offset by <paramref name = "offset"/>
        /// </summary>
        /// <param name = "offset">the offset</param>
        public void Offset(int offset)
        {
            this.m_currentTokenIndex += offset;
        }

        /// <summary>
        /// Tries to analyze the current token/s.
        /// </summary>
        /// <returns>true if the token/s had been analysed, false if an error occurred or it's an unknown token (also an error)</returns>
        public AnalyserResult AnalyzeCurrent()
        {
            int startingPosition = m_currentTokenIndex;
            AnalyserResult success = AnalyserResult.Unrecognised;
            for (int j = 0; j < this.m_analyzers.Count; j++)
            {
                var analyzer = this.m_analyzers[j];
                var result = analyzer.Analyze(this);
                if (result == AnalyserResult.Success)
                {
                    if (m_currentTokenIndex <= startingPosition)
                    {
                        diagnostics.Error("Parser made no forward progress.");
                        return AnalyserResult.Error;
                    }
                    success = AnalyserResult.Success;
                    break;
                }
                else if (result == AnalyserResult.Error)
                {
                    return AnalyserResult.Error;
                }
            }

            if (success == AnalyserResult.Unrecognised)
            {
                diagnostics.Error($"Syntax Error: Unknown token '{this.currentToken.AsString()}' ({this.currentToken.type})", this.currentToken.location);
            }

            return success;
        }

        /// <summary>
        /// Analyses all token/s in a scope then returns.
        /// </summary>
        /// <param name = "scope">the scope node</param>
        /// <returns>true if no error occurred, otherwise false</returns>
        public AnalyserResult AnalyseScoped(SyntaxNode scope)
        {
            if (this.isEnd)
            {
                diagnostics.Error("Syntax Error: { expected");
                return AnalyserResult.Error;
            }

            if (!this.currentToken.isPunctuation || this.currentToken != '{')
            {
                diagnostics.Error("Syntax Error: { expected", this.currentToken.location);
                return AnalyserResult.Error;
            }

            MoveNext();
            PushScope(scope);
            while (!this.isEnd)
            {
                if (this.currentToken.isPunctuation && this.currentToken == '}')
                {
                    MoveNext();
                    PopScope();
                    return AnalyserResult.Success;
                }

                if (!this.m_options.parseComments && this.currentToken.isComment)
                {
                    MoveNext();
                    continue;
                }

                var result = AnalyzeCurrent();
                if (result != AnalyserResult.Success)
                {
                    return result;
                }
            }

            diagnostics.Error("Syntax Error: } expected", this.isEnd ? null : this.currentToken.location);
            return AnalyserResult.Error;
        }

        /// <summary>
        /// Analyses all token/s in the file scope then returns.
        /// </summary>
        /// <param name = "scope">the scope node</param>
        /// <returns>true if no error occurred, otherwise false</returns>
        public AnalyserResult AnalyseFileScoped(SyntaxNode scope)
        {
            PushScope(scope);
            while (!this.isEnd)
            {
                if (!this.m_options.parseComments && this.currentToken.isComment)
                {
                    MoveNext();
                    continue;
                }

                var result = AnalyzeCurrent();
                if (result != AnalyserResult.Success)
                {
                    return result;
                }
            }

            return PopScope() ? AnalyserResult.Success : AnalyserResult.Error;
        }
    }
}
