using System;
using System.Text.Json;
using BGCS.Core.Hosting;
using BGCS.Hosting.Standard;

namespace BGCS.Worker.Tests;

public abstract class WorkerTestHost : IDisposable
{
    protected HostServices host { get; } = new HostServicesProvider().Create(new(AppContext.BaseDirectory, JsonSerializer.SerializeToElement(new { })));
    public virtual void Dispose() => host.Dispose();
}
