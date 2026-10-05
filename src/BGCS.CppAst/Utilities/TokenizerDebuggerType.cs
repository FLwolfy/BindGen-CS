using System.Diagnostics;

namespace BGCS.CppAst.Utilities;

/// <summary>
/// Defines the public class <c>TokenizerDebuggerType</c>.
/// </summary>
internal class TokenizerDebuggerType
{
    private readonly Tokenizer m_tokenizer;
    /// <summary>
    /// Initializes a new instance of <see cref = "TokenizerDebuggerType"/>.
    /// </summary>
    public TokenizerDebuggerType(Tokenizer tokenizer)
    {
        this.m_tokenizer = tokenizer;
    }

    /// <summary>
    /// Exposes public member <c>Items</c>.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public object[] items
    {
        get
        {
            var array = new object[this.m_tokenizer.count];
            for (int i = 0; i < this.m_tokenizer.count; i++)
            {
                array[i] = this.m_tokenizer[i];
            }

            return array;
        }
    }
}
