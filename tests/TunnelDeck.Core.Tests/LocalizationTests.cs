using System.Globalization;
using TunnelDeck.Core.Localization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Transfer;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tests;

[CollectionDefinition("UI language", DisableParallelization = true)]
public sealed class LanguageCollection { }

[Collection("UI language")]
public sealed class LocalizationTests : IDisposable
{
    private readonly string _original = UiText.Language;

    public LocalizationTests() => UiText.SetLanguage(UiText.English);
    public void Dispose() => UiText.SetLanguage(_original);

    [Fact]
    public void ChangingLanguageRefreshesExistingRowsWithoutChangingRuntimeOrUserText()
    {
        var rule = DomainData.Rule();
        var source = new ForwardSourceSnapshot { ServerName = "Running", ProfileName = "No usage yet", ProfileId = DomainData.Id(1001) };
        var tunnel = new ActiveTunnel { Requested = rule, State = TunnelState.Running, Source = source, StartedAt = DomainData.Now };
        var entry = new ActivityEntry { Id = DomainData.Id(900), Requested = rule, Source = source, Outcome = ActivityOutcome.Failed, OccurredAt = DomainData.Now };
        var row = new TunnelItem(tunnel, new(), DomainData.Now);
        var activity = new RecentActivityItem(entry, new());
        activity.UpdateTime(DomainData.Now.AddMinutes(2), TimeZoneInfo.Utc, UiText.Culture);
        int notifications = 0;
        row.PropertyChanged += (_, _) => notifications++;

        UiText.SetLanguage(UiText.SimplifiedChinese);

        Assert.Equal("运行中", row.Status);
        Assert.Equal("Running", row.StatusKey);
        Assert.Equal("失败", activity.Outcome);
        Assert.Equal("Failed", activity.OutcomeKey);
        Assert.Contains("分钟前", activity.Time);
        Assert.True(notifications > 0);
        Assert.Equal("Running", row.ServerName);
        Assert.Equal("No usage yet", row.Source);
        Assert.Equal("No usage yet", activity.Name);
        Assert.Contains("3000", row.Route);
        Assert.Contains("5432", row.Route);
        Assert.Same(tunnel, row.Value);
        Assert.Same(entry, activity.Entry);

        UiText.SetLanguage(UiText.English);
        Assert.Equal("Running", row.Status);
        Assert.Equal("Failed", activity.Outcome);
        Assert.Equal("2 min ago", activity.Time);
    }

    [Fact]
    public void ImportDiagnosticsTranslateWhileKeywordsPathsAndNamesStayIntact()
    {
        const string keyPath = @"C:\Profiles\Running\No usage yet\id_rsa";
        AppState configuration = new()
        {
            Settings = new() { DefaultAuthenticationMethod = AuthenticationMethod.PrivateKey, DefaultIdentityFilePath = keyPath },
        };
        ImportPlan plan = SshConfigParser.Plan("Host Running\n HostName example.com\n User dev\n Compression yes", configuration, @"C:\fixture", _ => true);
        ImportItem item = Assert.Single(plan.Items);
        string[] originalNotes = item.Notes.ToArray();
        Assert.True(item.Usable);

        UiText.SetLanguage(UiText.SimplifiedChinese);

        Assert.Contains("第 4 行：不支持 Compression，已忽略。", item.NotesText);
        Assert.Contains("未指定 IdentityFile", item.NotesText);
        Assert.Contains(keyPath, item.NotesText);
        Assert.Equal("Running", item.Name);
        Assert.Contains("dev@example.com:22", item.DisplayDescription);
        Assert.Equal(originalNotes, item.Notes);
        Assert.Equal(keyPath, plan.Servers[0].IdentityFilePath);
        Assert.Contains("Line 4", item.Notes[0]);

        string translated = UiText.Translate(item.AuthenticationNote);
        UiText.SetLanguage(UiText.English);
        Assert.Equal(item.AuthenticationNote, UiText.Translate(translated));
    }

    [Fact]
    public void ValidationPathsAndHistoricalDiagnosticsUseTheCurrentLanguage()
    {
        string error = ConfigurationValidator.ValidateRule(DomainData.Rule(localPort: 0)).Issues
            .Single(issue => issue.Path == "LocalPort").Message;
        UiText.SetLanguage(UiText.SimplifiedChinese);
        Assert.Equal("本地端口：请输入 1 到 65535 之间的整数端口号。", UiText.Translate("LocalPort: " + error));
        Assert.Equal("第 6 行：不支持 Compression，已忽略。", UiText.Translate("Line 6: Compression is not supported and was ignored."));
        const string unknown = "An unrecognized external diagnostic remains unchanged.";
        Assert.Equal(unknown, UiText.Translate(unknown));
        Assert.Contains("whole-number", error);
    }

    [Fact]
    public void ExplicitLanguageDoesNotFollowThreadCultureOrChangeNewSettingsDefaults()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Assert.Equal("Settings", UiText.Get("MainWindow_Settings"));
            UiText.SetLanguage(UiText.SimplifiedChinese);
            Assert.Equal("设置", UiText.Get("MainWindow_Settings"));
            Assert.Equal(UiText.English, new AppSettings().Language);
            Assert.Throws<ArgumentOutOfRangeException>(() => UiText.SetLanguage("fr-FR"));
            Assert.Equal(UiText.SimplifiedChinese, UiText.Language);
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }

    [Fact]
    public void UsagePlaceholdersCountsAndRelativeTimesRespectExplicitCulture()
    {
        var chinese = CultureInfo.GetCultureInfo(UiText.SimplifiedChinese);
        Assert.Equal("暂无使用记录", UsageStatisticsProjection.Create(new(), chinese).Server.Name);
        Assert.Equal("No usage yet", UsageStatisticsProjection.Create(new(), CultureInfo.InvariantCulture).Profile.Name);
        Assert.Equal("1 server", UiText.Count(1, "server"));
        Assert.Equal("2 servers", UiText.Count(2, "server"));
        Assert.Equal("2 个服务器", UiText.Count(2, "server", chinese));
        Assert.Equal("刚刚", ActivityTimeFormatter.Format(DomainData.Now, DomainData.Now, TimeZoneInfo.Utc, chinese).Relative);
    }

    [Theory]
    [InlineData("en-US", true)]
    [InlineData("zh-CN", true)]
    [InlineData("zh-TW", false)]
    [InlineData("system", false)]
    [InlineData(null, false)]
    public void SettingsAcceptOnlyExplicitSupportedLanguages(string? language, bool valid) =>
        Assert.Equal(valid, ConfigurationValidator.ValidateSettings(new() { Language = language! }).IsValid);
}
