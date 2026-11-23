namespace OpenVP.Core.Community;

public enum Support { Supported, Partial, Unsupported }

/// <summary>One panel feature and how far OpenVP gets with it (the panel part of docs/unsupported.md, as data).</summary>
public sealed record PanelFeature(string Area, string Feature, Support Status, string Note);

/// <summary>Where an unsupported-log kind belongs on the compatibility dashboard, and the panel feature list.</summary>
public static class CompatCatalog
{
    public const string Primitives = "Primitives (SIMPL symbols)", SimplSharp = "SIMPL# APIs", Splus = "SIMPL+ modules", Panels = "Panel features", Other = "Other";

    public static readonly IReadOnlyList<string> Categories = [Primitives, SimplSharp, Splus, Panels, Other];

    public static string CategoryOf(string kind)
    {
        var k = kind ?? "";
        if (k.StartsWith("SIMPL#", StringComparison.OrdinalIgnoreCase)) return SimplSharp;
        if (k.StartsWith("SIMPL+", StringComparison.OrdinalIgnoreCase)) return Splus;
        if (k.StartsWith("SIMPL symbol", StringComparison.OrdinalIgnoreCase) || k.StartsWith("SIMPL sub-device", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("SIMPL hardware", StringComparison.OrdinalIgnoreCase) || k.StartsWith("Compiled primitive", StringComparison.OrdinalIgnoreCase)) return Primitives;
        if (k.Contains("panel", StringComparison.OrdinalIgnoreCase) || k.StartsWith("CH5", StringComparison.OrdinalIgnoreCase) || k.Contains("graphics", StringComparison.OrdinalIgnoreCase)) return Panels;
        return Other;
    }

    /// <summary>How an item in the log counts: a SIMPL# member that is missing from a type OpenVP otherwise has is partial support of that type; everything else the log holds is unsupported.</summary>
    public static Support StatusOf(string kind) => kind.Equals("SIMPL# member", StringComparison.OrdinalIgnoreCase) ? Support.Partial : Support.Unsupported;

    public static string Code(Support s) => s switch { Support.Supported => "supported", Support.Partial => "partial", _ => "unsupported" };

    public static readonly IReadOnlyList<PanelFeature> PanelFeatures =
    [
        new("CrComLib shim", "publishEvent, subscribeState, unsubscribeState, getState for boolean, number and string signals", Support.Supported, "Join numbers, typed joins (d12, a3, s7), smart-object joins (so4.d1) and names mapped with OpenVP.mapSignals."),
        new("CrComLib shim", "Contract names from OpenVP's own openvp-contract.json", Support.Supported, "Next to the page or in window.OPENVP_CONTRACT."),
        new("CrComLib shim", "Contract Editor export (contract.cse2j)", Support.Partial, "Joins are numbered from 1 per type and direction in file order. That numbering is an assumption: check a few names against your program."),
        new("CrComLib shim", "Contract names with no contract file", Support.Unsupported, "The call is ignored, a warning is logged once and an unmapped event fires."),
        new("CrComLib shim", "registerCrComLibInstance, getRemoteTouchpanelSignal, device APIs, WebXPanel, native-app bridge calls", Support.Unsupported, "Only window.CrComLib calls go through OpenVP."),
        new("CrComLib shim", "A project that bundles Crestron's own CH5 components and its own CrComLib", Support.Partial, "Its components keep talking to their own copy of CrComLib, which OpenVP can't intercept. Use numeric joins through the OpenVP shim."),
        new("CH5 components", "ch5-button, ch5-toggle, ch5-slider, ch5-label, ch5-text, ch5-textinput, ch5-select, ch5-image, ch5-list", Support.Partial, "Basic behaviour and the common send/receive attributes only."),
        new("CH5 components", "ch5-subpage-reference-list, ch5-modal-dialog, ch5-spinner, ch5-tabbutton, ch5-video, ch5-keypad, ch5-qrcode, ch5-triggerview, ch5-form, themes", Support.Unsupported, "Everything else in the CH5 library; receivestatescriptlabelhtml shows as plain text."),
        new("Panels in OpenVP", "Emulate mode (a stored CH5 or smart-graphics project in the browser)", Support.Supported, "A browser, not a Crestron panel: no native-app calls, no panel hardware, no CIP."),
        new("Panels in OpenVP", "Generated panel with saved layouts", Support.Supported, "A SIMPL# program doesn't list its joins, so only joins it has touched appear; add the rest with + join."),
        new("Panels in OpenVP", "Kiosk mode", Support.Partial, "The page option works with Wake Lock where the browser has it; the Electron --kiosk option has not been run on a real display."),
        new("Panels in OpenVP", "Latency: browser panels (server ping) and CIP connect handshake", Support.Supported, "Shown in the rack view."),
        new("Panels in OpenVP", "Latency: live CIP heartbeat measurement", Support.Partial, "Behind the Labs switch (CIP heartbeat probe); untested against real panels."),
        new("Panels in OpenVP", "CIP panels and XPanels", Support.Partial, "The CIP listener is experimental."),
        new("Smart graphics", "VT Pro-e .vtz, Core 3 UI .c3p, XML and JSON projects: buttons, text, sliders, gauges, lists, images, boxes, pages and subpages as tabs", Support.Partial, "Read by looking for names, positions, sizes and join numbers; best effort, not checked against real project files."),
        new("Smart graphics", "The classic binary .vtp project file", Support.Unsupported, "It can't be decoded: export XML or JSON, or use CH5."),
        new("Smart graphics", "Video windows, web views, cameras, keypads, SVG and other vector art", Support.Unsupported, "Listed on screen when met."),
        new("Smart graphics", "Fonts, transitions and animation, visibility and enable joins, dynamic icons", Support.Unsupported, "The browser's font is used; subpages are tabs, not popups."),
        new("Smart graphics", "Subpage reference lists and the exact numbering of smart-object joins", Support.Partial, "Drawn as a box; items are digital joins 1 to n of the object."),
    ];
}
