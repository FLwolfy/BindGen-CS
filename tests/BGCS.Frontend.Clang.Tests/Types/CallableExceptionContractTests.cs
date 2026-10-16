using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class CallableExceptionContractTests
{
    [Fact]
    public async Task NativeVirtualQualifiers_PreserveEvaluatedExceptionAndVolatileReferenceFacts()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-callable-qualifiers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "qualifiers.hpp");
            await File.WriteAllTextAsync(header, """
                class Slots {
                public:
                    virtual int Throwing(int& value) volatile & noexcept(false) = 0;
                    virtual int NoThrow() const noexcept = 0;
                    virtual int EvaluatedFalse() noexcept(sizeof(int) == 0) = 0;
                    virtual int EvaluatedTrue() noexcept(sizeof(int) > 0) = 0;
                };
                template<class T> class Dependent {
                public:
                    virtual int Unresolved() noexcept(sizeof(T) > 0) = 0;
                };
                """);
            NativeModule module = (await new ClangFrontend().ParseAsync(new(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, [header])
            { pathBase = root, parseMacros = false }, CancellationToken.None)).module;

            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration throwing = Function("Throwing");
            Assert.False(throwing.isNoThrow);
            Assert.True(throwing.isVolatile);
            Assert.Equal(NativeReferenceQualifier.LValue, throwing.referenceQualifier);
            Assert.True(Function("NoThrow").isNoThrow);
            Assert.True(Function("NoThrow").isConst);
            Assert.False(Function("EvaluatedFalse").isNoThrow);
            Assert.True(Function("EvaluatedTrue").isNoThrow);
            Assert.Null(Function("Unresolved").isNoThrow);

            NativeDeclaration Function(string name) => Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
