using System.IO;
using Mark.Designer.ViewModels;
using Mark.Licensing.Api;
using Mark.Licensing.Client;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>
/// A new copy of MARK signs in to the licence server shipped with it (licence-server.txt from the installer build); when
/// the server cannot be reached, the sign-in page opens Connection and says why.
/// </summary>
public class ServerAddressTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mark-tests", "server-" + Guid.NewGuid().ToString("N"));

    public ServerAddressTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void TheShippedAddress_IsUsed_UntilAnotherIsEntered()
    {
        Assert.Equal(LicenceDefaults.ServerUrl, LicenceDefaults.ServerUrlIn(_folder));     // no file: the built-in one
        File.WriteAllLines(Path.Combine(_folder, LicenceDefaults.ServerFileName), new[] { "# MARK licence server", "https://licence.example.com/" });
        string shipped = LicenceDefaults.ServerUrlIn(_folder);
        Assert.Equal("https://licence.example.com", shipped);

        using var server = new TestServer();
        var manager = new LicenceManager(new MemoryStateStore(), server.Verifier, "PC-1", "Office", _ => new DirectApi(server.Service),
            defaultServerUrl: shipped);
        Assert.Equal(shipped, manager.ServerUrl);
        Assert.Equal(shipped, new SignInViewModel(manager).ServerUrl);
    }

    [Fact]
    public void AServerThatCannotBeReached_OpensConnection()
    {
        using var server = new TestServer();
        var manager = new LicenceManager(new MemoryStateStore(), server.Verifier, "PC-1", "Office", url => new LicenceApiClient(url),
            defaultServerUrl: "http://127.0.0.1:1");
        var signIn = new SignInViewModel(manager) { UserId = "someone", Password = "secret" };
        Assert.False(signIn.ShowConnection);

        signIn.SignInAsync().GetAwaiter().GetResult();

        Assert.True(signIn.ShowConnection);
        Assert.Contains("Connection", signIn.Error);
        Assert.Contains("127.0.0.1:1", signIn.Error);
    }
}
