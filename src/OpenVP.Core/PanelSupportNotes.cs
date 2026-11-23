namespace OpenVP.Core;

/// <summary>The panel part of docs/unsupported.md. <see cref="UnsupportedLog"/> writes it above the table of unsupported symbols, so the file keeps it when it is rewritten.</summary>
public static class PanelSupportNotes
{
    public const string Markdown = """
        ## Panels: what is and isn't supported

        Everything here is written for OpenVP from public behaviour; none of it is Crestron code. Nothing in this section has been checked against a real touch panel or a real processor's CH5 runtime, and no Crestron project files were available to test the smart-graphics reader on, so treat the Crestron-format parts as best effort.

        **CrComLib shim (`openvp-crcomlib.js`)**

        - Supported: `publishEvent`, `subscribeState`, `unsubscribeState` and `getState` for boolean, number and string signals. Signal names can be a join number (`"12"`), a typed join (`d12`, `a3`, `s7`, `digital_12`), a smart-object join (`so4.d1`), a name from a contract file (`Room1.Light.Press`, `Rooms[2].Volume.Set`) or a name you map yourself (`OpenVP.mapSignals`).
        - Contracts: OpenVP's own `openvp-contract.json` (next to the page, or `window.OPENVP_CONTRACT`) is read. A Contract Editor export (`contract.cse2j`) is flattened by its published shape: joins are numbered from 1 per type and direction in file order. That numbering is an assumption, so check a few names against your program.
        - Not supported: contract names with no contract file (the call is ignored, a warning is logged once and an `unmapped` event fires); join names that depend on Crestron's own project configuration; `registerCrComLibInstance`, `getRemoteTouchpanelSignal`, device-level APIs, WebXPanel, `bridgeReceiveIntegerFromNative` and the other native-app bridge calls.
        - A project that bundles Crestron's own CH5 components keeps those components talking to its own copy of CrComLib, which OpenVP can't intercept; only code that calls `window.CrComLib` goes through OpenVP (on a shared link OpenVP pins `window.CrComLib` so that code does). Projects that need their own bundle's components should use numeric joins through their own `CrComLib.publishEvent` calls or the OpenVP shim's components.

        **CH5 components (defined only if the page uses them and nothing else has defined them)**

        - Supported, basic behaviour: `ch5-button` (`label`, `sendeventonclick` as a press-and-release, `sendeventontouch` as press and release, `receivestateselected`, `receivestatelabel`, `receivestateenable`, `receivestateshow`), `ch5-toggle` (`sendeventonchange`, `receivestatevalue`, `labelon`/`labeloff`), `ch5-slider` (`min`, `max`, `step`, `sendeventonchange`, `receivestatevalue`), `ch5-label` and `ch5-text` (`label`, `receivestatelabel`), `ch5-textinput` (`sendeventonchange`, `receivestatevalue`), `ch5-select` (child `ch5-select-option`/`option`, `sendeventonchange` as the 0-based option index, `receivestatesize`, `receivestatevalue`) and `ch5-image` (`url`, `receivestateurl`; only http, https, relative and image data addresses), and `ch5-list` (a `template` child stamped out per item; `{{idx}}`, `{{idx+1}}`; `size`, `receivestatesize`).
        - Not supported: everything else in the CH5 library (`ch5-subpage-reference-list`, `ch5-modal-dialog`, `ch5-spinner`, `ch5-tabbutton`, `ch5-video`, `ch5-keypad`, `ch5-qrcode`, `ch5-triggerview`, `ch5-form`, themes, `receivestatescriptlabelhtml` as HTML (it is shown as plain text), gestures, scrolling list features, animation and CH5's own CSS classes beyond a few).

        **Emulate mode, generated panels, layouts, kiosk, latency, rack**

        - Emulate runs a stored CH5 project (`.ch5z`) or smart-graphics project in the browser as any slot and IP ID. It is a browser, not a Crestron panel: no native-app calls, no panel hardware, no CIP.
        - Generated panels draw one control per program signal (digital buttons, analog sliders, serial text). A SIMPL# program doesn't list its joins, so only joins it has touched appear; add the rest with **+ join**. They drive signals the way the debugger does (by name), not as a panel on an IP ID.
        - Kiosk mode is a page option (`kiosk=1`) with the Wake Lock API where the browser has it and a fullscreen request on the first tap. The exit PIN (`kioskpin=`) is in the address and stops accidents, not people. The Electron `--kiosk` option has not been run on a real display.
        - Latency: browser panels are measured by the server (a ping every 5 s). CIP panels get the round trip of the connect handshake; a live heartbeat measurement exists (`OpenVP:CipHeartbeatProbe=true`) but is off by default and untested against real panels.

        **Smart graphics (VT Pro-e `.vtz`, Crestron Core 3 UI `.c3p`, XML, JSON)**

        - Read by looking for pages, subpages and objects with names, positions, sizes and join numbers in the archive's XML or JSON. Drawn: buttons (press, feedback, text on a serial join), text and text entry, sliders, gauges, lists and button lists, images (PNG, JPEG, GIF, WebP, BMP), boxes, and pages and subpages as tabs.
        - Not supported, and listed on screen when met: the classic binary `.vtp` project file (it cannot be decoded; export XML or JSON, or use CH5), video windows, web views, cameras, keypads, SVG and other vector art, fonts (the browser's font is used), transitions and animation, visibility and enable joins, subpage reference lists (drawn as a box), dynamic icons, and the exact numbering of smart-object joins (items are digital joins 1 to n of the object). Subpages are tabs, not popups that open from a button.
        """;
}
