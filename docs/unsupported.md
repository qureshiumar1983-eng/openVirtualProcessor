# Unsupported symbols and APIs

OpenVP adds a row here whenever a program uses a symbol, type or API it doesn't support yet. Commit this file after running your programs so everyone can see what to build next; **Programs** counts the program loads that hit it, so the top of each kind is the most wanted. Only symbol names are recorded, never program names.

When you implement one, delete its row (the server also drops rows for symbols the catalog supports now, whether matched by library name, SmC code or compiled type, and rows for SIMPL# types and members the API shim implements now; a `SIMPL# member` row with `(N arguments)` is an overload with that many arguments that is missing). If a program still needs it, it comes back on the next run.

Merging: if two people's edits clash, keep both rows; for the same row, add the Programs counts and keep the earliest First seen and latest Last seen.

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
- Latency: browser panels are measured by the server (a ping every 5 s). CIP panels get the round trip of the connect handshake; a live heartbeat measurement exists (the CIP heartbeat probe switch on the Labs page, or `OpenVP:CipHeartbeatProbe=true`) but is off by default and untested against real panels.

**Smart graphics (VT Pro-e `.vtz`, Crestron Core 3 UI `.c3p`, XML, JSON)**

- Read by looking for pages, subpages and objects with names, positions, sizes and join numbers in the archive's XML or JSON. Drawn: buttons (press, feedback, text on a serial join), text and text entry, sliders, gauges, lists and button lists, images (PNG, JPEG, GIF, WebP, BMP), boxes, and pages and subpages as tabs.
- Not supported, and listed on screen when met: the classic binary `.vtp` project file (it cannot be decoded; export XML or JSON, or use CH5), video windows, web views, cameras, keypads, SVG and other vector art, fonts (the browser's font is used), transitions and animation, visibility and enable joins, subpage reference lists (drawn as a box), dynamic icons, and the exact numbering of smart-object joins (items are digital joins 1 to n of the object). Subpages are tabs, not popups that open from a button.

## Unsupported symbols and types seen in programs

| Kind | Symbol / API | Details | Programs | First seen | Last seen |
|---|---|---|---:|---|---|
| Compiled primitive | type 107 | unidentified; inside DENON DN-700R Full Control v1.0.umc (run from the .lpz) | 3 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 261 | unidentified; inside SACN SenderDirectSockets.cmc (run from the .lpz) | 2 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 113 | unidentified | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | GLS-PART-CN (SmC 5206) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4N Relay (SmC 7055) | processor hardware (Cresnet, COM, IR, relays...) | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4N System Control (SmC 7058) | processor hardware (Cresnet, COM, IR, relays...) | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4 Relay (SmC 6583) | processor hardware (Cresnet, COM, IR, relays...) | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-CP3-RY8 (SmC 3391) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-DMTX201C-VGA (SmC 2576) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4 System Control (SmC 6586) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4N I/O Ports (SmC 7054) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-TX-201-C (SmC 2574) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Relays (SmC 4382) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 System Control (SmC 4415) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-300-C AV Control (SmC 5102) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-300-C Device Control (SmC 4816) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C AV Control (SmC 5858) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-CP3-SYSTEMCONTROL (SmC 3395) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-DMTX200-C-2G-VGA (SmC 3385) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-DMTX201C-HDMI (SmC 2575) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-PRO3-SYSTEMCONTROL (SmC 3673) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-REMOTE-BACNET-DEVICE v1.0 (SmC 3560) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-REMOTE-BACNET-DEVICES v1.0 (SmC 3559) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2N-CBD-P (SmC 2735) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C3RY-8 (SmC 3473) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CM2-KPCN Button/LED Functions (SmC 19492) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4 I/O Ports (SmC 6582) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4N System Information (SmC 7059) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-4K-100-C-1G HDMI (SmC 5223) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-4KZ-SCALER-C (SmC 6431) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-SCALER-C Scaler (SmC 3338) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-TX-200-C-2G (SmC 3383) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Digital Inputs (SmC 4381) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Input 6 DM (SmC 4613) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Input 7 DM (SmC 4614) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C DM 3 (SmC 5896) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C DM 4 (SmC 5899) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 1 HDMI (SmC 5867) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 2 HDMI (SmC 5868) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | GLS-ODT-C-CN (SmC 3478) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Hosted Analog Input Object (SmC 3563) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Hosted Analog Value Object (SmC 20238) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Hosted Analog Value Object (SmC 3576) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 Front Panel (SmC 6606) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 Front Panel Buttons (SmC 6607) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 IO (SmC 6603) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Remote Analog Value Object (SmC 20241) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Remote Analog Value Object (SmC 3579) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Remote Binary Output Object (SmC 20234) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Remote Binary Output Object (SmC 3568) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | RMC4 System Control (SmC 6543) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | RMC4 System Monitor (SmC 6542) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | ST-IO (SmC 136) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TST-902 (EX) (SmC 5187) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x HDMI In (SmC 7065) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x HDMI Out (SmC 7067) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-1060 Buttons (SmC 5933) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX200-C-2G-HDMI (SmC 3384) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX200-C-2G-VGA (SmC 3385) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX201C-HDMI (SmC 2575) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX201C-VGA (SmC 2576) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX401C-HDMI (SmC 3871) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM NAX TX (SmC 7050) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Network (SmC 7074) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x USB (SmC 7075) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC USB (SmC 15517) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30 (v2) HDMI Out (SmC 15424) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E30 (v2) HDMI In (SmC 15440) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760 DM In (SmC 15306) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-200-C (SmC 4061) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-O-HD (25-32) (SmC 5446) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | AM-200 AirMedia (SmC 6464) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2N-CBD-P (SmC 2735) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2N-CBD-P Brightness and Bargraph Functions (SmC 2737) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM NAX Routing (SmC 7048) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM NAX RX (SmC 7049) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM XIO Routing (SmC 6094) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial RX Zone (SmC 17547) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) HDMI In 1 (SmC 15403) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) HDMI Out (SmC 15405) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) Network (SmC 15410) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) USB (SmC 15411) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) HDMI In 1 (SmC 15416) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) HDMI Out (SmC 15418) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) Network (SmC 15421) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) USB (SmC 15422) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Stream Transmit (SmC 7063) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC HDMI In (SmC 15512) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC HDMI Out (SmC 15513) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC Network (SmC 15516) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D200 Network (SmC 16771) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30 (v2) Network (SmC 15428) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E10-E20 HDMI In (SmC 16384) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E10-E20 Network (SmC 16387) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E20-2G HDMI In (SmC 16988) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E20-2G Network (SmC 16991) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E30C (v2) HDMI In (SmC 15447) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760 Network (SmC 15307) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760C DM In (SmC 15311) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-4KZ-SCALER-C (SmC 6431) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-O-HD (33-40) (SmC 5446) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4K-HDO Output 1 (SmC 5324) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4KZ-C Advanced. (SmC 6345) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4KZ-CO-HD Output 1. (SmC 6295) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4KZ-CO-HD Output 2. (SmC 6296) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-CTL-101 HDMI In (SmC 16965) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Audio (SmC 18915) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Camera Names (SmC 18371) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Config Names (SmC 18369) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Layouts (SmC 18367) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Scenario Names (SmC 18373) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Scenario Names (SmC 20133) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TPMC-3SMD Buttons (SmC 3488) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-560 Buttons (SmC 5929) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | press and hold v1.2.cmc (SmC 155) | pins in=1 out=2 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Display Control v2.3.umc (SmC 155) | pins in=8 out=9 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Display Helper.usp (SmC 103) | pins in=32 out=32 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | press and hold2.cmc (SmC 155) | pins in=1 out=3 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AnalogHasValue.umc (SmC 155) | pins in=32 out=32 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Auto Sync Detect v2.0.umc (SmC 155) | pins in=12 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Command Processor IP v1.6.umc (SmC 155) | pins in=110 out=110 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Command Processor IP v3.3.umc (SmC 155) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Level Control v3.3.umc (SmC 155) | pins in=13 out=13 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira State Control v3.3.umc (SmC 155) | pins in=7 out=7 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Analog Remap.usp (SmC 103) | pins in=325 out=212 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Analog.usp (SmC 103) | pins in=1 out=11 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Digitals.usp (SmC 103) | pins in=1 out=47 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Reader.usp (SmC 103) | pins in=2 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Serial.usp (SmC 103) | pins in=1 out=95 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DM64x64 Naming (SmC 4054) | pins in=1064 out=0 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Epson PowerLite Pro G5150NL + G5200WNL + G5350NL Full Control.cmc (SmC 155) | pins in=42 out=42 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Full 101+ Keyboard v1.0.cmc (SmC 155) | pins in=68 out=3 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multiple Router.umc (SmC 155) | pins in=408 out=305 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVX Video Router.usp (SmC 103) | pins in=27 out=28 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Generic Boolean Named Control.usp (SmC 103) | pins in=3 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SerialHasValue.umc (SmC 155) | pins in=32 out=32 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sony EVI-D100 Camera Control (25 Presets).cmc (SmC 155) | pins in=61 out=61 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SSH Interface v1.8.cmc (SmC 155) | pins in=8 out=8 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TST-902 RF/WiFi Reserved Joins (SmC 4722) | pins in=69 out=103 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Addressbook File Operations v2.0(DG).usp (SmC 103) | pins in=12 out=109 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Alarm Clock v2.0.cmc (SmC 155) | pins in=25 out=28 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ANALOG BUFFER.usp (SmC 103) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Compare Pulse.usp (SmC 103) | pins in=2 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Delay.usp (SmC 103) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Value Counter.usp (SmC 103) | pins in=4 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AnalogValueMatch.usp (SmC 103) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Auto Adjust Font Size v1.0.usp (SmC 103) | pins in=4 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AutoSwitch (Rev 1-00-0).usp (SmC 103) | pins in=5 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AVoIP Router.usp (SmC 103) | pins in=91 out=61 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Level Control (Rev 0-00-0).usp (SmC 103) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Preset Control v3.3.umc (SmC 155) | pins in=10 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Router Control v3.3.umc (SmC 155) | pins in=9 out=9 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco Room Devices 4.0.umc (SmC 155) | pins in=769 out=769 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco SX80 v2.3.9.2 SG.umc (SmC 155) | pins in=331 out=334 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco Touch 10 Room Control Processor v1.0.usp (SmC 103) | pins in=3 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco Touch 10 Room Control Slider v1.0.usp (SmC 103) | pins in=3 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco Touch 10 Room Control Toggle Button v1.0.usp (SmC 103) | pins in=4 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | CiscoTeamsCallState.usp (SmC 103) | pins in=3 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge (Multiple Units) Macros v1.7.umc (SmC 155) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge (Multiple Units) Single Channel Output Control with Setup v1.7.umc (SmC 155) | pins in=20 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge Dialing v1.8.umc (SmC 155) | pins in=54 out=54 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge Feedback Processor v1.7.umc (SmC 155) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge VH20 Dialing v1.7.umc (SmC 155) | pins in=40 out=40 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cn-spw2.cmc (SmC 155) | pins in=15 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Analog Reroute.usp (SmC 103) | pins in=82 out=49 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Countdown Timer with Analog Timer Output.umc (SmC 155) | pins in=2 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Device-Pwr-IsItOn.umc (SmC 155) | pins in=2 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DigitalDelay.usp (SmC 103) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ext_channelpresets_x50.cmc (SmC 155) | pins in=58 out=72 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HP_BiampRouterControl.usp (SmC 103) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Language lookup.usp (SmC 103) | pins in=5 out=35 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | LBISS GE Text Dial v1.00.usp (SmC 103) | pins in=5 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Loading Bar v1.0.umc (SmC 155) | pins in=1 out=6 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Memory Store.usp (SmC 103) | pins in=66 out=66 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Mitsubishi Projector Control (Rev 1-00-1).umc (SmC 155) | pins in=11 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multi-Analog Comparison.usp (SmC 103) | pins in=3 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multiple Press Enable.umc (SmC 155) | pins in=34 out=34 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NEC NP-PA600X v1.0.umc (SmC 155) | pins in=91 out=91 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Notification Service Issuer.usp (SmC 103) | pins in=2 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Notification Service Notifier.usp (SmC 103) | pins in=3 out=9 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVRAM Toggle.usp (SmC 103) | pins in=3 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVX Multicast Address Assigner.usp (SmC 103) | pins in=315 out=16 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Passcode Database v1.0.usp (SmC 103) | pins in=5 out=16 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Passcode Entry.umc (SmC 155) | pins in=22 out=22 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | password with backdoor password.umc (SmC 155) | pins in=15 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Password_v1_1.cmc (SmC 155) | pins in=47 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | pj Helper.umc (SmC 155) | pins in=15 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Polycom Eagle Eye Camera w-Iris v3-lk.umc (SmC 155) | pins in=96 out=96 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Polycom RealPresence Group Series v2.4.3-MGJ.umc (SmC 155) | pins in=365 out=365 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Presentation_Timer_v1_0.cmc (SmC 155) | pins in=13 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | press and hold v2.umc (SmC 155) | pins in=1 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Button] V5.0.umc (SmC 155) | pins in=7 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Button] V5.2.umc (SmC 155) | pins in=7 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Button] V5.3.umc (SmC 155) | pins in=7 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Core] V5.2.0 RM.umc (SmC 155) | pins in=9 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Core] V5.3.umc (SmC 155) | pins in=9 out=9 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Level Control] V5.0.umc (SmC 155) | pins in=12 out=8 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Level Control] V5.2.umc (SmC 155) | pins in=12 out=8 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [String - Value] V5.2.umc (SmC 155) | pins in=7 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Core.umc (SmC 155) | pins in=14 out=14 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Core.usp (SmC 103) | pins in=7 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Fader.usp (SmC 103) | pins in=7 out=6 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Level Control v3.0.usp (SmC 103) | pins in=12 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Processor Control v3.1.usp (SmC 103) | pins in=13 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Snapshot Controller.usp (SmC 103) | pins in=7 out=6 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys State Control v3.0.usp (SmC 103) | pins in=6 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Room Combiner.umc (SmC 155) | pins in=66 out=99 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SACN SenderDirectSockets.cmc (SmC 155) | pins in=512 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SCHEDULED TRIGGER.usp (SmC 103) | pins in=5 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SERIALIZE_TIME_2.usp (SmC 103) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SERIAL_TIME_2.umc (SmC 155) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sharp PN Monitor Control (Rev 2-00-1).umc (SmC 155) | pins in=17 out=17 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | shure mic battery v2.0.usp (SmC 103) | pins in=22 out=22 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure MXWAPT.umc (SmC 155) | pins in=152 out=240 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure P300 v1.1.usp (SmC 103) | pins in=362 out=389 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure-ULX-D_module V3.umc (SmC 155) | pins in=3 out=76 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simple Alarm Clock.usp (SmC 103) | pins in=11 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simple Math.usp (SmC 103) | pins in=23 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | simple_timer_v10.cmc (SmC 155) | pins in=2 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SmartViewGlassSplus.usp (SmC 103) | pins in=5 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Splash Mode v2.0.usp (SmC 103) | pins in=54 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SubPage Navigation v1.0.usp (SmC 103) | pins in=39 out=70 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | System Presets.umc (SmC 155) | pins in=872 out=872 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Tesira VI (Rev 1.00.9).umc (SmC 155) | pins in=32 out=32 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Timed Event Scheduler.usp (SmC 103) | pins in=25 out=25 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UC-ENGINE-FLEX-TEAMS System Reserved Joins (SmC 16512) | pins in=461 out=434 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | udsrly_mom.cmc (SmC 155) | pins in=4 out=6 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UInt64 Bit Mask_v2.usp (SmC 103) | pins in=12 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | USB-EXT-DM Control v1.0.cmc (SmC 155) | pins in=4 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Video Source Send (Rev 0-00-2).umc (SmC 155) | pins in=1 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | wait_for_initialization.usp (SmC 103) | pins in=0 out=1 | 1 | 2026-11-19 | 2026-11-19 |
