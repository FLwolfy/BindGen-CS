using System;
using System.Text.Json;
using BGCS.Core.Hosting;
using BGCS.Hosting.Standard;

namespace BGCS.Infrastructure.Tests;

internal static class HostTestPaths
{
    internal static IHostPathService Create()
        => new HostServicesProvider().Create(new(AppContext.BaseDirectory, JsonSerializer.SerializeToElement(new { }))).paths;
}
