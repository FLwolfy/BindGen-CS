using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Requests;
using BGCS.Infrastructure.IO;
using BGCS.Frontend.Clang.Parsing.Arguments;

namespace BGCS.Frontend.Clang.Parsing;

internal static class FrontendArgumentValidator
{
    internal static void Validate(FrontendRequest request)
    {
        IEnumerable<string> arguments = request.arguments.Concat(request.target.toolchain.arguments);
        NativeArgumentIdentityValidator.Validate(arguments, "frontend", rejectParserSubstitution: true);
        if (arguments.Any(argument => argument == "-funsigned-bitfields" || argument == "/clang:-funsigned-bitfields"))
            throw new NotSupportedException("The pinned Clang frontend does not implement unsigned plain-int bitfield semantics; ignoring -funsigned-bitfields would produce an incorrect binding contract.");
    }
}
