using System;
using System.Collections.Generic;
using System.Threading;
using BGCS.Contracts.Common;
using BGCS.Contracts.Requests;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Advanced;

/// <summary>Provides explicitly scoped native Clang access; it cannot be used after its contributor returns.</summary>
public sealed class ClangFactContributionContext
{
    private readonly CXTranslationUnit m_translationUnit;
    private readonly IReadOnlyDictionary<StableId, CXCursor> m_declarations;
    private readonly Func<CXCursor, SourceLocation> m_location;
    private int m_active = 1;

    internal ClangFactContributionContext(
        CXTranslationUnit translationUnit,
        FrontendRequest request,
        IReadOnlyDictionary<StableId, CXCursor> declarations,
        Func<CXCursor, SourceLocation> location
    ) {
        m_translationUnit = translationUnit;
        this.request = request;
        m_declarations = declarations;
        m_location = location;
    }

    /// <summary>Gets the frozen frontend inputs; this property never exposes a host service.</summary>
    public FrontendRequest request { get; }
    /// <summary>Gets the borrowed translation unit; copying a handle does not extend its native lifetime.</summary>
    public CXTranslationUnit translationUnit { get { EnsureActive(); return m_translationUnit; } }

    /// <summary>Resolves an original detached declaration to its exact borrowed Clang cursor.</summary>
    /// <param name="declaration">The original stable declaration identity.</param>
    /// <param name="cursor">The matching borrowed cursor, or default for a synthetic or newly contributed declaration.</param>
    /// <returns>True only for an established original cursor.</returns>
    public bool TryGetDeclarationCursor(
        StableId declaration,
        out CXCursor cursor
    ) {
        EnsureActive();
        return m_declarations.TryGetValue(declaration, out cursor);
    }

    /// <summary>Maps a borrowed cursor location using the same original-source rules as the detached model.</summary>
    /// <param name="cursor">A cursor belonging to the current translation unit.</param>
    /// <returns>The logical or presumed original source location.</returns>
    public SourceLocation GetSourceLocation(CXCursor cursor) { EnsureActive(); return m_location(cursor); }

    internal void Invalidate() => Interlocked.Exchange(ref m_active, 0);
    private void EnsureActive() => ObjectDisposedException.ThrowIf(Volatile.Read(ref m_active) == 0, this);
}
