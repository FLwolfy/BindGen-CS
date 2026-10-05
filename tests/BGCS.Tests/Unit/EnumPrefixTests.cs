using System;
using System.Collections.Generic;
using BGCS.Configuration.Mapping;
using Xunit;

namespace BGCS.Tests.Unit;

public sealed class EnumPrefixTests
{
    [Fact]
    public void Prefix_CopiesCallerSegmentsAndRejectsExternalMutation()
    {
        string[] segments = ["NATIVE", "MODE"];
        EnumPrefix prefix = new(segments);
        segments[0] = "CHANGED";

        Assert.Equal("NATIVE", prefix.parts[0]);
        IList<string> list = Assert.IsAssignableFrom<IList<string>>(prefix.parts);
        Assert.Throws<NotSupportedException>(() => list[0] = "CHANGED");
        Assert.Empty(default(EnumPrefix).parts);
    }

    [Fact]
    public void Prefix_RejectsNullSegments()
    {
        Assert.Throws<ArgumentNullException>(() => new EnumPrefix([null!]));
        Assert.Throws<ArgumentNullException>(() => new EnumPrefix(null!));
    }
}
