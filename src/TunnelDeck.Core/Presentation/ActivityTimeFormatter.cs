using System.Globalization;

namespace TunnelDeck.Core.Presentation;

public sealed record ActivityTimeText(string Relative, string Exact);

public static class ActivityTimeFormatter
{
    public static ActivityTimeText Format(DateTimeOffset occurredAt, DateTimeOffset now, TimeZoneInfo zone, CultureInfo culture)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(occurredAt, zone);
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(now, zone);
        string exact = local.ToString("g", culture) + " (UTC" + local.ToString("zzz", CultureInfo.InvariantCulture) + ")";
        TimeSpan age = now - occurredAt;
        string relative;
        if (age < TimeSpan.Zero) { relative = local.ToString("g", culture); }
        else if (age < TimeSpan.FromMinutes(1)) { relative = UiText.Format("ActivityTimeFormatter_JustNow", culture); }
        else if (age < TimeSpan.FromHours(1)) { relative = UiText.Format("ActivityTimeFormatter_MinAgo", culture, (int)age.TotalMinutes); }
        else if (local.Date == localNow.Date) { relative = UiText.Format("ActivityTimeFormatter_HrAgo", culture, (int)age.TotalHours); }
        else if (local.Date == localNow.Date.AddDays(-1)) { relative = UiText.Format("Time_Yesterday", culture, local.ToString("t", culture)); }
        else { relative = local.ToString("g", culture); }
        return new(relative, exact);
    }
}
