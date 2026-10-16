using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;

namespace BGCS.Configuration;

internal sealed class RemoteConfigurationReader(HttpClient? client)
{
    internal const int C_DOCUMENT_LIMIT = 16 * 1024 * 1024;
    private static readonly HttpClient s_client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    { Timeout = Timeout.InfiniteTimeSpan };

    internal async Task<byte[]> ReadAsync(
        Uri original,
        ContentDigest expected,
        CancellationToken cancellationToken
    ) {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        Uri current = original;
        try
        {
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                ValidateAddress(current);
                using HttpRequestMessage request = new(HttpMethod.Get, current);
                using HttpResponseMessage response = await (client ?? s_client).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                // A caller-owned transport can implement redirects itself. Its final
                // address must still satisfy the same HTTPS input contract.
                if (response.RequestMessage?.RequestUri is Uri final) ValidateAddress(final);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (redirects == 5 || response.Headers.Location is not Uri location)
                        throw new IOException("Remote configuration redirects exceed the limit or omit a destination.");
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > C_DOCUMENT_LIMIT)
                    throw new IOException("A remote configuration exceeds the 16 MiB input limit.");
                await using Stream source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                byte[] content = await ReadBoundedAsync(source, deadline.Token).ConfigureAwait(false);
                ContentDigest actual = new(Convert.ToHexStringLower(SHA256.HashData(content)));
                if (actual != expected) throw new IOException("The remote configuration bytes do not match their pinned SHA-256 content identity.");
                return content;
            }
            throw new IOException("Remote configuration redirects exceed the limit.");
        }
        catch (OperationCanceledException failure) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw new TimeoutException("Reading the pinned HTTPS configuration exceeded 30 seconds.", failure); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException(cancellationToken); }
    }

    internal static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        CancellationToken cancellationToken
    ) {
        using MemoryStream content = new();
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            int count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) return content.ToArray();
            if (content.Length + count > C_DOCUMENT_LIMIT) throw new IOException("A binding configuration exceeds the 16 MiB input limit.");
            content.Write(buffer, 0, count);
        }
    }

    private static void ValidateAddress(Uri address)
    {
        if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps || address.Fragment.Length != 0 || address.UserInfo.Length != 0)
            throw new IOException("Remote configuration inputs and redirects require HTTPS addresses without fragments or embedded credentials.");
    }
}
