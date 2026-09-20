using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Renci.SshNet;
using TunnelDeck.Core.Security;
using TunnelDeck.Infrastructure.Security;

namespace TunnelDeck.Ssh.Tests.Fixture;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SshCollection : ICollectionFixture<SshFixture>
{
    public const string Name = "Isolated OpenSSH";
}

public sealed class SshFixture : IDisposable
{
    private readonly HttpClient _http;
    public FixtureManifest Manifest { get; }

    public SshFixture()
    {
        string? path = Environment.GetEnvironmentVariable("TUNNELDECK_SSH_FIXTURE");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException("Real SSH tests require an isolated fixture. Run scripts/Test.ps1 -Ssh; these tests are never silently skipped.");
        }

        Manifest = JsonSerializer.Deserialize<FixtureManifest>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The SSH fixture manifest is empty.");
        _http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri($"http://{Manifest.Host}:{Manifest.ControlPort}/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Manifest.ControlToken);
    }

    public TestConnection CreateConnection(int? port = null, bool privateKey = false, bool trust = true,
        string? fingerprint = null, TimeSpan? timeout = null)
    {
        int sshPort = port ?? Manifest.AllowPort;
        PrivateKeyFile? key = privateKey ? new PrivateKeyFile(Manifest.PrivateKeyPath, Manifest.PrivateKeyPassphrase) : null;
        AuthenticationMethod authentication = key is null
            ? new PasswordAuthenticationMethod(Manifest.Username, Manifest.Password)
            : new PrivateKeyAuthenticationMethod(Manifest.Username, key);
        var info = new ConnectionInfo(Manifest.Host, sshPort, Manifest.Username, authentication)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(3),
        };
        var client = new SshClient(info);
        HostKeyIdentity? trusted = trust
            ? new HostKeyIdentity(Manifest.Host, sshPort, "ssh-ed25519", fingerprint ?? Manifest.HostKeyFingerprint)
            : null;
        return new TestConnection(client, new HostKeyVerifier(client, trusted), key, authentication);
    }

    public async Task<JsonElement> PostAsync(string route, object request)
    {
        // The small Python fixture deliberately accepts bounded Content-Length requests.
        using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync(route, content);
        string json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Fixture operation {route} failed: {json}");
        }
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task<int> ReservePortAsync(int port = 0) =>
        (await PostAsync("reserve", new { port })).GetProperty("port").GetInt32();

    public async Task<int> UnusedPortAsync()
    {
        int port = await ReservePortAsync();
        await ReleasePortAsync(port);
        return port;
    }

    public async Task ReleasePortAsync(int port) => _ = await PostAsync("release", new { port });

    public async Task<bool> IsListeningAsync(int port, string host = "127.0.0.1") =>
        (await PostAsync("listening", new { port, host })).GetProperty("listening").GetBoolean();

    public async Task ExpectListeningAsync(int port, bool expected, string host = "127.0.0.1")
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (await IsListeningAsync(port, host) != expected)
        {
            await Task.Delay(50, deadline.Token);
        }
    }

    public async Task AssertRemotePayloadAsync(int port, byte[] payload, string host = "127.0.0.1")
    {
        JsonElement result = await PostAsync("probe", new { port, host, payload = Convert.ToBase64String(payload) });
        Assert.Equal(payload, Convert.FromBase64String(result.GetProperty("payload").GetString()!));
    }

    public void Dispose() => _http.Dispose();
}

public sealed class TestConnection(SshClient client, HostKeyVerifier verifier, PrivateKeyFile? key,
    AuthenticationMethod authentication) : IDisposable
{
    public SshClient Client { get; } = client;
    public HostKeyVerifier Verifier { get; } = verifier;

    public void Dispose()
    {
        Client.Dispose();
        Verifier.Dispose();
        key?.Dispose();
        (authentication as IDisposable)?.Dispose();
    }
}

public sealed record FixtureManifest
{
    public required string Host { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string PrivateKeyPath { get; init; }
    public required string PlainPrivateKeyPath { get; init; }
    public required string PrivateKeyPassphrase { get; init; }
    public required string HostKeyFingerprint { get; init; }
    public required string AlternateHostKeyFingerprint { get; init; }
    public required string ControlToken { get; init; }
    public required string SshdVersion { get; init; }
    public int AllowPort { get; init; }
    public int DenyPort { get; init; }
    public int RestrictedPort { get; init; }
    public int MutablePort { get; init; }
    public int ControlPort { get; init; }
    public int EchoPort { get; init; }
    public int EchoV6Port { get; init; }
    public int PermitListenPort { get; init; }
}
