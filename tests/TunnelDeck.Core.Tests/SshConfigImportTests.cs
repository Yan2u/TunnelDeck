using TunnelDeck.Core.Models;
using TunnelDeck.Core.Transfer;

namespace TunnelDeck.Core.Tests;

public sealed class SshConfigImportTests
{
    private static ImportPlan Plan(string text, Func<string, bool>? exists = null) =>
        SshConfigParser.Plan(text, new(), @"C:\Users\fixture", exists ?? (_ => true));

    [Fact]
    public void FirstValueWinsAcrossDefaultsMultipleAliasesAndRepeatedHostBlocks()
    {
        ImportPlan plan = Plan("""
            # Before every Host
            USER global-user
            Host alpha beta
                HostName = "backend.example"
                PORT=2222
                User ignored
            Host alpha
                Port 3333
            Host *
                HostName ignored.example
                Port 4444
            """);
        Assert.Equal(2, plan.Servers.Length);
        Assert.Equal(["alpha", "beta"], plan.Servers.Select(server => server.DisplayName!).ToArray());
        Assert.All(plan.Servers, server =>
        { Assert.Equal("global-user", server.Username); Assert.Equal("backend.example", server.Host); Assert.Equal(2222, server.Port); });
        Assert.Equal(plan.Servers.Select(server => server.Id), plan.Order.OtherServerIds);
        Assert.Contains("password", plan.Items[0].NotesText);
    }

    [Fact]
    public void QuotedKeyPathAndInlineCommentsKeepHashSpacesAndUnicode()
    {
        ImportPlan plan = Plan("""
            Host "example" # a comment
                HostName example.org
                User 'name#suffix' # after value
                IdentityFile "C:/keys/开发 key #1"
            """);
        RemoteServer server = Assert.Single(plan.Servers);
        Assert.Equal("name#suffix", server.Username);
        Assert.Equal(@"C:\keys\开发 key #1", server.IdentityFilePath);
        Assert.Equal(AuthenticationMethod.PrivateKey, server.AuthMethod);
        Assert.Null(server.CredentialReference);
    }

    [Fact]
    public void UnquotedHashInsideAValueIsLiteralAndDefaultHostNameUsesAlias()
    {
        RemoteServer server = Assert.Single(Plan("Host example\n User name#suffix\n").Servers);
        Assert.Equal("name#suffix", server.Username);
        Assert.Equal("example", server.Host);
    }

    [Theory]
    [InlineData("ProxyJump bastion")]
    [InlineData("ProxyCommand touch must-not-run")]
    [InlineData("Include other.conf")]
    [InlineData("IdentitiesOnly yes")]
    [InlineData("ForwardAgent yes")]
    [InlineData("UnknownOption value")]
    [InlineData("LocalForward 9000 target:9000")]
    [InlineData("Compression yes")]
    [InlineData("ForwardX11 yes")]
    [InlineData("ForwardX11Trusted yes")]
    [InlineData("RemoteForward 9000 target:9000")]
    public void UnsupportedOptionsAreIgnoredWithLineNumbersWhenRequiredFieldsAreValid(string option)
    {
        ImportPlan plan = Plan($"Host good\n User dev\nHost bad\n User dev\n {option}\n");
        Assert.Equal(2, plan.Servers.Length);
        ImportItem imported = plan.Items.Single(item => item.Name == "bad");
        Assert.True(imported.Usable);
        Assert.Contains("Line 5", imported.NotesText);
        Assert.Contains(option.Split(' ')[0], imported.NotesText);
        Assert.Contains("ignored", imported.NotesText);
        Assert.True(plan.HasWarnings); Assert.Equal(0, plan.SkippedCount);
    }

    [Fact]
    public void MatchExecAndItsConditionalValuesAreIgnoredWithoutContaminatingStaticFields()
    {
        ImportPlan plan = Plan("Host good\n User dev\nMatch exec \"touch must-not-run\"\n HostName other.example\n");
        Assert.True(plan.CanCommit);
        Assert.Equal("good", Assert.Single(plan.Servers).Host);
        Assert.Contains("Line 3", string.Join("\n", plan.Warnings));
        Assert.Contains("Match", string.Join("\n", plan.Warnings));
    }

    [Fact]
    public void StaticPatternDefaultsApplyToConcreteAliasesWithoutInventingPatternResources()
    {
        ImportPlan plan = Plan("""
            Host *
                User dev
            Host *.corp
                Port 2222
            Host api.corp other.example
            """);
        Assert.Equal(2, plan.Servers.Length);
        Assert.Equal(2222, plan.Servers.Single(server => server.Host == "api.corp").Port);
        Assert.Equal(22, plan.Servers.Single(server => server.Host == "other.example").Port);
        Assert.Contains("Line 3", string.Join("\n", plan.Warnings));
    }

    [Fact]
    public void NegatedPatternDoesNotBlockTheExcludedAlias()
    {
        ImportPlan plan = Plan("Host * !safe\n ProxyJump bastion\nHost safe unsafe\n User dev\n");
        Assert.Equal(2, plan.Servers.Length);
        Assert.DoesNotContain("ProxyJump", plan.Items.Single(item => item.Name == "safe").NotesText);
        Assert.Contains("ProxyJump", plan.Items.Single(item => item.Name == "unsafe").NotesText);
        Assert.Equal(0, plan.SkippedCount);
    }

    [Theory]
    [InlineData("IdentityFile C:/keys/one\n IdentityFile C:/keys/two", "multiple IdentityFile")]
    [InlineData("IdentityFile none", "none")]
    [InlineData("IdentityFile relative/key", "absolute")]
    [InlineData("IdentityFile ~/.ssh/%h", "tokens")]
    public void UnsupportedIdentityOptionsUsePasswordAuthenticationWithAnExplicitNotice(string directive, string expected)
    {
        ImportPlan plan = Plan($"Host fixture\n {directive}\n User dev\n");
        Assert.True(plan.CanCommit);
        Assert.Contains(expected, plan.Items[0].NotesText);
        Assert.Contains("ignored", plan.Items[0].NotesText);
        Assert.Contains("password authentication", plan.Items[0].NotesText);
        Assert.Equal(AuthenticationMethod.Password, Assert.Single(plan.Servers).AuthMethod);
        Assert.Null(plan.Servers[0].IdentityFilePath);
    }

    [Fact]
    public void MissingOptionalKeyIsReportedAndAuthenticationCanBeConfiguredAfterImport()
    {
        ImportPlan plan = Plan("Host fixture\n User dev\n IdentityFile ~/.ssh/id_ed25519\n", _ => false);
        Assert.Equal(AuthenticationMethod.Password, Assert.Single(plan.Servers).AuthMethod);
        Assert.Contains("missing", plan.Items[0].NotesText);
        Assert.Contains("Line 3", plan.Items[0].NotesText);
    }

    [Fact]
    public void MissingUserRequiresManualCompletion()
    {
        Assert.False(Plan("Host fixture\n HostName server.example\n").CanCommit);
        Assert.Contains("explicit User", Plan("Host fixture").Items[0].NotesText);
    }

    [Fact]
    public void IncludeOnlyAndHostStarDoNotInventServers()
    {
        ImportPlan plan = Plan("Include other.conf\nHost *\n User dev\n");
        Assert.False(plan.CanCommit);
        Assert.Empty(plan.Items);
        Assert.Contains("Line 1: Include", string.Join("\n", plan.Warnings));
    }

    [Theory]
    [InlineData("HostName \"unterminated")]
    [InlineData("HostName first second")]
    [InlineData("HostName ${TARGET}")]
    [InlineData("User \"unterminated")]
    [InlineData("User first second")]
    [InlineData("User %r")]
    [InlineData("Port 0")]
    [InlineData("Port 65536")]
    [InlineData("Port invalid")]
    [InlineData("Port 22 2222")]
    public void InvalidRequiredFieldsCannotFallBackToALaterDefault(string directive)
    {
        ImportPlan plan = Plan($"Host invalid\n {directive}\nHost *\n User dev\n Port 22\n");
        Assert.False(plan.CanCommit);
        Assert.Equal(1, plan.SkippedCount);
        Assert.Contains("Line 2", plan.Items[0].NotesText);
    }

    [Fact]
    public void MatchUserCannotSatisfyAMissingStaticUserAndFollowingHostResumesNormalParsing()
    {
        ImportPlan plan = Plan("Host missing\n HostName static.example\nMatch exec must-not-run\n User conditional\nHost ready\n User dev\n");
        Assert.Equal("ready", Assert.Single(plan.Servers).DisplayName);
        Assert.Equal(1, plan.SkippedCount);
        Assert.Contains("explicit User", plan.Items[0].NotesText);
    }

    [Fact]
    public void MalformedOptionalLineAndHostBlockDoNotPreventValidServersFromImporting()
    {
        ImportPlan plan = Plan("Host good\n User dev\n ProxyCommand \"unterminated\nHost \"broken\n User wrong\nHost next\n User next-user\n");
        Assert.Equal(2, plan.Servers.Length);
        Assert.Equal("dev", plan.Servers[0].Username);
        Assert.Equal("next-user", plan.Servers[1].Username);
        Assert.Contains("ignored", plan.Items[0].NotesText);
        Assert.Contains("Line 4", string.Join("\n", plan.Warnings));
    }
}
