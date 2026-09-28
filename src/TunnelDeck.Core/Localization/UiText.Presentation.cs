using System.Globalization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;

namespace TunnelDeck.Core.Localization;

public static partial class UiText
{
    public static string Count(long count, string noun, CultureInfo? culture = null)
    {
        string key = noun switch
        {
            "server" => "Server", "profile" => "Profile", "rule" => "Rule", "forward" => "Forward",
            "new server" => "NewServer", "new profile" => "NewProfile", "forwarding rule" => "ForwardingRule",
            "existing forward" => "ExistingForward", "pending forward" => "PendingForward", "connection test" => "ConnectionTest",
            "use" => "Use", "entry" => "Entry", _ => throw new ArgumentOutOfRangeException(nameof(noun)),
        };
        return Format("Count_" + key + (count == 1 ? "One" : "Other"), culture ?? Culture, count);
    }

    public static string State(TunnelState state) => Get("State_" + state);
    public static string Outcome(ActivityOutcome outcome) => Get("Outcome_" + outcome);
}
