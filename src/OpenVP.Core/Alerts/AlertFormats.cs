using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenVP.Core.Alerts;

/// <summary>A plain email, ready for any SMTP transport.</summary>
public sealed record EmailMessage(string From, IReadOnlyList<string> To, string Subject, string Text, string Html);

/// <summary>How a notification looks on each channel. Pure functions, so every payload can be checked in a unit test.</summary>
public static class AlertFormats
{
    public static string Label(AlertSeverity s) => s switch { AlertSeverity.Critical => "Critical", AlertSeverity.Warning => "Warning", _ => "Info" };

    private static string EventWord(Notification n) => n.Event switch
    {
        "resolved" => "Resolved", "test" => "Test", "report" => "Report", "reminder" => "Still happening", _ => Label(n.Severity)
    };

    private static string Icon(Notification n) => n.Event switch
    {
        "resolved" => "✅", "test" => "🧪", "report" => "📊",
        _ => n.Severity switch { AlertSeverity.Critical => "🔴", AlertSeverity.Warning => "🟠", _ => "🔵" }
    };

    /// <summary>The colour of a notification as #rrggbb (Slack's side bar, the HTML email's header).</summary>
    public static string Color(Notification n) => n.Event switch
    {
        "resolved" => "#2f9e6e", "test" or "report" => "#4c8dff",
        _ => n.Severity switch { AlertSeverity.Critical => "#e05252", AlertSeverity.Warning => "#d69e2e", _ => "#4c8dff" }
    };

    private static string Where(Notification n) =>
        n.Slot is { } s ? $"Slot {s}" + (n.Program != null ? $" · {n.Program}" : "") + (n.Room != null ? $" · {n.Room}" : "") : "OpenVP";

    private static IEnumerable<KeyValuePair<string, string>> AllFacts(Notification n)
    {
        if (n.Slot is { } s) yield return new("Slot", s.ToString());
        if (n.Program != null) yield return new("Program", n.Program);
        if (n.Room != null) yield return new("Room", n.Room);
        foreach (var f in n.Facts) yield return f;
        if (n.Count > 1) yield return new("Times", n.Count.ToString());
        yield return new("Time", n.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        yield return new("Computer", n.Host);
    }

    public static string Headline(Notification n) => n.Event == "report" ? n.Title : $"{Icon(n)} {(n.Event == "resolved" ? "Resolved: " : n.Event == "test" ? "Test: " : "")}{n.Title}";

    // ------------------------------------------------------------------ Microsoft Teams

    /// <summary>
    /// An Adaptive Card in the message envelope Teams accepts from a Workflows webhook ("Post to a channel when a webhook request is
    /// received") and from the classic Incoming Webhook connector: a coloured title bar, the message, a table of facts and an
    /// "Open console" button when a console address is set.
    /// </summary>
    public static JsonObject TeamsCard(Notification n)
    {
        var style = n.Event switch { "resolved" => "good", "test" or "report" => "accent", _ => n.Severity switch { AlertSeverity.Critical => "attention", AlertSeverity.Warning => "warning", _ => "accent" } };
        var body = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "Container", ["style"] = style, ["bleed"] = true,
                ["items"] = new JsonArray
                {
                    new JsonObject { ["type"] = "TextBlock", ["text"] = Headline(n), ["weight"] = "Bolder", ["size"] = "Medium", ["wrap"] = true },
                    new JsonObject { ["type"] = "TextBlock", ["text"] = $"{EventWord(n)} · {Where(n)}", ["isSubtle"] = true, ["spacing"] = "None", ["wrap"] = true }
                }
            },
            new JsonObject { ["type"] = "TextBlock", ["text"] = n.Text, ["wrap"] = true },
            new JsonObject { ["type"] = "FactSet", ["facts"] = new JsonArray(AllFacts(n).Select(f => (JsonNode)new JsonObject { ["title"] = f.Key, ["value"] = f.Value }).ToArray()) }
        };
        var card = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["type"] = "AdaptiveCard", ["version"] = "1.4",
            ["msteams"] = new JsonObject { ["width"] = "Full" }, ["body"] = body
        };
        if (!string.IsNullOrEmpty(n.ConsoleUrl))
            card["actions"] = new JsonArray(new JsonObject { ["type"] = "Action.OpenUrl", ["title"] = "Open console", ["url"] = n.ConsoleUrl });
        return new JsonObject
        {
            ["type"] = "message",
            ["attachments"] = new JsonArray(new JsonObject { ["contentType"] = "application/vnd.microsoft.card.adaptive", ["contentUrl"] = null, ["content"] = card })
        };
    }

    // ------------------------------------------------------------------ Slack

    /// <summary>Slack's mrkdwn treats &amp;, &lt; and &gt; specially.</summary>
    public static string SlackEscape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// Block Kit blocks inside one attachment, so the message gets a coloured side bar: a header, the message, the facts as fields and a
    /// context line. <c>text</c> is the fallback shown in notifications.
    /// </summary>
    public static JsonObject SlackBlocks(Notification n)
    {
        var blocks = new JsonArray
        {
            new JsonObject { ["type"] = "header", ["text"] = new JsonObject { ["type"] = "plain_text", ["text"] = Trunc(Headline(n), 150), ["emoji"] = true } },
            new JsonObject { ["type"] = "section", ["text"] = new JsonObject { ["type"] = "mrkdwn", ["text"] = Trunc(SlackEscape(n.Text), 3000) } }
        };
        var facts = AllFacts(n).Where(f => f.Key is not ("Time" or "Computer")).Take(10).ToList();
        if (facts.Count > 0)
            blocks.Add(new JsonObject
            {
                ["type"] = "section",
                ["fields"] = new JsonArray(facts.Select(f => (JsonNode)new JsonObject { ["type"] = "mrkdwn", ["text"] = Trunc($"*{SlackEscape(f.Key)}*\n{SlackEscape(f.Value)}", 2000) }).ToArray())
            });
        blocks.Add(new JsonObject
        {
            ["type"] = "context",
            ["elements"] = new JsonArray(new JsonObject { ["type"] = "mrkdwn", ["text"] = $"{EventWord(n)} · OpenVP on {SlackEscape(n.Host)} · {n.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss}" })
        });
        if (!string.IsNullOrEmpty(n.ConsoleUrl))
            blocks.Add(new JsonObject
            {
                ["type"] = "actions",
                ["elements"] = new JsonArray(new JsonObject { ["type"] = "button", ["text"] = new JsonObject { ["type"] = "plain_text", ["text"] = "Open console" }, ["url"] = n.ConsoleUrl })
            });
        return new JsonObject
        {
            ["text"] = $"{Headline(n)}: {Trunc(n.Text, 200)}",
            ["attachments"] = new JsonArray(new JsonObject { ["color"] = Color(n), ["blocks"] = blocks })
        };
    }

    // ------------------------------------------------------------------ ntfy

    /// <summary>ntfy's JSON publish body (POST it to the server's root): the topic, title, message, priority 1-5, emoji tags and an optional click link.</summary>
    public static JsonObject Ntfy(Notification n, string topic)
    {
        var priority = n.Event switch { "resolved" or "test" or "report" => 3, _ => n.Severity switch { AlertSeverity.Critical => 5, AlertSeverity.Warning => 4, _ => 3 } };
        var tag = n.Event switch { "resolved" => "white_check_mark", "test" => "test_tube", "report" => "bar_chart", _ => n.Severity switch { AlertSeverity.Critical => "rotating_light", AlertSeverity.Warning => "warning", _ => "information_source" } };
        var o = new JsonObject
        {
            ["topic"] = topic,
            ["title"] = (n.Event == "resolved" ? "Resolved: " : n.Event == "test" ? "Test: " : "") + n.Title,
            ["message"] = n.Text + (n.Slot is { } s ? $"\n{Where(n)}" : ""),
            ["priority"] = priority,
            ["tags"] = new JsonArray(tag, "openvp")
        };
        if (!string.IsNullOrEmpty(n.ConsoleUrl)) o["click"] = n.ConsoleUrl;
        return o;
    }

    // ------------------------------------------------------------------ generic webhook

    /// <summary>The JSON a generic webhook receives. <c>event</c> is <c>alert.raised</c>, <c>alert.reminder</c>, <c>alert.resolved</c>, <c>alert.test</c> or <c>report</c>.</summary>
    public static JsonObject Webhook(Notification n)
    {
        var o = new JsonObject
        {
            ["event"] = EventName(n), ["time"] = n.Time.ToString("O"), ["id"] = n.Id, ["key"] = n.Key, ["kind"] = n.Kind,
            ["severity"] = Label(n.Severity).ToLowerInvariant(), ["title"] = n.Title, ["message"] = n.Text, ["count"] = n.Count, ["host"] = n.Host
        };
        if (n.Slot != null) { o["slot"] = n.Slot; o["room"] = n.Room; o["program"] = n.Program; }
        var facts = new JsonObject();
        foreach (var f in n.Facts) facts[f.Key] = f.Value;
        if (facts.Count > 0) o["facts"] = facts;
        if (n.Event == "report" && n.Html != null) o["html"] = n.Html;
        return o;
    }

    public static string EventName(Notification n) => n.Event == "report" ? "report" : "alert." + n.Event;

    // ------------------------------------------------------------------ email

    public static string EmailSubject(Notification n) => n.Event switch
    {
        "resolved" => $"[OpenVP] RESOLVED: {n.Title}",
        "test" => $"[OpenVP] Test: {n.Title}",
        "report" => $"[OpenVP] {n.Title}",
        _ => $"[OpenVP] {Label(n.Severity).ToUpperInvariant()}: {n.Title}"
    };

    public static string EmailText(Notification n)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Headline(n)).AppendLine();
        sb.AppendLine(n.Text).AppendLine();
        foreach (var f in AllFacts(n)) sb.AppendLine($"{f.Key}: {f.Value}");
        if (!string.IsNullOrEmpty(n.ConsoleUrl)) sb.AppendLine().AppendLine("Open the console: " + n.ConsoleUrl);
        return sb.ToString().TrimEnd();
    }

    public static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>The HTML body: a coloured header, the message, a table of facts. Everything is inline-styled, since mail clients drop style sheets. A report supplies its own HTML.</summary>
    public static string EmailHtml(Notification n)
    {
        if (n.Event == "report" && n.Html != null) return n.Html;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><body style=\"margin:0;padding:16px;background:#f3f5f8;font-family:Segoe UI,Arial,sans-serif;color:#1b2330\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" style=\"max-width:600px;margin:0 auto;background:#fff;border:1px solid #d2d9e2;border-radius:8px;border-collapse:separate\">");
        sb.Append($"<tr><td style=\"background:{Color(n)};color:#fff;padding:12px 16px;border-radius:8px 8px 0 0\"><div style=\"font-size:12px;opacity:.9\">{Esc(EventWord(n))} · {Esc(Where(n))}</div><div style=\"font-size:18px;font-weight:600\">{Esc(n.Title)}</div></td></tr>");
        sb.Append($"<tr><td style=\"padding:14px 16px;font-size:14px;line-height:1.5\">{Esc(n.Text).Replace("\n", "<br>")}</td></tr>");
        sb.Append("<tr><td style=\"padding:0 16px 14px\"><table role=\"presentation\" width=\"100%\" style=\"border-collapse:collapse;font-size:13px\">");
        foreach (var f in AllFacts(n))
            sb.Append($"<tr><td style=\"padding:4px 8px 4px 0;color:#586374;white-space:nowrap;vertical-align:top\">{Esc(f.Key)}</td><td style=\"padding:4px 0\">{Esc(f.Value)}</td></tr>");
        sb.Append("</table></td></tr>");
        if (!string.IsNullOrEmpty(n.ConsoleUrl))
            sb.Append($"<tr><td style=\"padding:0 16px 16px\"><a href=\"{Esc(n.ConsoleUrl)}\" style=\"display:inline-block;background:#1d5fd6;color:#fff;text-decoration:none;padding:8px 14px;border-radius:6px;font-size:13px\">Open console</a></td></tr>");
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    public static EmailMessage Email(Notification n, string from, IEnumerable<string> to) =>
        new(from, to.ToList(), EmailSubject(n), EmailText(n), EmailHtml(n));

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    public static byte[] Bytes(JsonNode node) => JsonSerializer.SerializeToUtf8Bytes(node);
}
