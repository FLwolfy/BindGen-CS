namespace BGCS.Language
{
    using System;

    /// <summary>
    /// Defines the public class <c>ParserContext</c>.
    /// </summary>
    public class ParserContext
    {
        /// <summary>
        /// Exposes public member <c>diagnostics</c>.
        /// </summary>
        public readonly DiagnosticBag diagnostics;

        private readonly RootNode root;
        private readonly ParserOptions options;
        private readonly IList<ISyntaxAnalyzer> analyzers;
        private readonly IList<Token> tokens;
        private readonly Stack<SyntaxNode> scopeStack = new();

        private int currentTokenIndex;
        private int localTokenIndex;

        private SyntaxNode current;

        private SyntaxNode? last;

        /// <summary>
        /// Initializes a new instance of <see cref="ParserContext"/>.
        /// </summary>
        public ParserContext(RootNode root, ParserOptions options, IList<ISyntaxAnalyzer> analyzers, IList<Token> tokens, DiagnosticBag diagnostics)
        {
            this.diagnostics = diagnostics;
            this.root = root;
            this.options = options;
            this.analyzers = analyzers;
            this.tokens = tokens;
            current = root;
            last = root;
        }

        /// <summary>
        /// Exposes public member <c>diagnostics.HasErrors</c>.
        /// </summary>
        public bool HasError => diagnostics.HasErrors;

        /// <summary>
        /// Exposes public member <c>diagnostics</c>.
        /// </summary>
        public DiagnosticBag Diagnostics => diagnostics;

        /// <summary>
        /// Exposes public member <c>root</c>.
        /// </summary>
        public SyntaxNode Root => root;

        /// <summary>
        /// The current scope.
        /// </summary>
        public SyntaxNode Current => current;

        /// <summary>
        /// The current node that has been added.
        /// </summary>
        public SyntaxNode? Last => last;

        /// <summary>
        /// Exposes public member <c>scopeStack</c>.
        /// </summary>
        public Stack<SyntaxNode> ScopeStack => scopeStack;

        /// <summary>
        /// Exposes public member <c>tokens[currentTokenIndex]</c>.
        /// </summary>
        public Token CurrentToken => tokens[currentTokenIndex];

        /// <summary>
        /// Exposes public member <c>currentTokenIndex</c>.
        /// </summary>
        public int CurrentTokenIndex => currentTokenIndex;

        /// <summary>
        /// Exposes public member <c>tokens.Count</c>.
        /// </summary>
        public int TokenCount => tokens.Count;

        /// <summary>
        /// Exposes public member <c>tokens.Count</c>.
        /// </summary>
        public bool IsEnd => currentTokenIndex >= tokens.Count;

        /// <summary>
        /// Exposes public member <c>index]</c>.
        /// </summary>
        public Token this[int index]
        {
            get => tokens[index];
        }

        /// <summary>
        /// Checks if the given <paramref name="index"/> is in valid range.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public bool InBounds(int index)
        {
            return unchecked((uint)index) < tokens.Count;
        }

        /// <summary>
        /// Checks if the CurrentTokenIndex + <paramref name="offset"/> is in valid range.
        /// </summary>
        /// <param name="offset"></param>
        /// <returns></returns>
        public bool SeekInBounds(int offset)
        {
            return unchecked((uint)(currentTokenIndex + offset)) < tokens.Count;
        }

        /// <summary>
        /// Pushes the current scope onto the stack and sets the new scope. <br/>
        /// WARNING: this will automatically append the node to the current scope
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public SyntaxNode PushScope(SyntaxNode node)
        {
            current.AddChild(node);
            scopeStack.Push(current);
            current = node;
            return node;
        }

        /// <summary>
        /// Pops the last scope from the stack and sets the last node as the popped scope.
        /// </summary>
        /// <param name="location">the location of the causing token (used if the operation fails)</param>
        /// <returns>true if the operation was successfully, otherwise false.<br/>
        /// WARNING: exit parse immediately after false is returned, false indicates that the scope open and close are unbalanced.</returns>
        public bool PopScope(SourceLocation? location = null)
        {
            if (scopeStack.Count == 0)
            {
                diagnostics.Error("Syntax error", location);
                return false;
            }
            current = scopeStack.Pop();
            last = current;
            return true;
        }

        /// <summary>
        /// Appends <paramref name="node"/> to the current scope.
        /// </summary>
        /// <param name="node"></param>
        public void AppendNode(SyntaxNode node)
        {
            if (node == null)
                return;
            current.AddChild(node);
            last = node;
        }

        /// <summary>
        /// Increments the CurrentTokenIndex.
        /// </summary>
        public void MoveNext()
        {
            currentTokenIndex++;
        }

        /// <summary>
        /// Decrements the CurrentTokenIndex.
        /// </summary>
        public void MoveBack()
        {
            currentTokenIndex--;
        }

        /// <summary>
        /// Gets a token by the given <paramref name="offset"/> without moving the pointer.
        /// </summary>
        /// <param name="offset">the offset</param>
        /// <returns>the token</returns>
        public Token Seek(int offset)
        {
            return tokens[currentTokenIndex + offset];
        }

        /// <summary>
        /// Executes public operation <c>CurrentCompare</c>.
        /// </summary>
        public bool CurrentCompare(Func<Token, bool> compare)
        {
            return InBounds(currentTokenIndex) && compare(CurrentToken);
        }

        /// <summary>
        /// Gets a token by the given <paramref name="offset"/> without moving the pointer.
        /// </summary>
        /// <param name="offset">the offset</param>
        /// <returns>the token</returns>
        public bool SeekCompare(int offset, Func<Token, bool> compare)
        {
            return SeekInBounds(offset) && compare(Seek(offset));
        }

        /// <summary>
        /// Tries to get the current token and increments the CurrentTokenIndex.
        /// </summary>
        /// <param name="current">the current token.</param>
        /// <returns>false if is at end, true if operation was successfully done</returns>
        public bool TryMoveNext(out Token current)
        {
            current = default;
            if (IsEnd)
                return false;
            current = tokens[currentTokenIndex];
            currentTokenIndex++;
            return true;
        }

        /// <summary>
        /// Moves the current token pointer to <paramref name="index"/>
        /// </summary>
        /// <param name="index">new offset</param>
        public void MoveTo(int index)
        {
            currentTokenIndex = index;
        }

        /// <summary>
        /// Offsets the current token offset by <paramref name="offset"/>
        /// </summary>
        /// <param name="offset">the offset</param>
        public void Offset(int offset)
        {
            currentTokenIndex += offset;
        }

        /// <summary>
        /// Tries to analyze the current token/s.
        /// </summary>
        /// <returns>true if the token/s had been analysed, false if an error occurred or it's an unknown token (also an error)</returns>
        public AnalyserResult AnalyzeCurrent()
        {
            AnalyserResult success = AnalyserResult.Unrecognised;
            for (int j = 0; j < analyzers.Count; j++)
            {
                var analyzer = analyzers[j];
                var result = analyzer.Analyze(this);
                if (result == AnalyserResult.Success)
                {
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
                diagnostics.Error($"Syntax Error: Unknown token '{CurrentToken.AsString()}' ({CurrentToken.Type})", CurrentToken.Location);
            }

            return success;
        }

        /// <summary>
        /// Analyses all token/s in a scope then returns.
        /// </summary>
        /// <param name="scope">the scope node</param>
        /// <returns>true if no error occurred, otherwise false</returns>
        public AnalyserResult AnalyseScoped(SyntaxNode scope)
        {
            if (IsEnd)
            {
                diagnostics.Error("Syntax Error: { expected");
                return AnalyserResult.Error;
            }

            if (!CurrentToken.IsPunctuation || CurrentToken != '{')
            {
                diagnostics.Error("Syntax Error: { expected", CurrentToken.Location);
                return AnalyserResult.Error;
            }

            MoveNext();
            PushScope(scope);

            while (!IsEnd)
            {
                if (CurrentToken.IsPunctuation && CurrentToken == '}')
                {
                    MoveNext();
                    PopScope();
                    return AnalyserResult.Success;
                }

                if (!options.ParseComments && CurrentToken.IsComment)
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

            diagnostics.Error("Syntax Error: } expected", IsEnd ? null : CurrentToken.Location);
            return AnalyserResult.Error;
        }

        /// <summary>
        /// Analyses all token/s in the file scope then returns.
        /// </summary>
        /// <param name="scope">the scope node</param>
        /// <returns>true if no error occurred, otherwise false</returns>
        public AnalyserResult AnalyseFileScoped(SyntaxNode scope)
        {
            PushScope(scope);

            while (!IsEnd)
            {
                if (!options.ParseComments && CurrentToken.IsComment)
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

            return AnalyserResult.Success;
        }
    }
}
