using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

public sealed class SessionStateTests
{
    [Fact]
    public void NotifyVersionInstalled_RaisesEventWithVersionId()
    {
        var session = new SessionState();
        string? raisedId = null;
        session.VersionInstalled += (_, versionId) => raisedId = versionId;

        session.NotifyVersionInstalled("1.20.1");

        Assert.Equal("1.20.1", raisedId);
    }
}
