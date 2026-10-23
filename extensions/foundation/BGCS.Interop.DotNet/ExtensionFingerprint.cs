using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Plugins;

namespace BGCS.Interop.DotNet;

internal static class ExtensionFingerprint
{
    internal static CacheFingerprintContribution Capture(
        Type service,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        string state = service.FullName + "/v1/" + service.Assembly.ManifestModule.ModuleVersionId.ToString("D");
        return new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)))), []);
    }
}
