using System.IO;
using PhoneMirror.Services;

namespace PhoneMirror.Tests;

/// <summary>
/// La lista de direcciones Wi-Fi, siempre sobre un fichero temporal (nunca el wifi.json de verdad).
/// </summary>
public sealed class WifiAddressesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-wifi-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_dir, "sub", "wifi.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Promote_PutsFirstWithoutDuplicates_IgnoringCase()
    {
        Assert.Equal(["c", "a", "b"], WifiAddresses.Promote(["a", "b", "c"], "c"));
        Assert.Equal(["x", "a"], WifiAddresses.Promote(["a"], "x"));
        Assert.Equal(["Movil.Local:5555", "a"], WifiAddresses.Promote(["movil.local:5555", "a"], "Movil.Local:5555"));
        Assert.Equal(["a"], WifiAddresses.Promote([], "a"));
    }

    [Fact]
    public void Promote_KeepsAtMostEight()
    {
        var list = Enumerable.Range(1, 8).Select(i => $"10.0.0.{i}").ToList();

        var result = WifiAddresses.Promote(list, "10.0.0.99");

        Assert.Equal(8, result.Count);
        Assert.Equal("10.0.0.99", result[0]);
        Assert.DoesNotContain("10.0.0.8", result);
    }

    [Fact]
    public void Without_RemovesAllMatchesIgnoringCase()
    {
        Assert.Equal(["b"], WifiAddresses.Without(["A", "b", "a"], "a"));
        Assert.Equal(["a"], WifiAddresses.Without(["a"], "z"));
    }

    [Fact]
    public void Load_MissingFile_IsEmpty() => Assert.Empty(WifiAddresses.Load(FilePath));

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("{\"a\":1}")]
    [InlineData("null")]
    [InlineData("")]
    public void Load_BrokenFile_IsEmpty(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);

        Assert.Empty(WifiAddresses.Load(FilePath));
    }

    [Fact]
    public void RememberAndForget_RoundTripThroughTheFile()
    {
        WifiAddresses.Remember(FilePath, "192.168.1.40");
        WifiAddresses.Remember(FilePath, "192.168.1.41:5555");
        WifiAddresses.Remember(FilePath, "192.168.1.40");

        Assert.True(File.Exists(FilePath));
        Assert.Equal(["192.168.1.40", "192.168.1.41:5555"], WifiAddresses.Load(FilePath));

        WifiAddresses.Forget(FilePath, "192.168.1.40");
        Assert.Equal(["192.168.1.41:5555"], WifiAddresses.Load(FilePath));

        WifiAddresses.Forget(FilePath, "no-estaba");
        Assert.Equal(["192.168.1.41:5555"], WifiAddresses.Load(FilePath));
    }

    [Fact]
    public void Save_WhereItCannotWrite_DoesNotThrow()
    {
        // La «carpeta» del fichero es un fichero: no se puede crear.
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "bloqueo");
        File.WriteAllText(blocker, "x");
        var path = Path.Combine(blocker, "wifi.json");

        WifiAddresses.Remember(path, "10.0.0.1");

        Assert.Empty(WifiAddresses.Load(path));
    }
}
