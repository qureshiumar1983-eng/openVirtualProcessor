# OpenVP — open-source virtual control processor

OpenVP runs control programs on any Linux, Windows or macOS machine, like Crestron's VC-4 does, but it's open source and needs no license server. It runs both kinds of program:

- **SIMPL# Pro programs**: the compiled `.cpz` archive or the program `.dll`
- **SIMPL Windows programs**: the compiled `.lpz` (no source needed), or the `.smw` source file

You get up to 10 program slots, browser panels and (experimental) real touch panels over CIP, EISC links between slots, a per-slot console, and a web debugger where each slot's signals stay separate.

> **Status: early (0.1).** The sample programs in this repo run end to end and are covered by tests. Real-world programs will hit APIs and symbols that aren't implemented yet. The tool tells you exactly which ones (see [Compatibility](#compatibility)), and filling them in is the main work ahead.

> **Not affiliated with Crestron.** Crestron, SIMPL, SIMPL#, VC-4 and related names are trademarks of Crestron Electronics, Inc. OpenVP is an independent, clean-room implementation and contains no Crestron code, firmware or SDK files. Read your Crestron license agreements before using this with their software. This isn't legal advice.

## Quick start

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/qureshiumar1983-eng/openVirtualProcessor.git
cd openVirtualProcessor
dotnet run --project src/OpenVP.Server
```

Open **http://localhost:8080**. New here? Click **Try the demo** (on the Welcome screen, in the console or in the search box, `Ctrl+K`): it loads the two sample programs into slots 1 and 2, starts them and opens a test panel; press button 10 on it and watch the other slot answer. From another machine you'll be asked for the access key, which the server prints when it starts (see [Security](#security)). Pick a slot, choose a `.cpz`, `.dll`, `.lpz` or `.smw` file, and OpenVP tells you whether it will run (green *Will run*, amber *Will partly run*, red *Won't run*, with the reasons). Then click **Load & start**; the verdict is advice and never blocks the load. Click **open test panel** next to a device to get a browser panel at that IP ID. Every page has a **Help** link that explains what each button on it does. The first time, a short welcome screen explains what OpenVP is and what to do first; finish or skip it and it stays away for everyone. Reopen it from **Help**.

### Desktop app (Windows)

**`OpenVP.exe`** is OpenVP as a desktop app (Electron), in one file: nothing to install, no .NET needed, no console window. Run it and the web console opens in its own window. Test panels and the debugger open in their own windows too. Closing the main window stops OpenVP. Your data and the log (`openvp.log`) are kept in `%APPDATA%\OpenVP`; shared-panel links are in `%APPDATA%\OpenVP\data\shares.json` and a downloaded `cloudflared` in `%APPDATA%\OpenVP\data\tools`. The internet tunnel is stopped when the app quits. The first time it runs, Windows may ask whether to let it through the firewall; allow it if you want panels or other machines to connect.

**Loading from your computer.** In the desktop app, **Load & start** loads the program from where it is on disk rather than uploading a copy, so the files that belong with it come along without picking them: the `.sig` with the same name (or the only `.sig` in that folder), the `.lpz` behind a `.smw`, and sidecars such as `.openvp.json`. Tick **Reload when the file changes** and the slot reloads by itself whenever the program or its `.sig` changes, for example every time you compile in SIMPL Windows. A running program starts again and a stopped one stays stopped. In a browser on the OpenVP computer itself, **Load from path…** does the same from a typed path. From other machines you still upload the files.

Download the latest from the [Releases](https://github.com/qureshiumar1983-eng/openVirtualProcessor/releases) page: every push to `main` builds a new `OpenVP.exe`, checks that it starts, and publishes it there.

**Updates.** OpenVP.exe checks that Releases page when it starts and every 6 hours (it uses GitHub's free public API, no account needed). When there's a newer version it pops up and offers **Update now**, **Later** or **Skip this version**. Update now downloads the new `OpenVP.exe`, checks it against the release's SHA-256, replaces the old file and restarts OpenVP. That stops your running programs for a few seconds. If OpenVP can't write to its own folder (for example Program Files), it saves the new version to Downloads instead. To check right away, press Alt and choose **Help → Check for updates…**.

**Working on OpenVP** (needs the .NET 8 SDK and Node.js): double-click **`Start OpenVP.bat`**, or run `npm install` once and then `npm start` in `desktop/`. That opens the app on your source code: edits to the web pages (`src/OpenVP.Server/wwwroot`) show up in the window straight away, and C# changes rebuild and restart the server automatically (about 10–15 seconds) and the window reloads. To build a standalone `OpenVP.exe` on your PC, run **`publish-desktop.bat`** (or `npm run build` in `desktop/`); it writes `desktop\dist\OpenVP.exe`.

Or with Docker:

```bash
docker build -t openvp .
docker run -p 8080:8080 -p 41794:41794 -v $PWD/data:/app/data openvp
```

The container prints its access key at startup (`docker logs <container>`), and it's saved in `data/apikey.txt`. To choose your own, add `-e OpenVP__ApiKey=your-long-random-key`. For a long-running install use Docker Compose or the Helm chart, see [Deploying with Docker Compose and Helm](#deploying-with-docker-compose-and-helm).

### Try the samples

The **Try the demo** button does this for you. The built samples ship with the server (in `samples/` next to it, in `OpenVP.exe` and Docker too), so it needs no build and asks before replacing anything already in slots 1 or 2. To do it by hand:

```bash
dotnet build samples/SampleProgram          # a small SIMPL# Pro program
# then in the web console:
#   slot 1  ← samples/SampleProgram/bin/Debug/net8.0/SampleProgram.dll
#   slot 2  ← samples/SimplWindows/SampleProgram.smw  (also select SampleProgram.openvp.json)
```

Both register a panel at IP ID 03 and an EISC at IP ID 10 on 127.0.0.2. Press digital 10 on slot 1's panel and slot 2's panel lights up digital 10, and the reverse. That's a SIMPL# program and a SIMPL Windows program talking through the processor.

### Templates

**Templates** in the console's top bar (also in the search box) downloads two starter projects. Their files are in [`templates/`](templates) in this repo; the server builds the zips from them when you ask.

- **SIMPL# Pro starter** (`OpenVP-SimplSharp-Starter.zip`): `StarterProgram.csproj`, `ControlSystem.cs` (a touch panel at IP ID 03 and an EISC at IP ID 10, with comments on what each press does), a README and `lib/` with `SimplSharpPro.dll` and `OpenVP.Core.dll`. Unzip it, run `dotnet build` in `StarterProgram/` (needs the .NET 8 SDK), then load `bin/Debug/net8.0/StarterProgram.dll` into a slot. It builds against OpenVP's own copy of the SIMPL# Pro API under Crestron's assembly name (the same stub the sample program uses, see `samples/RefStub`), so the compiled program refers to `SimplSharpPro` as one built with Crestron's SDK does, and nothing in `lib/` is Crestron code. To build the same source for a real processor, point the two `<Reference>` lines in the project at Crestron's SDK.
- **CH5 / HTML panel starter** (`OpenVP-Ch5-Panel-Starter.ch5z`): `index.html`, `app.js` and `style.css` with buttons (digital joins 1, 2, 3 and 10), a slider (analog join 1) and text feedback (serial joins 2 and 10) on numeric joins, using `openvp-crcomlib.js`. Upload it for an IP ID (**Upload .ch5z…** in the IP table) and **Share…** it, see [Sharing a panel](#sharing-a-panel). It also works as a plain page: unzip it and open `index.html?host=localhost:8080&ipid=03&slot=1`, and it loads the join library from the console. It matches the SIMPL# starter (digital 1 lamp, analog 1 echo, serial 1 in upper case on serial 2) but any program works; change the joins.

## How it works

```
             ┌──────────────── OpenVP.Server (web console, REST, WebSockets) ───────────────┐
 browser ──► │  /ws/panel  ─┐                                         ┌─ /ws/trace (debugger)│
 CH5 page    │              ▼                                         │                      │
 touch panel │  CIP :41794 ─► JoinDevice (IP table, per slot) ◄──────►│  Processor           │
             │                    ▲            ▲                      │   slots 1–10         │
             │       SIMPL# runtime│            │SIMPL Windows runtime │   IntersystemBus     │
             │   (your .dll + API shim)      (.smw interpreter)        │   (EISC links)       │
             └────────────────────────────────────────────────────────────────────────────────┘
```

| Project | What it does |
|---|---|
| `OpenVP.Core` | Slots, IP table, join devices, per-slot event thread, log, trace, EISC bus |
| `OpenVP.SimplSharp` | Clean-room implementation of the SIMPL# / SIMPL# Pro API (`Crestron.SimplSharp.*` namespaces) |
| `src/Facades/*` | Empty assemblies named like Crestron's (`SimplSharpPro.dll`…) that forward every type to the shim |
| `OpenVP.SimplSharpHost` | Loads a program into its own unloadable load context; compatibility scanner |
| `OpenVP.Simpl` | `.smw` parser and interpreter, symbol survey |
| `OpenVP.Lpz` | `.lpz` reader and runtime, and the logic symbol library both engines share (`LogicSymbolCatalog`) |
| `OpenVP.Panels` | Browser panel WebSocket protocol and the CIP server |
| `OpenVP.Server` | The app: web console, REST API, debugger stream, autoload |
| `templates/` | Files of the two starter projects (SIMPL# Pro, CH5 panel), shipped with the server and zipped on request |

### SIMPL# programs

A compiled program references Crestron's assembly names. Each slot gets its own .NET load context in which those names resolve to **facades** that forward to OpenVP's API. The program itself is never changed. Every program event (panel presses, `CTimer` callbacks, socket data) runs on that slot's own thread, one at a time.

Before running, OpenVP reads the program's metadata and lists every Crestron type and member it uses that isn't implemented yet. Click **Compatibility** in the console, or check the log.

Implemented so far: `CrestronControlSystem`, `InitializeSystem`, program status events, `CrestronConsole` (print, console commands), `ErrorLog`, `CTimer`, `CrestronInvoke`, `CrestronThread.Thread`, `CCriticalSection`/`CMutex`/`CEvent`, `InitialParametersClass`, `TCPClient`, `ComPort` (`ControlSystem.ComPorts[1..8]`, virtual: see [COM terminal](#com-terminal)), `BasicTriList` with all six sig collections and `SigChange`, touch panel and XPanel classes, and local EISC (`ThreeSeriesTcpIpEthernetIntersystemCommunications`).

### SIMPL Windows programs

OpenVP reads the `.smw` source, not the compiled `.lpz`, so nothing of Crestron's runtime is decompiled. The file is a list of blocks: `Sm` symbols point at `Sg` signals by handle (`I1=4` = input pin 1 is wired to signal 4), and pins are grouped digital, analog, serial (`n1I`, `n2I`, `n3I`). When a signal changes, every symbol listening to it runs and its outputs drive further signals until the logic settles. The `1` signal goes high at start, as it does on a processor.

**Logic symbols** come from one library shared with compiled programs, so a symbol behaves the same whether you load the `.smw` or the `.lpz`. [docs/symbols.md](docs/symbols.md) lists all of them with their SmC code, compiled type and how sure we are of each: *compiled* symbols were checked against real compiled programs and their source; *inferred* ones were seen compiled in real programs, with behavior worked out from how those programs wire them (mostly inside macros whose source isn't available); *documented* ones were written from the SIMPL symbol descriptions and still need a real program to confirm their pins. All 96 compiled programs in our private test set of real programs load and run (they belong to their owners, so they aren't in this repo); only three compiled types in them (107, 113, 261, 37 records in all) are still unidentified. **Load the `.smw` together with its compiled `.lpz` and `.sig`** (select all three, or put them side by side) and OpenVP runs user macros (`.umc`/`.cmc`) and SIMPL+ modules from the compiled program, which the `.smw` alone can't: their source files aren't part of it. In fact every logic symbol then runs from its compiled record, with the compiler's pin order and parameter units, and signal kinds come from the `.sig`; devices, panel children, ports and crosspoints still run from the `.smw`. Each `.smw` symbol is matched to its compiled records by its position under the Logic folder, and only used if every program-level signal those records touch is wired to that symbol, so a `.smw` edited after the compile never picks up the wrong logic (those symbols run natively, or are reported unsupported). On those programs, a `.smw` run this way ends up with the same signal values as the `.lpz` run, apart from timing (clocks, start-up pulses) and network devices. Symbols disabled in SIMPL Windows (`OpF=0`) are left out, as the compiler does. Without the `.lpz`, macros and SIMPL+ aren't run, and a few symbols read their pins in the wrong order (Multiple One Shots, Set/Reset Latch, Toggle), so prefer loading all three. In a `.smw`, parameters are converted the way the compiler does: times to 1/100 s, `\x0D`-style escapes in strings decoded. Device symbols (panels, XPanels, EISC) map their pins to joins. Only devices on the processor's Ethernet slot get an IP ID (taken from the hardware tree in the file). What hangs off a panel is mapped too: Smart Graphics objects and Contract Editor components (their joins travel with the object ID, `P1`), and reserved-join extenders such as button toolbars, app control and hard buttons, at the join bases read off those compiled programs (`src/OpenVP.Simpl/ExtenderJoins.cs`). Crosspoint routing (Equipment, Control and Connect) runs as in compiled programs, and COM and IR port symbols are logged and handed to the port bridge like compiled ports (below). Other sub-devices (DM/NVX inputs and outputs, card slots) and processor hardware (relays, I/O, system control) aren't mapped yet; any that are wired are listed once in the log and in [docs/unsupported.md](docs/unsupported.md).

**Two things the format doesn't make clear, so you may need to help:**

1. **Symbol codes.** Symbols are identified by a numeric `SmC` code, and Crestron doesn't publish the list. OpenVP matches by the symbol's library name ("AND", "Interlock"…), then by the codes it knows. To teach it codes, add `MyProgram.symbols.json` next to the program (or `data/symbols.json` for all programs): `{ "1234": "AND", "1240": "Interlock" }`. Compiled programs have the same problem with primitive types; name a type with `MyProgram.primitives.json` (or `data/primitives.json`): `{ "301": "NAND" }`. Names must match [docs/symbols.md](docs/symbols.md); unknown ones are reported in the slot log.
2. **IP IDs.** Where an IP ID can't be found in the file, set it in `MyProgram.openvp.json`, keyed by symbol handle: `{ "ipids": { "21": "03" } }`.

The fastest way to close these gaps is real programs. **Symbol survey** reads any number of them and works out the codes for you:

```bash
dotnet run --project tools/SymbolSurvey -- path/to/programs --out survey
```

It lists every symbol the programs use and whether OpenVP runs it. For each compiled `.lpz` that comes with its source (archived inside, or a `.smw` of the same name next to it) and its `.sig`, it pairs each compiled record with the source symbol that drives the same signal. That identifies the symbol's SmC code and compiled type in one go, and it flags any conflict with what OpenVP already believes. With `--out` it writes `symbols.json` and `primitives.json`, ready to drop into `data/`. The quickest test program: one of each symbol from the SIMPL Windows logic library, each output on a named signal, compiled with the source archived.

### IR and COM ports

A virtual processor has no IR emitters or serial ports, so compiled programs' IR ports and COM ports (including those of DM-NVX encoders and decoders) don't talk to hardware. They still run honestly: every IR command (a rising trigger on an IR driver symbol) and every COM transmission is written once to the slot log, e.g. `IR IPID 31 port 1: DRV.CBL.1.KEYPAD.1 (cmd 0)` or `COM IPID 11 port 1 TX (7): "PWR ON\x0D"`, and the signals involved show in the debugger trace. To feed a program a COM reply, use the [COM terminal](#com-terminal) (or force the port's receive signal in the debugger); it is logged as `RX`. IR ports on the control processor itself read `IR processor slot 6 port 1`. Programs loaded from `.smw` source behave the same; their ports are named after the parent symbol and port number (`CP4 COM/COM-01`), and an IR command is identified by its position in the driver and its name (`IR processor port 2: MUTE (cmd 1)`). `comports.json` applies to compiled programs only.

To reach a real serial device through a serial device server, add `comports.json` to the program folder (or `data/`) mapping a port to `host:port`, e.g. `{ "IP-ID-11/Slot-08/COM-01": "192.168.1.50:4999" }`. Code that can reach real ports (say, NVX endpoints) can implement `IPortBridge` and set `LpzRuntime.PortBridgeFactory`; nothing ships that speaks to NVX hardware yet.

### Device simulators

A **simulator** is a fake device that answers a program's COM port or TCP/UDP connection with canned replies, so you can test a program with no hardware. Open **Devices ↗** in the console's top bar (or `/devices.html?slot=N`); the **Simulators** tab lists them for the selected slot.

- **Generic profiles.** **New simulator…** starts from one of four profiles: a *generic projector* (power on/off/query, input select, lamp hours: `PWR ON\r`, `PWR?\r`, `INPUT HDMI1\r`, `LAMP?\r`), a *generic display* (power, input, volume, mute), a *generic DSP* (volume and mute on the named levels `Program`, `Mic1`, `Mic2` and `Aux`: `SET Mic1 VOL 70\r`, `GET Aux MUTE\r`) and a *generic video switcher* (4x4: `ROUTE 2 3\r`, `ROUTE? 3\r`, `STATUS\r`). They are made-up, plain-text protocols of the command-space-value-carriage-return kind that small AV devices commonly use. **They are not any manufacturer's protocol**; copy one and edit its rules to match your device's documentation.
- **Attaching.** A simulator attaches to a **COM port** of the program (the same names as `comports.json`: `IP-ID-11/Slot-08/COM-01`, or just `COM-01` for a SIMPL# program's processor port; the box offers the ports the running program has opened), or it **listens on a local TCP or UDP port** (127.0.0.1 only; 0 picks a free one) that the program connects to. To make a program that connects to a real device's address (say `192.168.1.50:23`) talk to a simulator without changing the program, add a [network redirect](#network-redirects).
- **State, live.** A simulator keeps its state (power, input, level, ...) and the **Simulators** tab shows it, updating as the program talks to it. Click a value to change it; **Reset state** puts the starting values back. State is kept while OpenVP runs and the starting values are saved with the simulator.
- **Rules.** **Edit rules…** is a rule editor: *when received* matches `PWR ON\r` (literal, or a regular expression, optionally ignoring case), *reply* `PWR=1\r` *after* 50 ms, and *set state* `power=1`. Matching is against what has arrived so far, so a command split across packets or two commands in one packet both work; the matched text is used up. Of several rules matching, the one whose match starts first wins, then the first in the list. Escapes in rules and replies are `\r \n \t \0 \\` and `\xNN`. In a reply, **`{1}`** is a regular expression capture group, **`{name}`** is a state value (and `{vol:D3}` formats a number: `D3` zero-pads, `X2` is hex); in a *set state* value, `{vol}+5` is worked out (`+`, `-`, `*` on whole numbers), and names can use captures too (`{1}.vol` sets `Mic1.vol` when group 1 is `Mic1`). **Try** on a simulator sends a message to a copy of it with its current state and shows what would be sent and after how long, without touching the real state or the program.
- **On its own.** A simulator can send **timed messages** (every N ms while it runs, to everyone connected) and **connect messages** (when a TCP/UDP client talks to it for the first time, or when the program opens the COM port).
- **Saved per slot.** Simulators, network redirects and the [IP ID remap](#ip-id-remap) are saved in the slot's folder (`data/program01/devices/`: `simulators.json`, `redirects.json`, `ipremap.json`) and included in the slot's snapshots, so rolling back restores them with the program. They are kept when a program is unloaded. At most 20 simulators per slot. The engine is `SimEngine` in `OpenVP.Core`, with unit tests.

### COM terminal

The **COM terminal** tab shows, for any COM port the program has opened in the selected slot, what the **program sent (TX)** and what it **received (RX)**, live, with a timestamp and where RX came from (a simulator, this terminal, or a serial device server). Switch between **ASCII** (control characters as `\x0D`-style escapes, `\r`, `\n`) and **hex**; **Follow** keeps the newest line in view; **Clear** empties it; **Save session…** writes what is shown to a text file. Under it, type text and **Send as RX**: the program receives it on that port as if the device had sent it (escapes `\r \n \x0D` work, and the `+ \r` and `+ \n` boxes add a terminator). That replaces forcing the receive signal by hand in the debugger. It lists every COM port the program uses once it runs (compiled `.lpz`, `.smw` and SIMPL# `ComPort`); the last 2,000 lines of each port are kept in memory. IR ports aren't listed (they carry commands, not text and are in the slot log).

A SIMPL# program gets virtual COM ports `ControlSystem.ComPorts[1]`…`[8]` (`NumberOfComPorts` is 8): `Register()`, `SetComPortSpec(...)` (accepted and ignored), `Send(string)` and the `SerialDataReceived` event, which delivers text on the slot's thread.

### Sockets

The **Sockets** tab lists every socket the selected slot's program opens, client and server: SIMPL# `TCPClient`, `TCPServer` (the listener and each connection), `UDPServer`, `SecureTCPClient`, `SecureTCPServer`, and SIMPL+ TCP client, TCP server and UDP sockets. For each: kind, local and remote endpoint (and where a redirect sent it), state (connected, listening, closed, closed by remote, connect failed), bytes in and out, and when it was opened and closed. The last 200 closed sockets are kept. Click a row to see its traffic like the COM terminal (ASCII or hex, timestamps, save). It costs next to nothing when nobody looks: bytes are always counted, but the text is kept in full only while a Sockets view has polled in the last 30 seconds; otherwise only the last 30 short pieces are held per socket.

### Network redirects

A **redirect** says: when the program connects to *this address*, connect to *that one* instead. Add one in the **Redirects** tab: *Program connects to* is `host:port` (or just `host` for any port; `*` matches any host), *Send it to* is a simulator (`sim:ID`, whose local port is looked up when the program connects) or another `host:port`, and it can be limited to TCP or UDP and switched off without deleting it. The most specific entry wins (a port beats any port, a named host beats `*`). The redirect is applied where the program's socket is opened, in the SIMPL# `TCPClient` (and `UDPServer` sends) and SIMPL+ TCP client and UDP sockets, so it works for any program with no change; the log says `Network redirect: 192.168.1.50:23 goes to 127.0.0.1:44765`, and the Sockets tab shows both addresses. `SecureTCPClient` isn't redirected (a simulator doesn't speak TLS). The map is per slot, in `redirects.json`.

### Panels

- **Browser / CH5:** `ws://host:8080/ws/panel?ipid=03&slot=1`, JSON `{"t":"b","j":1,"v":true}` (`b` digital, `n` analog, `s` serial). `wwwroot/openvp-crcomlib.js` gives a page `CrComLib.publishEvent` / `subscribeState` for numeric join names, so simple CH5 projects can point at OpenVP. `panel.html` is a ready-made test panel.
- **Real panels over CIP (experimental):** listener on TCP 41794. CIP isn't publicly documented; the framing follows community write-ups and is unit tested against itself, **not yet against real hardware**. The shipped `appsettings.json` sets `OpenVP:CipLogPackets=true`, which logs every packet; compare with a Wireshark capture of a panel talking to a real processor. All encodings live in `Cip` in `CipServer.cs`. **Smart Graphics** (subpage reference lists, button lists, tab lists) are carried over CIP too, in both directions: item presses, analog and serial values from the panel, and visibility, enable, feedback and text back to it, including in the full update a panel asks for when it connects. Smart objects are identified by a 4-byte object ID inside the normal data packets. This is also experimental and untested on hardware: with packet logging on, any data sub-type OpenVP doesn't understand is logged once with its bytes.

A panel can connect before its program loads. It binds to the IP ID and comes online when the program registers it.

### Sharing a panel

To let someone else use a panel, click **Share…** on an IP ID in the IP table. OpenVP makes a link for that slot and IP ID. Send it, and the person opens it in their browser (a phone works well; the box also shows a QR code) and controls the program straight away. For someone outside your network, turn on [Internet access](#internet-access-tunnel) first.

- **What the link opens.** `/s/{token}` opens the panel for that slot and IP ID, and nothing else. The slot and IP ID are stored with the link, never taken from the address, so changing the address can't reach another one. The person can press joins on that IP ID and see its feedback. They can't open the console, the API, the debugger or trace streams, the files, other slots or other IP IDs. Their browser talks to OpenVP over a WebSocket at `/s/{token}/ws`, which does the same job as `/ws/panel` but only for that link.
- **The link.** The token is 192 random bits. A link has a label (only you see it), an optional **PIN** (4 to 8 digits), and an expiry of 1 hour, 1 day, 1 week or never. The console shows when it was created and last used and how many people have it open now. Links are kept in `data/shares.json` (readable only by its owner on Linux and macOS, like `apikey.txt`).
- **PIN.** Only a salted hash is stored (PBKDF2-SHA256), so a PIN can't be shown again; make a new link to change it. After a correct PIN the browser keeps a cookie for that link only (HttpOnly, SameSite=Strict, limited to `/s/{token}`). Wrong tries are limited: 5 wrong PINs from one address lock that address out of that link for 5 minutes, and 20 wrong PINs from all addresses together lock the link for 5 minutes.
- **Revoke and expiry.** **Revoke** (in **Shared panels**) deletes the link. Anyone connected is disconnected within a few seconds and their page shows "This link has expired or was revoked". The same happens when a link expires.
- **Which address goes in the link.** The address the console was opened with. If that is `localhost`, which means nothing to anyone else, the box offers this computer's network addresses instead. A link on a network address only works for people on that network. With [Internet access](#internet-access-tunnel) on, links use the tunnel's public address instead.
- **Test panel or CH5.** With no CH5 project for the IP ID, the link opens OpenVP's own test panel with the slot and IP ID fixed (no pickers). With a CH5 project, it opens that.

**CH5 (HTML5) panels.** Upload a CH5 project archive (`.ch5z`, which is a zip) for an IP ID with **Upload .ch5z…** in the IP table's details (or in the Share box), or `POST /api/slots/{n}/panels/{ipid}/ch5`. It is unpacked to `data/panels/slotNN/ipidXX/` and served to link holders under `/s/{token}/app/`. The archive must contain an `index.html` (at the top, or inside one folder). Paths that would escape the folder (`../`, absolute paths, drive letters) are refused, an archive over 100 MB, 300 MB unpacked or 10,000 files is refused, and nothing is installed if any entry is bad. OpenVP ships no Crestron files.

What works with CH5, and what doesn't:

- OpenVP adds its `openvp-crcomlib.js` to every HTML page of the project, before the project's own scripts, and sets it up for that link. Pages that call the global `CrComLib.publishEvent(type, join, value)` and `CrComLib.subscribeState(type, join, callback)` with numeric join names (`"1"`, `"27"`, types `b`, `n`, `s`) work: presses reach the program, feedback comes back.
- Projects normally bundle Crestron's own CrComLib. OpenVP pins `window.CrComLib` to its own version, so when the bundle loads and assigns `window.CrComLib` the assignment is ignored and the page keeps talking to OpenVP. A script that declares it with `let` or `const` at the top level would still fail.
- **Not covered:** the CH5 web components (`ch5-button`, `ch5-slider`, and so on) and anything else that uses the bundled library's own internal signal system instead of the global `CrComLib` functions, contract-editor signal names, and the library's own connection code. A project made entirely of CH5 components with contract names will load but will not control the program. This has been tested with a small hand-made page, not with real Crestron CH5 Studio output.
- Classic VT Pro-e XPanels are not CH5. They connect over CIP.

### Internet access (tunnel)

To let someone outside your network open a shared panel, turn on **Internet access** at the top of the console. OpenVP runs [`cloudflared`](https://github.com/cloudflare/cloudflared) as a Cloudflare "quick tunnel" (`cloudflared tunnel --url http://127.0.0.1:8085 --no-autoupdate`), which is free and needs no account. The console shows the public `https://….trycloudflare.com` address, the status (starting, on, error, off) and a **Stop** button. While it is on, share links use that address (so the QR code works from anywhere). Turning it off kills `cloudflared`, and all outside access stops. It is off at every start unless you set `OpenVP:TunnelAutoStart=true`. A quick tunnel's address changes every time it starts, so a link you copied earlier points at the old address (the link itself is still valid): copy it again from **Shared panels**.

**The tunnel only exposes shared panels.** It does not point at the console's port.

- OpenVP opens a second listener on `127.0.0.1:8085` (`OpenVP:TunnelPort`) and `cloudflared` is pointed only at that. Nothing on the console's port (8080) is reachable through the tunnel.
- Every request that arrives on the tunnel port is treated as remote, whatever its address or `Host` header. (`cloudflared` connects from 127.0.0.1, which would otherwise look local.) None of them gets the "local machine needs no key" exception.
- The tunnel port serves only `/s/{token}…`: the share page, its WebSocket, its CH5 files and the one script they need, all under the link's own address. Everything else (the console, `/api/*`, `/ws/trace`, `/ws/metrics`, `/ws/panel`, the debugger, help and metrics pages) is a plain 404, even with the right access key.
- `/s/{token}…` needs the link's token (and the PIN, if the link has one). A request to the console's own port that carries `Cf-Connecting-IP`, `Cf-Ray` or `CF-Visitor`, as well as `X-Forwarded-For`, `Forwarded` or `X-Real-IP`, is never treated as local either.
- If the tunnel port is the same as the console's port, or is already in use, Internet access is switched off (the console says why) rather than risk it.

**Where `cloudflared` comes from.** OpenVP uses `OpenVP:CloudflaredPath` if that is set, else `cloudflared` on the PATH, else the copy it downloaded earlier into `data/tools`. If there is none, the first time you turn Internet access on, the console asks. Only if you click **Download cloudflared and start** does OpenVP download Cloudflare's official build for your system from `https://github.com/cloudflare/cloudflared/releases/latest/download/…` (over HTTPS) into `data/tools`. On macOS Cloudflare publishes a `.tgz`, which OpenVP unpacks. There is no signature check beyond HTTPS from GitHub, so install `cloudflared` yourself and set `OpenVP:CloudflaredPath` if you want to control that.

**Limits.**

- Classic VT Pro-e XPanels and the Crestron apps connect over CIP (TCP 41794), which cannot go through this tunnel. They need port 41794 reachable directly (same network, VPN or a port forward). CIP support is experimental and untested with real XPanels. The tunnel is for browser panels and CH5 projects.
- Quick tunnels have no uptime guarantee and are subject to Cloudflare's terms. For something permanent, run your own named tunnel or reverse proxy in front of the tunnel port.
- On Windows and in the desktop app, `cloudflared` is stopped when OpenVP stops. If the OpenVP process is killed outright on Linux or macOS (`kill -9`), `cloudflared` can be left running; it is harmless (it can only reach the tunnel port) but stop it yourself.

### Room ID

Each slot has a **Room ID**, like the Virtual Control Room ID of a VC-4 program. It defaults to `SLOT01`, `SLOT02`, and so on; change it with `PUT /api/slots/{n}/room` (1-64 letters, digits, `_`, `-` or `.`; unique across slots, not case-sensitive). It is saved in `slot.json` in the slot's folder and is what a program reads from `InitialParametersClass.RoomId` and `RoomName`. Browser panels can connect with `/ws/panel?ipid=03&room=ROOM2` and are routed to the slot with that Room ID (an unknown Room ID gets a 404). Panels connecting over CIP still route by IP ID only for now, binding to the lowest slot that has it; the log warns when two slots register the same IP ID.

### Console features

- **Drop anywhere.** Drop a `.cpz`, `.dll`, `.lpz` or `.smw` (with its `.sig` and sidecars) anywhere on the console; a box names the slot, shows whether the program will run, and loads it into the selected slot when you click **Load & start**. In the desktop app it loads from disk like **Load & start**. Dropping on the Files list still uploads files there.
- **Will it run?** When you choose or drop a file, OpenVP reads it without loading it: *Will run* (green), *Will partly run, N things aren't supported yet* (amber, with the list) or *Won't run* (red, with the reason: not a program, damaged, a `.dll` with no `CrestronControlSystem`…). It loads the file into a throwaway one-slot processor that is never started, so nothing runs and your slots are untouched, and it uses the same compatibility code as the **Compatibility** button. Advice only: loading is never blocked. `POST /api/check`.
- **Program summary.** One plain sentence under the program name, in the readiness check and in the Status board tile's tooltip: *SIMPL Windows · 3 touch panels (IP IDs 03, 04, 05), 2 EISCs, 4 COM ports, 1 IR port, 12 SIMPL+ modules, 2 user macros, 1,284 signals.* It counts what the runtime already knows: devices by IP ID (touch panels, EISCs, others), COM and IR ports, SIMPL+ modules, user macros (from a compiled `.lpz`) and signals. A SIMPL# program makes its devices when it starts, so before that the summary names the device types it uses. `GET /api/slots/{n}/summary`.
- **Try the demo.** Loads the SIMPL# sample into slot 1 and the SIMPL Windows sample into slot 2, linked over EISC, starts them, opens slot 1's test panel and says what to press (digital 10) and what you will see. It asks first if slots 1 or 2 already have programs (their programs are snapshotted before they are replaced).
- **Help on any control.** Hover a button or box and click the small **?** that appears, or hold `Shift` and click it, press `F1` on it, or long-press it on a touch screen: the help window opens at that control's section (`help.html#anchor`; controls are tied to anchors with `data-help`). The search box (`Ctrl+K`) also finds help topics.
- **Activity.** A processor-wide feed of who did what and when, newest first: loads, unloads, start, stop and restart, roll backs and snapshots, recordings, share links made, opened and revoked, CH5 uploads, Internet access on and off, settings changes, crash-policy actions, auto-reloads and start-up restores. *Who* is **this computer**, an address, **share link …** or **auto**; a user name will be added when OpenVP has accounts. The last 5,000 entries are kept in `activity.jsonl` in the data folder (JSON lines). The console shows it in **Activity** (filter by slot) and the Metrics page shows the latest. `GET /api/activity?slot=&limit=`.
- **Recent.** The last 10 programs loaded (`data/recent/`). A path load is remembered by its path; an upload by name, with its files kept (up to 100 MB each, 400 MB in all). **Recent ▾** loads one into the selected slot.
- **Search and shortcuts.** `Ctrl+K` / `Cmd+K` opens a fuzzy search over every action, and signal names from every loaded slot. Keys (never while typing in a box): `1`–`9`, `0` select a slot, `S` start, `X` stop, `R` restart, `U` unload, `D` debugger, `P` paths, `M` metrics, `N` snapshot, `L` log filter, `F` find a signal in all slots, `T` theme, `?` lists them. The debugger lists its F-keys and star, note, why and watches keys with `?`. See [Log](#log).
- **Settings save themselves.** Slot settings save as you change them (a "Saved" mark shows). Only the Room ID is edited in a dialog.
- **Theme.** Auto, light or dark on every page, remembered per browser. The debugger waveform and the Paths map stay dark.
- **Slot name and colour.** Saved in the slot's `slot.json`; shown on the board, the slot table, page titles and the debugger and metrics headers.
- **Bulk actions.** Tick slots on the board or table, then Start, Stop, Restart or Unload them together (`POST /api/slots/bulk`). Stop, Restart and Unload ask once.
- **Phone.** Sections collapse, a slot picker stays at the top, touch targets are bigger and tables become cards; no sideways scroll at 390 px.
- **Undo.** Unload, deleting a file, folder, snapshot or recording, saving an edit in the [file editor](#file-editor), changing a Room ID and revoking a share show an Undo message for 10 seconds. Unload takes a snapshot first. A saved file edit also shows Undo. Deleted files, snapshots and recordings, and the earlier content of an edited file, wait in `data/trash` for 24 hours. A revoked link comes back (same token, PIN and expiry) if undone within 10 minutes; that is kept in memory only.

### Autostart and crash policy

Each slot remembers its program in `slot.json` (`program`: the uploaded copy, or the path of a file loaded from disk, plus whether it was running and whether auto-reload was on). When OpenVP starts it restores the slots in **start order** (lowest first, ties by slot number; default is the slot number), each after its **delay** in seconds, in the background. Slots with **autostart** off stay empty; `OpenVP:Autoload` entries always load and win over the remembered program. What was restored is in the slot log; a program that can't be restored puts only that slot in Error. Unload forgets the program; stopping OpenVP does not.

The **crash policy** is per slot: stay stopped (default), restart, or restart up to N times in M minutes and then stay stopped. A crash is a running program that faults: an unhandled exception in an event handler (only if "Unhandled exception = crash" is on; otherwise it is only logged), or one event running for longer than "Stuck after" seconds (0 = never). A program that fails to load or start is not a crash, so a broken program is never restarted in a loop, and plain restart stops after 10 restarts in a minute. Each action is logged and shown on the slot's board tile. Restarting a stuck slot waits for its stuck thread, up to about 35 s.

### Status board

The row of tiles at the top of the console shows every slot at a glance: program, state, panels online, events per second, and a red **problem** badge (hover it to read the problem) when the slot is in Error or the Metrics page would list a problem for it. Click a tile to select that slot. A slot that reloads when its file changes shows an **auto-reload** badge. The board refreshes every 2 seconds.

### IP table

The **IP table** on the console's main page shows the selected slot's IP IDs, one row for each connected client. Each row starts with who the client is, then its address, IP ID, type, status, how long it has been connected, and how many joins went each way. An IP ID with nobody connected shows when it was last seen. Click a row to see everything known about that client and IP ID. The table refreshes every 2 seconds. The `ipt` console command prints the same list. **Remap IP ID…** in the detail changes the number panels use, see [IP ID remap](#ip-id-remap).

How OpenVP works out who a client is:

- **Without credentials, automatically:** it uses the remote address and port, and reverse DNS for the hostname. A client on the OpenVP machine itself is named after this computer. For CIP clients it also sends **Crestron autodiscovery** (UDP 41794). Crestron hardware answers with its hostname, model, firmware and MAC, so a row reads e.g. *Crestron touch panel (TSW-1070)*. XPanel and the Crestron apps don't answer, so a CIP client with no reply is listed as *Software client (XPanel or Crestron app?)* under its computer's hostname. The CIP connect request bytes are kept as well, to help tell client types apart later. Autodiscovery is **experimental**, like CIP: it follows community write-ups and hasn't been tested on every device. **Re-identify** runs these checks again.
- **Browser and CH5 panels:** browser and OS come from the User-Agent, and pages using `openvp-crcomlib.js` also report their screen size, platform, language, time zone and round-trip time.
- **EISC:** the row names the slot and program on the other end of the link.
- **Log in & read…** (optional): type the device's SSH username and password. OpenVP runs only read-only console commands (`hostname`, `ver`, `uptime`, `ipconfig`, `ipt`, `time`), shows what they return, and fills in hostname, model, firmware and MAC from them. The credentials are used for that one login and are never stored or logged.

Metrics for each client: connected since, last message, messages and bytes in and out, joins in and out, heartbeats (and the interval between them for CIP), round-trip time (browser panels), packets waiting to be sent (CIP) and errors. For each IP ID: registered, last online and offline, connections since registered, inputs received and feedback sent.

### IP ID remap

**Remap IP ID…** in the IP table's detail makes panels reach a program's IP ID at a different number, without recompiling: "the program's IP ID 03 should answer at 0A". Panels (browser, CH5, shared links, CIP touch panels and XPanels) connect to the **public** ID, `0A`, and drive the program's own `03`; the program still sees `03` and registers it as usual. The IP table shows `03` with `at 0A` under it, the detail says which number panels use, and the `ipt` console command prints `IPID 03 (at 0A)`. The test panel, **Share…** links and CH5 uploads use the number panels connect to (the public one).

- Once an ID is remapped, panels connecting to its old number reach nothing (they get a placeholder device that drives no program) until the remap is undone. Two IDs can swap (03 to 0A and 0A to 03).
- A public ID can't be one the program registers itself and hasn't remapped, can't be used twice in a slot, and goes from 01 to FE.
- It is applied where a panel finds its device (`DeviceDirectory`, the one place the WebSocket panels, share links and CIP all use), so it works for SIMPL# programs, `.lpz` and `.smw` alike. A panel that connects before the program registers waits under the program's own ID, as always. Recordings, the debugger and EISC links use the program's own IDs. A share link made before a remap keeps its old number: make a new one.
- Saved per slot in `devices/ipremap.json`, and in snapshots.

### Program files: NVRAM and User

Each slot has its own file system, like a program on a processor: an **NVRAM** folder and the built-in **User** folder. Use them for config files your program reads at startup (room lists, IP tables, JSON settings).

In the console, the **Files** section for the selected slot lets you switch between NVRAM and User, create folders, upload files (or a whole folder, or drag them in), download, rename and delete. The path shown on the right (for example `\USER\config`) is what the program uses.

Programs see the folders under their usual processor paths:

| The program opens | On disk |
|---|---|
| `\NVRAM\settings.json`, `/nvram/settings.json` | `data/program01/nvram/settings.json` |
| `\USER\config\rooms.json`, `/user/config/rooms.json` | `data/program01/user/config/rooms.json` |
| `Path.Combine(Directory.GetApplicationRootDirectory(), "user", "x.json")` | `data/program01/user/x.json` |

**Program folders.** On a processor, SIMPL+ `MakeProgramFilename("\NVRAM\config.txt")` gives each program its own folder: `\NVRAM\{Program ID Tag}\config.txt`, using the Program ID Tag from the SIMPL Windows program header. OpenVP does the same, and SIMPL# `InitialParametersClass.ProgramIDTag` returns the same tag. For a `.smw` the tag is the header's `PIT`; for a `.lpz` it is `FriendlyName` in `boot.bt` (or `PIT` in the archived `.smw`). A program without a tag falls back to its `.smw` file name. (The header's `PgmNm` is the Programmer field, so it isn't used.) When a program is loaded, the Files section shows that folder's name with a button to create or open it, so your config file goes in the right place.

**Missing config files.** When a program looks for a file or folder in NVRAM or User that isn't there (it checks `File.Exists`, opens a file to read, lists a folder, or SIMPL+ `FileOpen`s without creating), OpenVP tells you where it belongs. The slot log says once which folder to create and where to put the file, both as the program sees it and on disk:

```
The program looked for \USER\config\rooms.json but it isn't there. Create the folder \USER\config\ and put rooms.json in it, in the web console (Files, User tab) or on disk at data/program01/user/config.
```

The Files section lists everything still missing, each with a button to create its folder (and any folders above it) or open it, ready for you to upload the file. An entry goes away once the file is there. Opening a file to read no longer creates an empty folder along the way; writing still creates whatever folders it needs.

The folder names match whatever case the program uses (`\USER`, `/user` and `\User` are the same folder, on Linux too). This works for SIMPL# (`Crestron.SimplSharp.CrestronIO`) and SIMPL+ file functions. A program can't reach outside its slot's folder. The files stay when you load a new program into the slot.

### File editor

**Edit** on a text file in the Files section opens it in an in-browser editor: a plain textarea with line numbers (no editor library, nothing from a CDN). It opens `.json`, `.xml`, `.txt`, `.csv`, `.tsv`, `.ini`, `.cfg`, `.conf`, `.usp`, `.smw` and similar text types up to **2 MB**; programs and binary files (any file with a zero byte in it) are refused. The engine is `TextFileEditor` in `OpenVP.Core`, with unit tests.

- **Validation.** JSON and XML are checked as you type and again on save, and the status line names the line and the problem (`Line 3: ',' is an invalid start of a value`), with that line number in red. A save of invalid JSON or XML is refused with the line; **Save anyway** overrides it. XML with a DTD is refused, so no entity can read a file from the computer.
- **Undo.** Every save first copies the old content into the trash (`data/trash`, 24 hours), and the Saved message offers **Undo** for 10 seconds, which puts the earlier content back over the new one.
- **Changed on disk.** The editor remembers a hash of what it opened. If the file changes on disk meanwhile (the program rewrote it, someone uploaded a new one), a yellow bar says so within a few seconds and offers **Reload from disk** or **Keep my version**; and a save over a changed file is refused with **Save anyway**. A file deleted meanwhile is the same.
- **Line endings and encoding.** A file with CRLF line endings keeps them, a UTF-8 byte order mark is kept, and a file that isn't valid UTF-8 is read and written as Latin-1.

Edits, uploads, renames and deletes in Files are written to [Activity](#console-features) (and so to [Git-backed config](#git-backed-config)).

### Snapshots and roll back

Before OpenVP loads a program into a slot that already has one, it saves a **snapshot** of the slot: the program files plus its NVRAM and User folders. The **History** section of the console lists them, newest first, with the time, program, reason (`before load`, `before roll back` or `manual`) and size. Each slot keeps the last 10; older ones are deleted. **Snapshot now** saves one on demand. A snapshot never blocks a load: if it can't be saved, the slot log says so and the load carries on, and a file that can't be read is skipped and noted.

**Roll back** puts the slot back as it was: the running program is unloaded, the program files, NVRAM and User folders are replaced with the snapshot's, and the program is loaded again (and started, if it was running). The current state is saved first as a `before roll back` snapshot, so a roll back can be undone. Rolling back stops "reload when the file changes" for the slot. The Room ID (`slot.json`) isn't saved or restored. Snapshots are in `data/snapshots/slotNN/`.

### Backups and export

**Backups ↗** in the console's top bar (or `/backup.html`) has three tabs. The logic is in `OpenVP.Core.Ops` (`ProcessorExporter`, `ProcessorImporter`, `BackupService`), with unit tests and tests against real servers.

**Export processor** downloads one `.openvp` file: a zip with a `manifest.json` (format name, format version, when, which computer, a summary of every slot) and, for every slot, its program files (`slots/NN/program`), `slot.json`, NVRAM and User folders, device simulators, redirects and IP ID remap, integrations (`devices/integrations.json`), watches (`debug.json`), recordings and bookmarks; and the processor-wide settings: integrations, alerts (channels, rules, thresholds and the report schedule), schedules, push targets, backup and version settings, symbol and primitive maps, the console and mDNS options as they are configured, and the recent programs list (paths only, no copies).

| Included | When |
|---|---|
| Programs, NVRAM and User, slot settings, simulators, integrations, recordings, bookmarks, watches, processor settings | Always |
| History: uptime and metrics files, the activity feed, alert history, saved traces | Tick **Include history** |
| Snapshots and kept program versions | Tick **Include snapshots and program versions** |
| Secrets (email, MQTT and webhook passwords and signing secrets, auth headers of outgoing webhooks, share-link and incoming-webhook tokens, push-target keys) | Tick **Include secrets** and type an export password |
| The access key | **Never** |

Secrets are only ever written inside `secrets.enc`: the secret store's values, share links and incoming webhooks, and the unredacted integration files, encrypted with **AES-256-GCM** under a key derived from your password by **PBKDF2-SHA256 (210,000 iterations, random salt)**, with the header authenticated too. A password has to be at least 8 characters; nothing can recover a lost one. Left out, none of them are in the file at all (the exporter also cuts webhook secrets and `Authorization`-like headers out of any `integrations.json` it copies, including those inside snapshots).

**Import processor.** Choosing a file shows a preview and changes nothing: who made it and when, the format version, each slot (its program, name, files, whether it starts, and what the slot holds now, which it will replace), the processor settings in the file, and whether it has secrets (type the password and the box says whether it is right). You choose all or some slots, and whether to **replace the processor settings**. Then, for each slot, OpenVP:

1. unpacks everything chosen into a staging folder first, checking as it goes. A bad file is refused before anything live is touched: a different format version (`wrong-version`), an entry name that would land outside the data folder (`..`, absolute paths, drive letters: zip-slip, `unsafe-path`), more than 100,000 entries, one file over 2 GB or more than 8 GB unpacked, a ratio that looks like a zip bomb, or a duplicate name (`too-big`, `bad-archive`). The byte counts are checked on what is actually read, not on what the header claims;
2. takes a **snapshot** of the slot (`before import`), stops its program, and swaps in the files;
3. restores the slot's settings, remembering the program as an uploaded copy (a path on the other computer means nothing here). A Room ID another slot already holds isn't used;
4. loads the program and **starts it as the slot's autostart setting and its state when exported say**: autostart on and running when exported loads and starts it; autostart on and stopped loads it stopped; autostart off restores the files and settings but leaves the slot unloaded, as after a restart;
5. keeps the program as a [version](#versions-and-diff) (`import`).

Replacing processor settings copies the old files to `data/tmp/settings-before-import-<time>` first. Console and mDNS options are configuration (command line, environment, `appsettings.json`), not data, so they are saved as `imported-options.json` for you to read and are not applied. A wrong password stops the whole import before anything changes. Snapshots, versions and history in the file are only added if you tick **restore history**, and they are added to, never replacing the snapshot just taken.

**Daily backups.** The **Backups** tab backs the whole processor up in the same export format at a time you choose (this computer's time zone, daily; on by default at 03:00), plus **Back up now**, into `data/backups` or any folder you type, such as a mounted network share. They are named `openvp-backup-yyyyMMdd-HHmmss.openvp` (UTC) and kept for N days (default 14); older ones are deleted after each backup, and the newest is always kept. A backup is written to a `.partial` file and renamed when done. If OpenVP wasn't running at the daily time, the backup runs **once when it starts**. The settings (`data/backups.json`) can also tick history, snapshots and secrets; with secrets, the password is kept in the secret store (`backup.password`) and every backup's secrets are encrypted with it. **If a backup fails** (folder not writable, share gone, disk full) a Critical `backup.failed` [alert](#alerts) is raised through your rules and the activity feed, and the next good backup clears it. **Restore…** opens the same preview as an import, so you choose the slots and settings; **Download** and **Delete** do what they say.

### Versions and diff

Every program loaded into a slot is kept as a **version** in `data/versions/slotNN/`: the program and the files that came with it (`.sig`, `.openvp.json`, the `.lpz` behind a `.smw`), by any route: upload, load from a path, auto-reload, roll back, import, a push from another machine, or loading an older version. A version has the program name, file, SHA-256 over the names and contents of its files (its id is the first 12 digits), size, when it was first and last loaded, how many times, how and by whom (`upload`, `path`, `auto-reload`, `roll back`, `import`, `push`, `version`, `schedule`, `demo`, `recent`) and an optional note you add. **The same files loaded again are the same version** (matched by hash): it only gets a new load time and count, so reloading on every compile doesn't fill the disk. Each slot keeps its newest **N** versions (default 30, **1 to 1000** on the page); older ones and their files are deleted, never the one loaded last. A bundle over 400 MB isn't kept.

**Versions…** in the History section (or `/versions.html?slot=N`) lists them. **Load this version** loads and starts it after a snapshot of what is there; **Download** gives the program, or a zip when it comes with other files; **Note…** adds a note; **Push…** sends it to other machines (see [Push](#push-to-other-machines)); **Delete** forgets one (not the loaded one).

**Compare…** reads two programs, or a version and the program loaded now, **without starting anything** (it loads a copy into a throwaway one-slot processor that is never started, the way [Will it run?](#console-features) does) and writes a report: devices and IP IDs added, removed or changed; signals added, removed or changed kind; for a SIMPL Windows `.smw`, the **symbols** by name and type (added, removed, or changed in parameters or in the signals wired to them: *Serial Send — parameters "Hello from SIMPL" to "Hello again"*, *AND — wiring: outputs no longer both_pressed, now both_pressed_v2*); SIMPL+ modules and, for a compiled program, its IP table; and which files changed. A compiled `.lpz` has no symbol list, and a SIMPL# program creates its devices and signals when it runs, so those are compared by what they do have, with a note saying so. `POST /api/slots/{n}/versions/diff` returns the report as JSON and as text (`ProgramDiff` in `OpenVP.Core`, `ProgramProfiler` in the server).

### Schedules

**Schedules ↗** runs an action at a time, per slot or for the processor: restart a slot (load its program again and start it), start, stop, load a program version into a slot, load a program file from this computer (only when set up from this computer), set a signal to a value, pulse a digital signal, or run a backup. The engine (`Scheduler`, `CronExpression` in `OpenVP.Core.Ops`) has unit tests including daylight saving changes and downtime.

- **When.** Once (a date and time), every day at a time, certain days of the week at a time, or advanced: a five-field cron expression (`minute hour day-of-month month day-of-week`, with lists `1,15`, ranges `mon-fri`, steps `*/10`, names, `@daily` and friends; as in cron, if both day fields are restricted a day matches when either does). The page shows the next few run times as you type, and each schedule's **next run**, **last run** and result, with a **Run now** button, an on/off switch and a history of the last 500 runs (`data/schedules.json`, `data/schedules-history.jsonl`).
- **Time zone and daylight saving.** Times are the processor's local time zone (the container's `TZ`, shown at the top of the page). When the clocks go **forward**, a time that doesn't exist (02:30 on the night 02:00 becomes 03:00) runs once, just after the gap. When they go **back**, a time that happens twice (01:30) runs the first time only, except a schedule whose hour field is a wildcard (`0 * * * *`, `*/15 * * * *`), which keeps running every hour or quarter hour through the repeated hour.
- **Runs missed while OpenVP wasn't running** (stopped, or the computer asleep): by default a missed run is **skipped** and the history says so ("OpenVP wasn't running at 03:00, so this run was skipped"), because restarting a slot at 10:00 for a 03:00 job is usually worse than missing it. Tick *run once if OpenVP wasn't running* on a schedule and it runs **once, when OpenVP starts**, if the missed time was within the last 24 hours (labelled "missed" in the history); several missed times count as one. A run counts as missed when it is more than two minutes late. Turning a schedule back on doesn't catch up. The daily backup always runs once when missed.
- A schedule that is still running when its next time comes is skipped for that time. A failed run (a slot with no program, an unknown signal, a path that isn't there) is a Warning `schedule.failed` [alert](#alerts) for that schedule, cleared by its next good run. Runs go to the activity feed as **auto**.

### Push to other machines

**Push ↗** (`/targets.html`) keeps a list of **targets**: other OpenVP servers, each an address (`http://192.168.1.20:8080`) and its access key. The key is saved in the secret store (`target.<id>.key`), never in `targets.json` and never shown again. It uses the other machine's own REST API: `POST /api/slots/{n}/program` to load a program, `POST /api/import` to import an export. **Test** connects, checks it is OpenVP and that the key is accepted.

- **A program version** (the loaded one or any kept version) is sent to the slot numbers you choose on each target, which stops the program there, snapshots it, loads the new one and starts it (or not). **A whole-processor export** (the slots you tick, optionally with the processor settings and secrets under a password) is imported there like an [import](#backups-and-export).
- **Many targets at once.** Targets run in parallel; the page shows each one's state and each slot's result as it goes (`queued`, `running`, `ok`, `failed` with the reason), and one failing (wrong key, unreachable, an older OpenVP without the endpoint) doesn't stop the others. A pushed program is a `push` version on the target.
- **TLS.** For an `https` target the certificate is verified: valid, trusted and matching the host name. For a self-signed certificate, **Fetch certificate** shows its fingerprint (SHA-256); typing or accepting it **pins that one certificate**: only a certificate with exactly that fingerprint is accepted for the target, whatever the chain says, and any other is refused. Plain `http` works but is marked in the form: the key and programs are not encrypted.
- Tested against second OpenVP servers started in the test process, including one serving https with a self-signed certificate (`PushTests`).

### Git-backed config

Optional. **Backups ↗ → Git-backed config** points OpenVP at a folder on this computer (outside the data folder; **Create repository** makes it and runs `git init`). It uses the `git` command line; if `git` isn't installed, the page says so and the feature stays off. After each change (a program load, settings, file edits and uploads: anything that is written to the activity feed except start, stop, restart, backups and the like), OpenVP writes a text-friendly copy of the configuration into the repository and commits it, with the **activity entry as the commit message**:

- `slots/NN/slot.json`, `slots/NN/devices/` (simulators, redirects, IP ID remap, integrations), `slots/NN/debug.json`, the text files in `slots/NN/nvram` and `slots/NN/user`, and the program files in `slots/NN/program` (up to a size you set, 50 MB by default);
- `processor/`: the integration, alert, schedule, push-target, backup and version settings and the symbol maps.

**Debounced:** the first change is committed about 3 seconds later, and never more than one commit every **30 seconds**; changes in between are gathered into one commit (subject: the last change plus "and N more changes", with all of them listed in the body). A commit that would change nothing isn't made. Files removed from the data folder leave the repository on the next commit.

**Never committed:** the access key; the secret store; share links and incoming-webhook tokens; push-target keys; webhook signing secrets and auth headers (cut out of `integrations.json`); binary NVRAM files; files whose name looks like a secret (`password`, `token`, `.key`, `.pem` ...); and any text file that contains a saved secret or the access key. **Commit now** says what was skipped and why. Optionally set a **remote** and **Push after each commit** (or **Push now**): it pushes `HEAD` to `origin` in the background using your own git authentication (SSH keys or a credential helper); OpenVP never asks for or stores a git password. The unit tests check the whole history for every secret (`GitSyncTests`).

### Record and replay

Use a recording as a regression test for a slot: record what a person does to a room, load a new program version, replay it, and get PASS or FAIL for whether the program still answers the same way. In the console, the **Recordings** section of the selected slot has **● Record** / **■ Stop**, and a list with **Replay**, **Replay fast**, **Result**, **Download**, **Rename**, **Delete** and **Upload**.

**What is recorded**, with its time since the start:

- **Inputs** from devices: digital presses, analog levels and serial text from browser panels and CIP panels (source `panel`), and what arrives over an EISC link from another slot (source `eisc`). Each has the IP ID, join and value.
- **Forces** from the debugger: set, clear, pulse, toggle and values, all of which go through `POST /api/slots/{n}/signals` (the debugger window and the Paths view use only that; nothing arrives over `/ws/trace`). A force is recorded once, as a force, even when it works by sending a device input.
- **Feedback** the program sends to each device, as a change of join and value, so there is something to compare. Devices that register after recording started (or again after a reload) are followed too.
- Each device's feedback when recording started and when it stopped.

A recording is saved as JSON in `data/recordings/slotNN/<id>.json` with its name (by default the program name and time), the program name and file at the time, its length and counts. A replay needs a running program; only one recording or replay runs per slot at a time, and either ends cleanly, keeping what it has, if the program is unloaded or replaced. Recordings are only as repeatable as the room: start recording from a state you can get back to, such as right after loading, because a replay sends the inputs and nothing else. Before a replay, every input join the recording uses is set back to what it held when recording started (a level left at 40000 by the last session would otherwise swallow the recorded 40000); the report notes which. What the program itself remembers can't be reset: toggles, interlocks and counters keep their state, so for a program that holds state, load it fresh before recording and again before each replay. A replay warns when the starting feedback differs from when you recorded, or when the first recorded input to a join equals the value the device already holds (an unchanged value is not an input).

**Replay timing.** *Replay* sends each input after the same gap as recorded, so timers and delays in the program behave as before. *Replay fast* shortens any gap longer than 1 s to 1 s and keeps shorter ones exactly. Feedback is expected at the same distance after the input before it as it had in the recording: a lamp that came on 2 s after a press is expected 2 s after the press, fast or not, because the program's timers run in real time. A program with long delays between inputs can therefore differ in fast mode; use the original timing for it. The replay ends 500 ms after the last expected feedback.

**How the comparison works.** Each recorded feedback change (device, join, value, time) must have a replay change to the same join and value within ±500 ms of where it is expected, and each replay change matches only one.

| Result | Meaning | Effect |
|---|---|---|
| matched | The value arrived in the window | |
| different | The join changed in the window, but to another value | FAIL |
| missing | Nothing arrived in the window | FAIL |
| extra | The replay changed a join the recording didn't, or at another time | warning |
| final state | A device ends with a different value for a join than the recording did (a join already listed as different or missing isn't listed again) | FAIL |
| input | A recorded input or force couldn't be sent: its IP ID isn't registered, or the signal name is unknown | FAIL |
| starting state | Feedback differs from when recording started, or an input wouldn't register | warning |

Extra feedback is only a warning, unless the device also ends in a different state. The windows are wide on purpose, so a program that flips a join back and forth within half a second can match by accident; the final state check still catches a different outcome. The report lists each difference with the IP ID, join, expected value, actual value and the time in the recording where it was expected, and the last 5 reports of each recording are kept.

**EISC.** Inputs that come in over an EISC from another slot are recorded as `eisc` inputs. On replay, *include EISC inputs* (on by default) sends them again, so the slot can be tested alone. If the other slot is running and drives that EISC itself, turn it off, or the inputs arrive twice (a value that is already there is not sent on, but timing can differ); the report notes which you chose. Feedback the slot sends out over its own EISC is compared like any other, and on a real replay it also reaches the other slot, which may react to it.

Download a recording to keep it with a project, and Upload it into any slot (it gets a new ID); the IP IDs and signal names must match the program. Nothing else may press, force or drive the slot's panels during a replay, or the result will show it.

### Debugger

Click **Open debugger** in the console (or go to `/debugger.html?slot=N`; add `&signal=NAME` to open it on a signal). It's laid out like the classic SIMPL Debugger:

- **Signal list** (left): every signal in the slot with its live value. Search, filter by type, and tick **T** to choose which signals go to the trace. Click a column header to sort.
- **Trace** (right): one row per change with time, signal, value and what caused it. **Stream** (F5) pauses and resumes it, **Clear** empties it, **Save…** writes it to a text file. You can trace all signals or only the ticked ones.
- **Display options**: time as absolute, relative or delta; analogs as decimal (`500d`), hex (`01F4h`) or percent; serials as ASCII with `\x0D`-style escapes, or hex.
- **Step-through** (for logic wave problems): turn on **⏸ Step** and the slot's logic pauses on every signal change, right after the new value is set and before anything wired to that signal reacts. **Next** (F10) lets that one change through and stops at the next; **Run** (F8) turns stepping off. Set **Break on** to *marked signals* and tick the **⏸** column (or press F9 on the selected signal) to pause only on the signals you care about; everything else runs through. The bar under the toolbar shows where it's paused, and that signal is highlighted in both lists. **Back** (F7) shows an earlier step: every signal's value as it was then (values that have changed since are dotted), with that change highlighted in both lists. The program itself stays paused where it is; logic can't be run backwards. **Next** walks forward through those steps to the live one, then carries on stepping the program. The history covers the steps this debugger window has seen. Forcing a signal while paused queues it until you step on. Stopping or unloading the slot releases the pause.
- **Forcing signals**: right-click a signal for Set / Clear / Pulse / Toggle (digital) or to enter a value, double-click a digital to toggle it, or use the **Force** bar at the bottom. Analog values can be typed as `500`, `1F4h` or `50%`, and serial values can include `\x0D`, `\r` or `\n`.
- **Zoning in**: rows glow when their signal changes; sort by *recently changed* or *most changes*, or show *active only*. Search takes several terms, `*` wildcards, `/regex/`, `d:` `a:` `s:` for type, `=1` / `>500` value tests and `-term` to exclude (the **?** next to the box lists them). Save sets of signals as **group tabs**, or switch to **tree** to see signals by device and module. In the trace, **follow** shows only the selected signal (or it and its neighbours), and **this burst only** shows one press and everything it caused.
- **Cause and effect**: each trace row says which symbol produced the change (**via**), and is indented by how many hops it is from the press, panel event or force that started it; rows from the same burst share a shade. Right-click a signal for **what drives this** / **what this drives**.
- **Waveform**: a logic-analyzer view of the ticked (**T**) signals under the panes. Wheel to zoom, drag to pan, click to set a marker and read Δt.
- **Paths** (`/paths.html?slot=N&signal=NAME`, or right-click → *Show paths from here*): a map of everything a signal can reach, one column per hop, with feedback loops drawn as magenta arcs. When the program runs, the route each change actually takes lights up and fades like a trail; signals that change again in the same burst get a ring and a count, and ones that oscillate turn red. Slow motion (¼×, 1/10×) and **Replay last burst** make fast bursts watchable. Double-click a digital to press it from the map. Works for SIMPL Windows and compiled programs (not SIMPL#). The API behind it is in [docs/debugger-paths.md](docs/debugger-paths.md).

- **Watches**: an expression over signals, such as `Room_On=1 && Proj_Power=0`, `Vol>50000` or `Src_Name contains "HDMI"`, with an optional `for 2s`. The server checks watches on every change, so they work with the debugger closed and are saved per slot (`debug.json` in the slot's folder). When one becomes true it appears in the **Watches** panel with its time, writes a Warning (with the values) to the slot log, and can pause stepping at that change. Digital signals are 1 or 0; analog values can be `500`, `1F4h` or `50%`; `contains`, `startswith` and `endswith` ignore case, `=` on text is exact; `and`, `or`, `not` and parentheses work; a signal on its own is true when it is high, non-zero or not empty; `[Name with spaces]` quotes a name. The parser is written by hand in `OpenVP.Core` (`SignalExpression`) and evaluates nothing as code; a malformed expression is refused with a message that says where.
- **Conditional breakpoints**: **Conditions** next to *Break on* takes the same expressions (`Vol > 500`, `Err contains "ERR"`). The program pauses at a change to a signal the expression reads when the expression is true at that step, and the paused bar says which condition stopped it.
- **Bookmarks and notes**: star signals (the ★ column, `S`; they are in the **★ Starred** tab) and add a note to any signal (`N`). They are saved on the server per *program name* (`data/bookmarks/`), so they survive reloads, a move to another slot and a restart. A note shows after the signal name and in its tooltip.
- **Traces**: **Traces** saves the shown trace under a name (up to 50 per slot, in `data/traces/`), exports it as CSV or JSON with every field (time, signal, kind, value, source, symbol, cause, depth, chain), imports such a file back, shows a saved trace in the Trace pane, and compares two saved traces side by side: signals are lined up by value (so one extra change doesn't shift the rest) and times are measured from each trace's first change; the report lists changes only one trace has, steps with a different value, and timing differences beyond a tolerance (50 ms by default). Typical use: before and after a program edit.
- **Waveform**: analog signals draw as a stepped line graph on a 0-65535 scale with a percent axis, and serial signals as markers labelled with the new text.
- **Why is it this value?** (right-click, or `Y`): walks back from a signal through the changes that caused it, using the program's wiring and the slot's recent trace (kept while a debugger window is open, the last 20,000 changes), to the input, force, timer or start-up behind it, and writes it out with times: *fb is 1 because fb went high at 10:02:03.120 (driven by AND S21), because mid went high ..., because press was pressed at 10:02:03.118 on IP ID 03 from panel 192.168.1.20*. Works for `.smw` and `.lpz` programs. For SIMPL# it says what is known: the join, when it was set, whether by the program or a device, and from which address.
- **Never changed**: lists signals that have not changed since the program started or since a **Reset**, to find logic nothing uses. It tracks only while switched on, so it costs nothing otherwise.
- **Alerts**: with **Detect races and oscillation** on, a signal that changes more than N times in one burst (default 6; SIMPL# programs have no bursts) or a digital signal that flips N times in M ms (default 20 in 1000 ms) writes a Warning to the slot log, shows a ⚠ badge on the debugger and on the slot's tile on the Status board, and is listed under **Alerts**. Thresholds are per slot. It is off by default, because it needs every change traced.

A slot pays for none of this unless it is switched on: with no watch, condition, detector, tracking or open debugger, `ProgramSlot.TraceWanted` stays false and the runtimes take the untraced path.

Each window shows only its own slot, so you can open one per slot and two people can debug two programs on the same processor at once. For SIMPL# slots the signals are device joins, named like `IPID03.out.d1` (panel press) and `IPID03.in.s5` (feedback).

### Log

The console's **Log** (and the same log on its own page, `/log.html?slot=N`) has a button per level (Error, Warning, Notice, Info) that shows how many lines there are and hides or shows them, a search box, colour by level, **Pause**, **Copy** and **Download** (the lines shown). Signal names, SIMPL# join names (`IPID03.out.d1`), IP IDs, slot numbers and file paths in a line are links: a signal opens that slot's debugger with the search set, the row selected and scrolled into view (a debugger window already open for the slot, named `openvp-debugger-N`, is told by `postMessage`; otherwise the page opens with `?signal=NAME`); an IP ID jumps to the IP table, a slot number selects the slot, `\USER\...` and `\NVRAM\...` paths show that folder in Files, and other paths are copied. The slot's last 1,000 lines are loaded; the page keeps 2,000.

**SIMPL# exceptions.** When a SIMPL# program throws and nothing catches it (in an event handler, a `CTimer`, a `CrestronThread` or `CrestronInvoke`), the log gets an Error with the exception's type, message and what was running, and an expandable entry holding the full stack trace. The slot also keeps the last 100 in an **Exceptions** list (under the log, and a tab on the full-page log) with the stack frames. Frames show file and line when the program's `.pdb` is found: OpenVP copies the `.pdb` along when loading a `.dll` (or a `.pdb` next to a `.cpz`; one inside the `.cpz` is extracted with it) and loads it with the assembly. It has to be a portable PDB, which is what `dotnet build` writes; the old Windows (full) PDB format can't be read, and the log says so.

**Support bundle.** **Support bundle…** on the Log section (or in the palette) downloads a zip of the slot's log (text and JSON), the recent trace if a debugger was open on the slot, the compatibility report, program summary, metrics snapshot, slot settings, activity for the slot, exceptions, watches and monitor settings, and the OpenVP version, OS and .NET. It never contains the access key, and leaves out the program binaries, NVRAM and User files unless you tick **Include the program files**.

**Find a signal in all slots.** The box in the top bar (`F`, and the command palette) finds signals by name across every loaded slot and opens that slot's debugger on the one you pick.

### Metrics

Click **Metrics ↗** in the console header (or go to `/metrics.html`) for a live view that refreshes every second:

- **CPU, memory, threads and thread pool** for the OpenVP process, each with a five-minute graph.
- **Per slot:** events per second, how busy the slot thread is, events waiting in its queue, what it's running right now, the longest single event so far, and how many timers, threads and background jobs the program has.
- **Problems** are listed at the top:
  - **Stuck:** the slot thread has been inside one event for 5 s or more. Nothing else in that slot can run until it returns. The event is named, for example `CTimer ControlSystem.OnPoll`.
  - **Orphaned thread:** a thread a program started (`CrestronThread`, SIMPL+ Function Main) is still running after that program was unloaded or replaced.
  - **Long-running background work:** a `CrestronInvoke` callback or SIMPL+ event handler has been running for 30 s or more.
  - **Backlog:** 1000 or more events are queued for a slot, or background work is waiting for thread-pool threads.

The same numbers are available from the slot console: `cpuload`, `ramfree`, `taskstat` (threads and what the slot thread is doing) and `metrics` (all of it). From scripts, use `GET /api/metrics` or the `/ws/metrics` WebSocket. The page's **History** and **Uptime** tabs are described in [Uptime and history](#uptime-and-history).

### Alerts

**Alerts ↗** in the console's top bar (or `/alerts.html`) turns events into notifications. A badge on the link counts what is wrong now. The engine is in `OpenVP.Core.Alerts` (`AlertEngine`) with unit tests; sending happens on other threads, each channel with its own bounded queue (100), so a slow mail server never holds up a program or another channel.

**Events** it raises alerts for:

| Kind | When | Severity |
|---|---|---|
| `slot.fault` | A slot goes to Error (a fault, or a program that failed to start); clears when the slot leaves Error | Critical |
| `slot.stuck` | The slot thread has been inside one event for 5 s or more (the Metrics page's "Stuck") | Critical |
| `slot.backlog` | 1000 or more events queued for a slot; clears below 500 | Warning |
| `crash.restart` / `crash.giveup` | The [crash policy](#autostart-and-crash-policy) restarted a program, or left it stopped (clears when it runs again) | Warning / Critical |
| `device.offline` | A panel or device that was online has been offline for more than N seconds (default 60) in a running slot; clears when it comes back | Warning |
| `oscillation` | The [oscillation detector](#debugger) fired | Warning |
| `watch` | A [watch expression](#debugger) became true | Warning |
| `restore.failed` | A program couldn't be brought back at start-up | Critical |
| `tunnel.stopped` | The internet tunnel stopped by itself (not when you turn it off) | Warning |
| `disk.low` | Less than 1000 MB (settable) free on the drive of the data folder; Critical below a quarter of that | Warning |
| `threshold` | A [threshold](#thresholds) was crossed | Warning / Critical |
| `backup.failed` | A [backup](#backups-and-export) couldn't be written; clears with the next good backup | Critical |
| `schedule.failed` | A [scheduled action](#schedules) failed; clears with that schedule's next good run | Warning |

**Channels.** Add as many as you like, each with a **Test** button that sends one message, once, and shows what happened:

- **Email (SMTP):** server, port, security (STARTTLS, implicit TLS or none), optional user name and password, from, and one or more recipients. Each message has a plain-text and an HTML part. Sent with [MailKit](https://github.com/jstedfast/MailKit) 4.13.0 (MIT; it brings MimeKit and BouncyCastle.Cryptography, both MIT).
- **Microsoft Teams:** an incoming webhook address (a Workflows "Post to a channel when a webhook request is received" webhook, or a classic Incoming Webhook connector). It gets an Adaptive Card: a coloured title bar, the message, a table of facts and an "Open console" button.
- **Slack:** an incoming webhook address. It gets Block Kit blocks (header, message, fields, context, button) in an attachment with a colour bar.
- **ntfy:** a server (default `https://ntfy.sh`), a topic and an optional access token. Published as JSON with a priority (critical is 5), emoji tags and a click link.
- **Generic webhook:** a JSON `POST` with `event` (`alert.raised`, `alert.reminder`, `alert.resolved`, `alert.test` or `report`), `kind`, `severity`, `slot`, `room`, `program`, `title`, `message`, `count` and `facts`, signed exactly like [outgoing webhooks](#webhooks) (`X-OpenVP-Timestamp`, `X-OpenVP-Signature`). It uses the same sender, so retries behave the same: a request that gets no answer in 15 s, fails to connect, or is answered 5xx, 408 or 429 is tried again after 1 s, 5 s and 30 s.
- **Desktop:** the stream behind desktop and browser notifications (below). One exists out of the box.

Passwords, tokens, signing secrets and the Teams and Slack addresses (the address is the credential) are kept in `data/integrations/secrets.json` like the MQTT password (DPAPI on Windows, an owner-only file elsewhere) and never sent back by the API or page; the settings are in `data/alerts/config.json`.

**Rules** say which alerts go to which channels. A rule lists alert kinds and slots (empty means all), a minimum severity, its channels, optional **quiet hours** (local time, may wrap midnight; Critical alerts can still go out), its own repeat time, and whether to send a message when the alert clears. An alert goes once to every channel named by every rule it matches. A new install has one rule: Warning and worse to the desktop.

**De-duplication and rate limiting.** The same alert (the same key, such as `slot.stuck:3`) is sent at most once per **15 minutes** per rule (changeable globally and per rule; 0 sends every time). This also holds across an alert that clears and comes back, so a flapping signal can't flood a channel. An alert that gets worse (Warning to Critical) is sent at once. One-off events (a watch firing) inside the window are grouped into one history entry with a count. When an alert clears, a **resolved** message goes to the channels that heard about it, and only to those.

**History and acknowledge/mute.** The Alerts tab lists what is happening now and the last 500 alerts (`data/alerts/state.json`), each with every channel's delivery status: `sent`, `failed` (with the reason and number of tries), `queued`, or `suppressed` (with why: repeat limit, quiet hours, muted, acknowledged). **Acknowledge** stops repeat messages for an alert that is going on (the resolved message still goes and the tray stops counting it). **Mute…** silences that alert and later ones with the same key for 1 hour to 7 days, or until unmuted. What was active when OpenVP stopped starts the next run as ended; the sources check again.

**Desktop notifications.** The server streams alerts at `GET /api/alerts/stream` (server-sent events: `status` with the counts and colour, then an `alert` event for each raise, resolve and test; a keep-alive comment every 15 s). In the **desktop app** the main process listens (`desktop/alerts-stream.js`, so it works even when the window is closed to the tray and nothing is added to `preload.js`) and shows native notifications (Critical ones stay until dismissed where the OS allows; clicking one opens the Alerts page). A **tray icon** shows the status colour (green all clear, amber warnings, red critical, grey when the server can't be reached) with a menu: Open console, Alerts, Metrics, Show desktop notifications, **Keep running in the tray when the window is closed** (off by default; otherwise closing the window quits as before) and Quit. These two settings are kept in `desktop.json` in the app's data folder. In a **browser**, tick *Show browser notifications* on the Alerts tab to use the Web Notifications API: the browser asks permission once, and notifications show only while a console page is open in that browser.

### Uptime and history

OpenVP records state changes in `data/history/uptime-yyyyMMdd.jsonl` (one file per UTC day, one JSON line per event, each day starting with a checkpoint of every state, 90 days kept; a heartbeat each minute shows OpenVP was running). It records each slot's state, each device's online/offline, and OpenVP starting and stopping. The **Uptime** tab of the Metrics page (`/metrics.html#uptime`) shows availability for 24 hours, 7, 30 or 90 days with a timeline bar for each slot and device (point at a segment for its time and percentage), the time down and the number of drops.

- A slot is **up** while Running and **down** while it holds a program that isn't running (stopped, faulted, or OpenVP itself not running, from the last heartbeat to the next start). Empty slots aren't counted.
- A panel or device is **up** while connected and **down** while registered and not connected, only while its slot is running. A device that has never connected since OpenVP started isn't counted until it first does.
- Time before OpenVP began recording isn't counted; the page says since when it has data.

**Offline heat map** (same tab): a row for each device that dropped and a column for each hour of the day (all days of the range added up) or each day, in your browser's time zone; the cell colour is the minutes offline, and hovering gives the device, the hour or day, the minutes and the drops. That shows which panels drop and when (for example every day around 14:00).

**Metrics history** (the **History** tab, `/metrics.html#history`): the live once-a-second path is unchanged. A second sampler also keeps the last 5 minutes second by second in memory, **1-minute averages for 7 days** and **15-minute averages for 90 days** on disk as compact JSON lines (`data/history/metrics-1m-yyyyMMdd.jsonl`, `metrics-15m-yyyyMM.jsonl`, UTC). No database is used (a plain-file ring was enough, so there is no SQLite package). Each point holds the average and the highest value, so a short spike isn't averaged away (shown as the faint line). A range picker (5 min, 1 h, 24 h, 7 d, 30 d, 90 d) drives graphs of CPU, memory, slot busy percent, queue length, events per second, longest event and devices offline, with a slot picker for the per-slot graphs, a read-out when you point at a graph, and your [threshold](#thresholds) levels as dashed lines. Ranges up to 7 days use the 1-minute points, longer ones the 15-minute points, and more than 600 points are merged into wider steps. Files older than the retention are deleted. All of this (alerts, history, thresholds, reports) is switched off with `OpenVP:Alerts=false`.

### Thresholds

**Thresholds** (Alerts tab) raise an alert when a number stays at or above a **warning** or **critical** level for N seconds, and end it when the number has stayed below for 5 seconds (so a value hovering at the line doesn't flap). Measures: process CPU %, memory MB, slot thread busy % (each slot, or one), events waiting for a slot, longest event running now (ms; the event the slot thread is in at this moment, so a handler that never returns keeps climbing), events per second, and devices offline (all, or one slot's). Each has a warning and a critical level (either may be left empty). A threshold alert is kind `threshold` and goes out through your rules, with the usual de-duplication and a resolved message. The levels are drawn on the History graphs.

### Reports

A **health report** covers the last 24 hours (daily) or 7 days (weekly) and says, per slot: uptime %, crashes and restarts (from the Activity feed), alerts raised; then the most frequent events, devices that dropped (drops and time offline), peak CPU and memory, and programs loaded or changed (from the Activity feed). On the **Reports** tab choose daily or weekly, the time (this computer's time) and the channels. It goes out once per scheduled time; one missed while OpenVP was off goes out when it starts; turning it on doesn't send for a time already past. Email gets the report as a self-contained HTML page; Teams, Slack, ntfy and webhooks get a short summary (a webhook also gets the HTML). **Send report now** sends it through the ticked channels, **View report** opens the HTML page and **Download HTML** saves it. Sent reports appear in the alert history.

### Integrations

**Integrations ↗** in the console's top bar (or `/integrations.html`) connects slots to other systems. It has a tab for each of MQTT, Webhooks, OSC, Art-Net and Prometheus, each showing its status (connected or error, when the last message went, counts) and a form to set it up. Signals are chosen from a picker that lists what the slot's loaded program has, the same list the debugger uses. Connection settings (broker, ports) are for all of OpenVP and are saved in `data/integrations/global.json`; which signals a slot uses, and its outgoing webhooks, are saved with the slot in `data/programNN/devices/integrations.json`, so snapshots and roll backs include them. Each change is written to the [Activity](#console-features) feed.

None of these can slow a program down. A slot listens for changes only to the signals you chose (a slot with no integrations is not traced at all), the listener only copies the change into a bounded queue, and the network work happens on other threads. If a broker, a webhook endpoint or an OSC target can't keep up, the oldest changes are dropped and counted, and the program carries on.

#### MQTT bridge

Connects to a broker (host, port, TLS, user name and password, client id, base topic) and publishes the signals you choose. The client is [MQTTnet](https://github.com/dotnet/MQTTnet) 4.3.7.1207 (MIT, no other dependencies).

| Topic | |
|---|---|
| `<base>/status` | `online`, or `offline` (the broker publishes it if OpenVP disappears; OpenVP publishes it itself when it stops cleanly). Retained |
| `<base>/slotNN/state` | `running`, `stopped`, `loaded`, `empty` or `error`. Retained |
| `<base>/slotNN/<signal>` | The signal's value, published whenever it changes (and again after a reconnect or when the program starts). Retained. Digital is `1` or `0`, analog a number 0-65535, serial the text |
| `<base>/slotNN/<signal>/set` | Write here to set the signal (only for signals with **Set** ticked) |

The base topic defaults to `openvp/<computer name>`. In a topic a signal's name keeps letters, digits and `_ . -`; every other character becomes `_` (and a clash gets `_2`, `_3`). What `/set` accepts: a digital signal `1`, `0`, `true`, `false`, `on`, `off` or `pulse` (high then low); an analog signal a number (clamped to 0-65535) or a percentage like `50%`; a serial signal any text. Retained messages on a `/set` topic are ignored, so a stale command isn't replayed at every reconnect.

```bash
mosquitto_sub -h broker -t 'openvp/mypc/#' -v
mosquitto_pub -h broker -t 'openvp/mypc/slot01/Room_On/set' -m 1
mosquitto_pub -h broker -t 'openvp/mypc/slot01/Volume/set' -m '50%'
```

**Home Assistant.** Turn on *Home Assistant discovery* and tick **Home Assistant** on a signal, and OpenVP publishes a retained discovery message to `homeassistant/<component>/openvp_<host>/slotNN_<signal>/config`: a **switch** for a digital signal (a binary sensor if it can't be set), a **number** (0-65535) for an analog one (a sensor if it can't be set) and a **sensor** with the text for a serial one. They are grouped under one device per slot and use `<base>/status` for availability. Un-ticking a signal removes its entity.

The password is kept apart from the other settings, in `data/integrations/secrets.json`: encrypted with Windows DPAPI for your user on Windows, and elsewhere stored as it is in a file readable only by its owner (like `apikey.txt`). The API and the page never send it back; a settings request that leaves `password` out keeps it.

#### Webhooks

*Outgoing.* A webhook fires when its signal changes, or when its condition becomes true. A condition uses the watch language of [debugger watches](#debugger) (`Room_On=1 && Proj_Power=0 for 2s`) and fires once each time it turns true. OpenVP sends `POST` with a JSON body:

```json
{"event":"signal.change","time":"2025-03-04T10:15:30.1234567+00:00","slot":1,"room":"SLOT01","program":"Boardroom","webhook":"Lights",
 "signal":"Room_On","kind":"digital","value":true}
```

A condition sends `"event":"expression.true"`, `"expression"` and `"values"` (the signals it reads) instead. Headers you add are sent too. With a signing secret, each request has `X-OpenVP-Timestamp` (Unix seconds) and `X-OpenVP-Signature: sha256=<hex>`, the HMAC-SHA256 of `timestamp + "." + body` with your secret as the key, so the receiver can check who sent it and reject old requests. `X-OpenVP-Event` and `X-OpenVP-Delivery` (a running number) are sent as well. A request that gets no answer in 10 s, fails to connect, or is answered 5xx, 408 or 429 is tried again after 1 s, 5 s and 30 s; any other answer is final. Each webhook has its own queue of 200, so a dead endpoint only delays itself. The **Delivery log** (and `GET /api/webhooks/deliveries`) lists the last 500 deliveries with result, tries and time, and **Test** sends a sample event once.

*Incoming.* `POST /hooks/<token>` sets a signal in one slot:

```bash
curl -X POST http://host:8080/hooks/TOKEN -d '{"signal":"Room_On","value":1}'
curl -X POST http://host:8080/hooks/TOKEN -d '{"pulse":"Doorbell"}'
```

`value` is a number, `true`/`false` or text, read like MQTT's `/set`. Make a hook in the page (or `POST /api/hooks`); it has a label, a slot, and optionally a list of signals it may set (the rest get 403). The token is 192 random bits. It works **without the access key**, because the token is the credential, so treat the address like a password. It is for one slot only (a `slot` in the body is ignored), can be switched off or revoked (it stops working at once), and is **not available through the internet tunnel**. Limits: 20 requests per second per token (a burst of 40) answered `429` with `Retry-After`, a 4 KB body, and 10 wrong tokens a minute per address before that address gets `429` for the rest of the minute. Hooks are kept in `data/integrations/hooks.json` (owner-only, and not in snapshots, so rolling back never brings a revoked token back).

#### REST per signal

For scripts, one URL per signal (same access key as the rest of the API; the name must be URL-encoded):

```bash
KEY=$(cat data/apikey.txt)
curl -H "Authorization: Bearer $KEY" http://host:8080/api/slots/1/signals/Room_On
# {"slot":1,"name":"Room_On","kind":"digital","value":true}
curl -X PUT  -H "Authorization: Bearer $KEY" http://host:8080/api/slots/1/signals/Volume -d '50%'
curl -X PUT  -H "Authorization: Bearer $KEY" http://host:8080/api/slots/1/signals/Source_Name -d '"HDMI 1"'
curl -X PUT  -H "Authorization: Bearer $KEY" http://host:8080/api/slots/1/signals/Room_On -d '{"value":1}'
curl -X POST -H "Authorization: Bearer $KEY" http://host:8080/api/slots/1/signals/Doorbell/pulse
curl -N      -H "Authorization: Bearer $KEY" http://host:8080/api/slots/1/signals/Volume/stream
```

`PUT` takes `1`, `true`, `"text"`, `{"value": ...}`, or plain text such as `50%` and `pulse`. The stream is [server-sent events](https://html.spec.whatwg.org/multipage/server-sent-events.html): first an `event: value` with the current value, then an `event: change` for each change, each `data:` a JSON `{"slot","name","kind","value","time"}`, with a `: keepalive` comment every 15 s. At most 100 streams are open at once.

#### OSC

Open Sound Control over UDP. In **Listen on UDP port** (0 is off, the default) OpenVP receives `/openvp/slotNN/<signal> <value>` and sets the signals that slot ticks **Receive** (nothing else can be set from OSC). A digital signal takes an int (non-zero is high), a float (above 0.5 is high), `T` or `F`, or no value at all or `/pulse` on the end of the address to pulse it. An analog signal takes an int 0-65535, or a float from 0 to 1 as a fraction of full scale (so TouchOSC faders work). A serial signal takes a string. In **Send to** (host and port) each change of a signal ticked **Send** goes out as one message to the same address: digital and analog as an int, serial as a string. The encoder and decoder (`OscCodec`, in `OpenVP.Core`) follow the OSC 1.0 specification, including bundles, and are tested against its example bytes. OSC has no authentication: only listen on a network you trust.

#### Art-Net

Maps analog signals to DMX channels. Each mapping has a universe (0-32767, the 15-bit port address), a channel (1-512) and a width: **8-bit** scales 0-65535 to 0-255, **16-bit** puts the high byte on the channel and the low byte on the next. A digital signal is 0 or 255. OpenVP sends one ArtDMX packet per universe in use at **Frames per second** (1-44, default 30), steadily, whether or not anything changed, to a node's address or a broadcast address. With **Also receive ArtDMX**, OpenVP listens on UDP 6454 and sets signals whose mapping ticks **Receive** when their channel changes (a byte `b` is `b * 257`). `ArtNetCodec` follows the Art-Net 4 ArtDMX layout and is tested against its bytes. Art-Net has no authentication either.

#### Prometheus and Grafana

`GET /metrics` returns the [Prometheus text format](https://prometheus.io/docs/instrumenting/exposition_formats/). It needs the access key (`Authorization: Bearer KEY`, or no key from this computer), unless you set `OpenVP:MetricsPublic=true`; it is **never** served on the internet tunnel's port. It has:

| Metric | |
|---|---|
| `openvp_process_cpu_percent`, `_resident_memory_bytes`, `_private_memory_bytes`, `_managed_heap_bytes`, `_threads`; `openvp_threadpool_*`; `openvp_gc_collections_total{generation}`; `openvp_uptime_seconds`; `openvp_info{version}` | The OpenVP process |
| `openvp_slot_info{slot,room,name,program,kind}`, `openvp_slot_state{slot,state}`, `openvp_slot_running{slot}` | What each slot holds and whether it runs |
| `openvp_slot_events_per_second`, `_events_total`, `_busy_percent`, `_queue_depth`, `_longest_event_seconds`, `_current_event_seconds`, `_timers`, `_stuck`, `_warnings`, `_alerts` (all `{slot}`) | How busy a slot is, and its problems |
| `openvp_slot_devices`, `_devices_online`, `_panel_connections` (`{slot}`); `openvp_devices_online`, `openvp_panel_connections` | Devices and panel connections |
| `openvp_slot_crashes_total`, `openvp_slot_restarts_total` (`{slot}`); `openvp_alerts` | Crash policy activity and alerts |
| `openvp_mqtt_connected`, `openvp_mqtt_messages_{published,received,dropped}_total`; `openvp_webhook_deliveries_total{result}`, `_dropped_total`, `_pending`; `openvp_osc_messages_{received,sent}_total`; `openvp_artnet_packets_{sent,received}_total` | The integrations |

To scrape it, add to `prometheus.yml`:

```yaml
scrape_configs:
  - job_name: openvp
    scrape_interval: 15s
    metrics_path: /metrics
    static_configs:
      - targets: ["openvp-host:8080"]
    authorization:
      credentials_file: /etc/prometheus/openvp.key   # the access key; leave out with OpenVP:MetricsPublic=true
```

The Grafana dashboard is [`tools/grafana/openvp-dashboard.json`](tools/grafana/openvp-dashboard.json): in Grafana choose Dashboards, New, Import, upload it and pick your Prometheus data source. It shows slots running, devices online, alerts, CPU and memory, a table of slots, per-slot events per second, busy percent, queue depth, devices, crashes and restarts, and the MQTT, webhook, OSC and Art-Net counters, with `job`, `instance` and `slot` variables.

### Telnet and SSH console

A text console that behaves like a processor's, reachable over **telnet (port 41795)** and **SSH (port 22)**. Both are **off by default**. Turn them on with `OpenVP:TelnetEnabled=true` and `OpenVP:SshEnabled=true` (ports `OpenVP:TelnetPort`, default 41795, and `OpenVP:SshPort`, default 22; on Linux and macOS a port below 1024 needs privileges, so the log says so if it can't listen). **When enabled a listener binds to all interfaces and the log says so** (`Telnet console listening on all interfaces, port 41795`).

```
telnet host 41795        # Password: the OpenVP access key
ssh admin@host           # any user name; the password is the access key
ssh admin@host ver       # one command, no shell
```

- **Login.** The password is the OpenVP [access key](#security); the user name is ignored for now. The authenticator is a small interface (`IConsoleAuth`), so a user from a later accounts feature can be checked the same way, with a user name prompt. With the key switched off (`OpenVP:ApiKey=off`) the console doesn't start at all: it won't be open to anyone. SSH takes password logins only (no keys, no port forwarding, no subsystems).
- **Commands.** `help`, `ver`, `info`, `hostname`, `uptime`, `time`, `slots`, `ipt [-p:N]`, `progreset -p:N` (load the slot's program again and start it), `progload -p:N [-f:file]` (the slot's remembered program, or a file in its upload folder), `stopprog -p:N`, `startprog -p:N`, `cpuload`, `ramfree`, `taskstat` (`-p:N` for a slot), `progcomments -p:N`, `signals -p:N`, `use N`, `prog N command` and `exit`. Anything else is tried as a console command the program registered (`CrestronConsole.AddNewConsoleCommand`, `RunConsoleCommand`, as in the web console), in the slot named with `-p:N`, the one chosen with `use N`, or the first slot that has a command of that name. `help` lists the program's own commands too.
- **Limits.** At most 5 sessions at a time (`OpenVP:ConsoleMaxConnections`) and 3 from one address; idle sessions close after 15 minutes. **Wrong passwords are limited**: each wrong one waits 1.5 s, 3 wrong end the session, 5 wrong from one address in 5 minutes lock that address out for 5 minutes (even for the right password), and 30 from all addresses together lock everyone out for 5 minutes.
- **SSH library.** [FxSsh](https://github.com/Aimeast/FxSsh) (MIT, no dependencies) from NuGet. Its host key is made on first start and kept in `data/ssh_host_key.pem` (owner-only on Linux and macOS); keep the file so clients don't see host key warnings. Telnet is plain text and so are the passwords typed into it: use it on a trusted network, or use SSH.

### Discovery

**Discover** (Devices ↗, **Discover** tab; `POST /api/discovery`) scans the computer's own subnets for devices, **only when you click it**, gently: at most 48 connections at a time, half a second each, 30 seconds overall, and a /24 at most. It uses:

- **Crestron autodiscovery**: the UDP 41794 broadcast the IP table already uses (`ClientIdentifier`), which gives hostname, model, firmware and MAC from Crestron hardware.
- **mDNS / Bonjour**: a browse for common AV and network services (`_http._tcp`, `_airplay._tcp`, `_googlecast._tcp`, `_raop._tcp`, `_pjlink._tcp`, `_ndi._tcp`, Dante `_netaudio-*`, `_ssh._tcp`, ...), which gives names and services.
- **Ping and the ARP table**, then a quick **TCP connect** to the control ports 23, 80, 443, 41794, 41795, 1515 and 4352 (PJLink) on the hosts that answered.

Each result shows address, name, model and firmware (from Crestron replies), MAC (when known), open ports and how it was found. **Add as simulator target** makes a [network redirect](#network-redirects) from the device's address to a simulator (an existing one, or a new TCP simulator from a profile); **Use in network redirect** opens the redirect box with the address filled in. This computer is left out. A scan on a network with nothing to find ends in a few seconds. Where ping isn't allowed or multicast is blocked, the scan says so and uses what it can.

### openvp.local (mDNS)

OpenVP advertises itself over mDNS so phones and PCs on the LAN can find it: `http://openvp.local:8080` and `http://<hostname>.local:8080` resolve to this computer, and a service browser lists **"OpenVP on <hostname>"** as an `_http._tcp` service on the console's port. It is a small responder written for this one job (no extra package; the ready-made mDNS libraries on NuGet are old and pull in several dependencies). `OpenVP:Mdns` turns it on or off: the default is **on** when running from source or in the desktop app and **off inside a container** unless the container uses host networking (set `OpenVP__Mdns=true` with `--network host`; on a bridge network multicast doesn't reach the LAN). `OpenVP:MdnsName` changes `openvp` to another name. If multicast isn't available (no network, a sandbox, blocked by the firewall) it logs `mDNS is off: ...` and carries on; OpenVP is still reachable by IP address. It doesn't check whether another machine already uses the name `openvp.local`; use the host name if two OpenVP computers share a network. Windows may need the firewall to allow UDP 5353 the first time.

## Compatibility

Every time a program loads, anything in it OpenVP doesn't support yet goes into **[`docs/unsupported.md`](docs/unsupported.md)**: SIMPL symbols, sub-devices and hardware, compiled primitive types, SIMPL+ modules that can't run, and SIMPL# types, members and assemblies. Each row counts how many programs needed it, so the top of each kind is what to build next. Only symbol names are written, never program names.

- **Running from a source checkout** (`dotnet run`, `Start OpenVP.bat`): the server writes straight to `docs/unsupported.md` in the repo. Run your programs, then commit and push the file so everyone gets the list.
- **Running `OpenVP.exe` or Docker**: it's `unsupported.md` in the data folder (`%APPDATA%\OpenVP` for the desktop app), also downloadable from `GET /api/unsupported`. Paste its table rows at the end of `docs/unsupported.md` in the repo (or replace the file if the repo's is empty) and commit; the next run from source merges duplicate rows and re-sorts the table.
- **When you implement something**, delete its row. If a program still needs it, it comes back on the next run.

The per-slot view is still there: the log lists what each program is missing, and **Compatibility** in the console (`GET /api/slots/{n}/compat`) shows it for the loaded program.

## Security

A SIMPL# program is .NET code that runs inside the OpenVP process, so anyone who can upload a program can run code on the machine. The web console and API are protected by an **access key**:

- If `OpenVP:ApiKey` isn't set, a random key is generated on first start, saved in `data/apikey.txt` (readable only by its owner) and printed to the console.
- In a browser, open `http://host:8080/?key=KEY` once, or type the key into the login box. You'll get an HttpOnly, SameSite=Strict cookie.
- From scripts, send `Authorization: Bearer KEY` or `X-OpenVP-Key: KEY`.
- Requests from the same machine to `localhost` don't need the key, unless `OpenVP:LocalRequiresKey=true`. This exception never applies to requests that arrive through a reverse proxy (with `X-Forwarded-For`, `Forwarded`, `X-Real-IP`, `Cf-Connecting-IP`, `Cf-Ray` or `CF-Visitor` headers), to anything that arrives on the internet tunnel's port, or to a host name other than localhost, which also stops DNS-rebinding attacks.
- Requests from a web page on another site are refused (403) even when the browser has the cookie, and even from localhost. So a malicious page open in your browser can't upload a program.
- Panels (`/ws/panel` and CIP on 41794) don't need the key, because they can only press joins, like panels on a real processor. Set `OpenVP:PanelsRequireKey=true` to protect `/ws/panel` too. CIP has no way to send a key, so keep port 41794 on a trusted network.
- **Shared panel links** (`/s/{token}`) don't need the key either: the secret token in the link is the credential. A link is for one slot and IP ID and can only press joins there. Treat it like a password: anyone who has it (and the PIN, if there is one) can use that panel until it expires or you revoke it. Links, their tokens and hashed PINs are stored in `data/shares.json`, owner-only on Linux and macOS. Pages under `/s/` are sent with `Referrer-Policy: no-referrer` so the link doesn't leak to other sites. A CH5 project you upload is your own code running in the link holder's browser, on the same address as the console, so only upload projects you trust.
- **Everything new is behind the key**: simulators, the COM terminal, sockets, redirects, the IP ID remap and discovery are all under `/api`. Simulators listen on 127.0.0.1 only. The telnet and SSH consoles are **off by default**; when enabled they listen on all interfaces, need the key as the password, cap sessions and limit wrong passwords (see [Telnet and SSH console](#telnet-and-ssh-console)), and don't start if the key is off. Discovery sends packets to your network only when someone clicks Discover. mDNS announces the computer's name and the console port on the LAN, nothing else.
- **Incoming webhooks (`POST /hooks/{token}`) are the one other open endpoint.** They need no access key: the token in the address is the credential (192 random bits, one per hook, scoped to one slot and optionally to a list of its signals, revocable, rate limited, and wrong guesses are limited per address). They are not served on the internet tunnel's port. See [Webhooks](#webhooks). `GET /metrics` needs the access key unless `OpenVP:MetricsPublic=true`, and is never on the tunnel port either. The MQTT password is stored apart (DPAPI on Windows, an owner-only file elsewhere) and is never returned by the API. OSC and Art-Net listeners have no authentication and are off by default; each only sets signals you have ticked **Receive** for.
- `OpenVP:ApiKey=off` turns the key off. Only do that on a network you trust completely.

- **Exported secrets.** A [processor export](#backups-and-export) leaves out the access key, every saved password and signing secret, share links and push-target keys unless you tick *Include secrets*, and then they are only in `secrets.enc`, encrypted (AES-256-GCM, PBKDF2-SHA256 210,000 iterations) with your export password. Whoever has the file and the password has your SMTP and MQTT passwords and can use your share links, so treat both like keys, and use a long password. The access key is never exported. Backups with secrets keep their password in the secret store (DPAPI on Windows, an owner-only file elsewhere). The same applies to anything you commit to a [git repository](#git-backed-config), which never gets the secrets, but gets your programs and your NVRAM and User text files: don't push that repository anywhere you wouldn't put the programs. Importing a file replaces slots and can replace settings, so only import files you trust; it refuses names that would write outside the data folder and archives that unpack to too much.
- **Push targets.** A [target's](#push-to-other-machines) access key lets this OpenVP load programs (which are .NET code) onto the other machine, so it is kept in the secret store and the target list is behind the access key. An `http` target sends that key and the programs unencrypted: use `https`, and for a self-signed certificate pin its fingerprint, which then accepts only that certificate. Scheduled actions that load a program from a path can only be created from the OpenVP computer itself, like loading from a path.
- **`/healthz`** answers `ok` to anyone who can reach the console port and tells them nothing else. It is not served on the internet tunnel's port.
- **Internet access (tunnel)** exposes only shared panels, through a separate listener that serves nothing but `/s/{token}…` (not `/hooks`, not `/metrics`); see [Internet access (tunnel)](#internet-access-tunnel). The console, API and debugger are never reachable through it.

Traffic is plain HTTP. To use OpenVP across an untrusted network, put it behind a reverse proxy that adds HTTPS. Through the internet tunnel, Cloudflare provides HTTPS between the visitor and Cloudflare.

## Deploying with Docker Compose and Helm

**Docker Compose** (`deploy/docker-compose.yml`):

```bash
cd deploy
cp .env.example .env              # set OPENVP_API_KEY (or leave it empty: the key is made on first start and printed in the log)
docker compose up -d --build
docker compose logs openvp        # the access key, if you didn't set one
```

It builds the repository's `Dockerfile` and publishes ports **8080** (web console and REST API) and **41794** (CIP; experimental). The telnet (41795) and SSH (22) ports are in the file, commented out, because those consoles are off unless you set `OpenVP__TelnetEnabled` / `OpenVP__SshEnabled`. Data is the `openvp-data` volume at `/app/data` (programs, NVRAM and User files, settings, `data/backups`, the access key). It has `restart: unless-stopped` and a **healthcheck**: `dotnet openvp.dll --healthcheck` asks the server's own port for `/healthz` (an unauthenticated `ok`) and exits 0 or 1, because the image has no `curl`. `OPENVP_API_KEY` goes to `OpenVP__ApiKey`; `TZ` sets the time zone for [schedules](#schedules) and daily backups. A commented `network_mode: host` option is there for **mDNS (`openvp.local`) and device discovery**, which a bridge network can't do (Linux only; remove `ports:` and set `OpenVP__Mdns` to `true`). `docker compose config` checks the file. The image includes `git` for [Git-backed config](#git-backed-config) (`--build-arg WITH_GIT=false` leaves it out) and the `samples/` and `templates/` folders, which the project file copies into the publish output, so **Try the demo** and **Templates** work in the container.

**Helm** (`deploy/helm/openvp`; `helm lint` and `helm template` are clean):

```bash
docker build -t registry.example.com/openvp:0.1.0 . && docker push registry.example.com/openvp:0.1.0
helm install openvp ./deploy/helm/openvp \
  --set image.repository=registry.example.com/openvp --set image.tag=0.1.0 \
  --set apiKey="$(openssl rand -base64 24)"
kubectl port-forward svc/openvp-openvp 8080:8080     # then open http://localhost:8080
```

- **Deployment** with one replica and the `Recreate` strategy (one pod owns the data), a **PersistentVolumeClaim** for `/app/data` (`persistence.*`; kept on `helm uninstall` unless `keepOnUninstall: false`), liveness, readiness and startup **probes on `/api/info`**, resources, and every OpenVP setting you are likely to need as a value (`openvp.slots`, `localRequiresKey`, `metricsPublic`, `timeZone`, ...; anything else through `extraEnv`).
- **Service** (`ClusterIP`, port 8080) for the console. A second **Service for CIP**, off by default (`devices.enabled`), is a **LoadBalancer** (or NodePort) on TCP 41794 for real touch panels, with the telnet and SSH ports added when you enable those consoles.
- An optional **Ingress** (`ingress.enabled`, with class, annotations, hosts and TLS) for the web console. It carries WebSockets (the debugger, panels), so set long proxy timeouts and a large body size for uploads; the values file has the nginx annotations.
- A **Secret** for the access key: from `apiKey`, from your own `existingSecret`, or a random one made the first time and kept on upgrades. `values.yaml` documents every value.

`/healthz` is on the console port and needs no key; the Helm probes use the older `/api/info`, which also needs none.

## Configuration

`src/OpenVP.Server/appsettings.json`, or command-line overrides like `--OpenVP:CipPort=41795`:

| Setting | Default | |
|---|---|---|
| `OpenVP:DataDirectory` | `data` | Uploads and per-slot program folders |
| `OpenVP:Slots` | `10` | Number of program slots |
| `OpenVP:SlotSpinMicroseconds` | `0` | After a slot's queue runs dry, its thread keeps polling for this many µs (max 10,000) before it sleeps, which skips the ~60 µs wake-up for the next event. Costs CPU while idle (see `docs/performance-plan.md`); leave at 0 unless a device streams tight request/response traffic |
| `OpenVP:CipEnabled` / `CipPort` | `true` / `41794` | CIP listener |
| `OpenVP:CipLogPackets` | `false` (code default); `true` in the shipped `appsettings.json` | Hex-log CIP traffic. The shipped file turns it on while CIP is being validated against real panels; set it to `false` to stop |
| `OpenVP:ApiKey` | (generated) | Access key for the console and API; `off` disables it. See [Security](#security) |
| `OpenVP:LocalRequiresKey` | `false` | Require the key from localhost too |
| `OpenVP:PanelsRequireKey` | `false` | Require the key for browser panels (`/ws/panel`) |
| `OpenVP:UnsupportedLog` | `docs/unsupported.md` in a source checkout, else `unsupported.md` in the data folder | Where the list of unsupported symbols is kept (see [Compatibility](#compatibility)); `off` keeps it in memory only |
| `OpenVP:MetricsPublic` | `false` | `true` lets `GET /metrics` (Prometheus) be read without the access key. It is never served on the tunnel port. See [Prometheus and Grafana](#prometheus-and-grafana) |
| `OpenVP:TunnelPort` | `8085` | Port of the second listener (on 127.0.0.1 only) that the internet tunnel points at. `0` switches Internet access off. Must differ from the console's port |
| `OpenVP:CloudflaredPath` | | Path to `cloudflared`. If empty, OpenVP looks on the PATH, then in `data/tools` |
| `OpenVP:TunnelAutoStart` | `false` | Start the tunnel when OpenVP starts (only if `cloudflared` is already installed; it never downloads by itself) |
| `OpenVP:TelnetEnabled` / `TelnetPort` | `false` / `41795` | Telnet console (see [Telnet and SSH console](#telnet-and-ssh-console)). Off by default; when on it listens on all interfaces and logins need the access key |
| `OpenVP:SshEnabled` / `SshPort` | `false` / `22` | SSH console. Off by default; same rules. Host key in `data/ssh_host_key.pem` |
| `OpenVP:ConsoleMaxConnections` | `5` | Telnet and SSH sessions open at once (3 per address) |
| `OpenVP:Mdns` | on, except in a container | Advertise `openvp.local` and an `_http._tcp` service over mDNS (see [openvp.local](#openvplocal-mdns)). In Docker it is off unless you set it (use host networking) |
| `OpenVP:MdnsName` | `openvp` | The extra host name to answer for (`<name>.local`) |
| `OpenVP:Alerts` | `true` | `false` turns off the alert engine, uptime and metrics history, thresholds and reports (the pages then say so). Settings and history are in `data/alerts` and `data/history`; see [Alerts](#alerts) |
| `OpenVP:Autoload:slot1` | | Program file to load into slot 1 at startup. Wins over the slot's remembered program (see [Autostart](#autostart-and-crash-policy)) |
| `Urls` | `http://0.0.0.0:8080` | Web console address |
| `--healthcheck` | | A command-line flag, not a setting: `openvp --healthcheck` asks the console port of the OpenVP already running (same `Urls`) for `/healthz` and exits 0 or 1. For containers, see [Deploying](#deploying-with-docker-compose-and-helm) |

The backups, schedules, push targets, git sync and program versions are set on their pages and saved in the data folder: `backups.json`, `schedules.json` (and `schedules-history.jsonl`), `targets.json` (the keys are in `integrations/secrets.json`), `git.json`, `versions/` (the kept programs and `settings.json`), `backups/` (the default backup folder) and `tmp/` (uploads waiting to be imported, which are cleared at start-up).

## REST API

Every endpoint except `/api/info`, `/api/login` and `POST /hooks/{token}` needs the access key (see [Security](#security)), for example `curl -H "Authorization: Bearer $(cat data/apikey.txt)" http://host:8080/api/slots`. An unknown slot number returns 404.

| Method | Path | |
|---|---|---|
| GET | `/api/slots` | All slots with state and devices |
| POST | `/api/slots/{n}/program` | Multipart `file` (+ sidecar files, and `start=false`, and `how=push` for a program sent by another OpenVP); loads and starts |
| POST | `/api/slots/{n}/program/path` | `{"path": "C:\\…\\Room.lpz", "watch": true, "start": true, "files": []}`: load a file on the OpenVP computer with its `.sig` and sidecars, and reload it when it changes. Only from that computer (403 otherwise) |
| DELETE | `/api/slots/{n}/watch` | Stop reloading the slot when its file changes |
| POST | `/api/slots/{n}/start` · `stop` · `restart` | Start (a slot in Error is loaded again) · stop · load again from its file and start |
| POST | `/api/slots/{n}/unload` | Unloads; returns the slot plus `undo: {snapshot, start}` (a snapshot taken first; restore it with `{"start": ...}` to undo) |
| POST | `/api/slots/bulk` | `{"slots":[1,2],"action":"start"}` (or `stop`, `restart`, `unload`)`; one result per slot (`ok`, `error`, `undo`) |
| PUT | `/api/slots/{n}/settings` | Any of `name`, `color` (`#3b82f6`), `autostart`, `startOrder`, `startDelaySeconds` and `crash` (`mode` of `stop`, `restart` or `limited`, plus `maxRestarts`, `windowMinutes`, `stuckSeconds`, `faultOnUnhandled`); saved in `slot.json`. `null` clears name, colour and start order |
| GET · DELETE | `/api/recent` · `/api/recent/{id}` | The last 10 programs loaded · remove one |
| POST | `/api/slots/{n}/program/recent/{id}` | Load a recent program into the slot (from its path, local only, or from the kept copy) |
| GET | `/api/trash` | What can still be undone (kept 24 hours) |
| POST | `/api/trash/{id}/restore` | Put a deleted file, folder, snapshot or recording back (409 if something is in the way). File, snapshot and recording DELETEs return `undo` with this id |
| POST | `/api/shares/{id}/undo` | Bring back a link revoked in the last 10 minutes, with the same token |
| PUT | `/api/slots/{n}/room` | `{"roomId":"ROOM2"}` sets the slot's Room ID (saved in `slot.json`); returns the slot, or 400 if the ID is invalid or another slot has it |
| GET / POST | `/api/slots/{n}/signals` | List; set with `{"name":"...","value":1}` |
| GET / POST | `/api/slots/{n}/step` | Step-through state; change with `{"enabled":true,"onlyBreakSignals":true,"breakSignals":["sig"],"conditions":["Vol > 500"]}` (a condition that doesn't parse is a 400). A paused step carries `reason` when a watch or condition stopped it |
| POST | `/api/slots/{n}/step/next` · `step/run` | Release the paused change · turn stepping off |
| GET · PUT | `/api/slots/{n}/watches` | The slot's watches with their state (`idle`, `waiting`, `true`, `error`, `off`), hit counts and recent hits · replace them with `{"watches":[{"expression":"Room_On=1 for 2s","pauseStep":false,"enabled":true,"name":"..."}]}` (400 with the reason if one doesn't parse). Saved in the slot's `debug.json` |
| POST · DELETE | `/api/slots/{n}/watches/check` · `watches/hits` | Check an expression without saving it (`{"expression":"..."}`; says which signals aren't in the program) · clear the hit list |
| GET · PUT | `/api/slots/{n}/monitor` | Oscillation detector and never-changed settings (`oscillation`, `maxChangesPerBurst`, `oscillationChanges`, `oscillationWindowMs`, `trackChanges`), whether it is listening, and the recent alerts · change any of them |
| DELETE | `/api/slots/{n}/monitor/alerts` | Forget the alerts |
| GET · POST | `/api/slots/{n}/never-changed` · `never-changed/reset` | Signals that haven't changed since tracking was reset (`tracking`, `since`, `total`, `signals`) · set the reset point to now |
| GET | `/api/slots/{n}/bookmarks` | `{program, starred, notes}` for the loaded program's name |
| PUT | `/api/slots/{n}/bookmarks/star` · `bookmarks/note` | `{"signal":"Vol","starred":true}` · `{"signal":"Vol","note":"max 50000"}` (empty note removes it) |
| GET · POST | `/api/slots/{n}/traces` | List saved traces · save one: the body is a trace as JSON or CSV (the debugger posts what it shows), `?name=` names it. A file from the export is accepted as is |
| GET · PUT · DELETE | `/api/slots/{n}/traces/{id}` | A saved trace as JSON (`?format=csv` for CSV) · `{"name":"..."}` renames it · delete it |
| POST | `/api/slots/{n}/traces/compare` | `{"a":id,"b":id,"toleranceMs":50}`: changes only in one trace (`onlyA`, `onlyB`), steps with another `value`, and `timing` differences beyond the tolerance, with counts |
| POST | `/api/slots/{n}/trace/export?format=csv\|json` | Turns a posted trace (JSON or CSV) into a download with every field: time, signal, kind, value, source, symbol, cause, depth, chain |
| GET | `/api/slots/{n}/why?signal=NAME` | Why the signal has its value: `summary`, `steps` (kind, signal, value, time, text, symbol, cause), `complete` and a `note`. Reads the slot's recent trace, which is kept while a debugger window is open on it |
| GET · DELETE | `/api/slots/{n}/exceptions` | Unhandled exceptions the SIMPL# program threw (type, message, source, stack, frames with file and line when the `.pdb` was loaded) · clear them |
| GET | `/api/slots/{n}/support-bundle?includeProgram=true` | A zip for support: log, recent trace, compatibility report, summary, metrics, settings, activity, exceptions, watches, version and OS. No access key; no program, NVRAM or User files unless `includeProgram=true` |
| GET | `/api/search/signals?q=vol&max=50` | Signals whose name contains every word of `q`, across all loaded slots: `slot`, `program`, `signal`, `kind`, `value` |
| POST | `/api/slots/{n}/console` | `{"command":"ipt"}` |
| GET · POST | `/api/slots/{n}/simulators` | The generic `profiles`, the slot's `simulators` (state, rules, status, recent activity) and the COM `ports` the program has opened · add one: `{"profile":"projector","name":"Lobby","kind":"com","com":"COM-01"}` (or `"kind":"tcp"`/`"udp"` with `"port"`), or a full definition with `rules`, `unsolicited`, `onConnect`, `initialState` (400 with the reason if a pattern doesn't compile) |
| PUT · DELETE | `/api/slots/{n}/simulators/{id}` | Change a simulator (any of the fields; `"resetState":true`) · remove it |
| POST | `/api/slots/{n}/simulators/{id}/state` · `try` | `{"key":"power","value":"1"}` or `{"reset":true}` · `{"text":"PWR?\\r"}` runs a message on a copy and returns the `replies` with their `delayMs` |
| GET | `/api/slots/{n}/ports` · `ports/traffic?key=COM-01&after=SEQ` | The COM ports the program opened (`open`, bytes, the attached `simulator`) · their traffic: `entries` of `seq`, `time`, `dir` (`tx` is from the program, `rx` to it), `text` (Latin-1) and `origin` |
| POST | `/api/slots/{n}/ports/send` · `ports/clear` | `{"key":"COM-01","text":"PWR=1\\r"}` gives the program RX (escapes decoded, `"raw":true` to skip that; 409 if the program isn't running) · empty a port's view |
| GET | `/api/slots/{n}/sockets` · `sockets/{id}/traffic?after=SEQ` | Every socket the program opened (kind, role, local, remote, `redirectedTo`, state, bytes, opened, closed); calling either marks sockets as watched for 30 s · one socket's traffic |
| GET · POST | `/api/slots/{n}/redirects` | The slot's network redirects (and the simulators they can point at) · add `{"from":"192.168.1.50:23","to":"sim:ID","proto":"any","note":""}` |
| PUT · DELETE | `/api/slots/{n}/redirects/{id}` | Change (including `"enabled":false`) · remove |
| GET · PUT | `/api/slots/{n}/ipremap` | The remap and the registered IP IDs with their public IDs · `{"program":"03","public":"0A"}` |
| DELETE | `/api/slots/{n}/ipremap/{ipid}` | Panels connect to the program's own number again |
| GET · POST · DELETE | `/api/discovery` | The last scan (`running`, `stage`, `subnets`, `devices`, `notes`) · start a scan (409 if one is running) · stop it |
| GET | `/api/consoles` | Telnet, SSH and mDNS: enabled, listening, port, sessions open |
| GET | `/api/slots/{n}/iptable` | IP IDs with each connected client: address, hostname, model, firmware, counters, details; `publicIpId` and `remapped` when the IP ID is [remapped](#ip-id-remap) |
| POST | `/api/slots/{n}/iptable/{ipid}/identify` | Run reverse DNS and Crestron autodiscovery again |
| POST | `/api/slots/{n}/iptable/{ipid}/clients/{id}/login` | `{"username":"...","password":"...","port":22}`; SSH in and run read-only commands (credentials not stored) |
| GET | `/api/slots/{n}/compat` | Compatibility report |
| GET | `/api/slots/{n}/summary` | `{loaded, program, kind, summary}`: the loaded program in one sentence (`summary.text`) and as numbers (`panels`, `eiscs`, `otherDevices`, `comPorts`, `irPorts`, `splusModules`, `userMacros`, `signals`). The same `summary` is in each slot of `/api/slots` |
| POST | `/api/check` | "Will it run?" without loading. Multipart `file` (the program) plus its `.sig` and sidecar files; returns `{file, kind, verdict, headline, reason, problems, summary}` where `verdict` is `will-run`, `partly` (with `problems`: `kind`, `name`, `detail`) or `wont-run` (with `reason`). Loads nothing |
| POST | `/api/check/path` | The same for a file on the OpenVP computer: `{"path": "C:\\…\\Room.lpz", "files": []}` (the `.sig` and sidecars next to it come along). Only from that computer (403 otherwise) |
| GET | `/api/activity?slot=&limit=` | Who did what and when, newest first (`time`, `action`, `text`, `slot`, `who`, `user`, `detail`); `slot` keeps one slot's entries, `limit` is 200 by default and at most 5,000 |
| GET · POST | `/api/demo` | `{available, occupied}` · load the demo into slots 1 and 2 and start it; `{"replace": true}` is needed if a slot already has a program (409 otherwise) |
| GET | `/api/templates` · `/api/templates/{id}` | The starter projects · download one: `simpl-sharp` (a `.zip`) or `ch5-panel` (a `.ch5z`) |
| GET | `/api/slots/{n}/files?root=user&path=config` | List a folder in the slot's NVRAM (`root=nvram`) or User (`root=user`) folder |
| GET | `/api/slots/{n}/files/missing` | Files and folders the program looked for and didn't find, with the folder each belongs in (also in the `missing` field of the folder listing) |
| POST | `/api/slots/{n}/files?root=user&path=config` | Multipart upload into that folder; a file name can include sub-folders (`rooms/r1.xml`) |
| POST | `/api/slots/{n}/files/folder` · `files/rename` | `{"root":"user","path":"config","name":"rooms"}` creates `config/rooms` · renames `path` to `name` |
| GET | `/api/slots/{n}/files/download?root=user&path=config/rooms.json` | Download a file |
| DELETE | `/api/slots/{n}/files?root=user&path=config` | Delete a file, or a folder and everything in it |
| GET / POST | `/api/slots/{n}/snapshots` | List the slot's snapshots, newest first (id, time, program, file, reason, size) · save one now |
| POST | `/api/slots/{n}/snapshots/{id}/restore` | Optional `{"start":true}` or `false` to override whether it starts. Roll back: saves the current state first, replaces the program files, NVRAM and User with the snapshot's, loads the program, and starts it if it was running. Returns the slot, or 404 for an unknown id |
| DELETE | `/api/slots/{n}/snapshots/{id}` | Delete a snapshot |
| GET | `/api/slots/{n}/versions` | The slot's [program versions](#versions-and-diff), newest load first: `id`, `name`, `file`, `hash`, `size`, `files`, `kind`, `firstLoaded`, `loadedAt`, `loads`, `how`, `who`, `lastHow`, `lastWho`, `note`, `available`, `current`; also `keep` and the `current` version id |
| PUT · DELETE | `/api/slots/{n}/versions/{id}` | `{"note":"..."}` sets or clears the note (500 characters) · forget a version and delete its files (409 for the loaded one) |
| POST | `/api/slots/{n}/versions/{id}/load` | `{"start":true}`: load the version (after a snapshot); returns the slot, or 404 if its files are gone |
| GET | `/api/slots/{n}/versions/{id}/download` | The program file, or a zip when the version has several files |
| POST | `/api/slots/{n}/versions/diff` | `{"a":"loaded"\|"<version id>","b":"...","aSlot":2,"bSlot":3}` (the slots default to `{n}`): the comparison as `sections` (`title`, `added`, `removed`, `changed`, `items` of `name`, `change`, `detail`), `summary`, `notes`, `identical` and a plain `text` report. Starts nothing |
| GET · PUT | `/api/versions/settings` | `{"keep":30}`: how many versions each slot keeps (1-1000) |
| GET | `/api/slots/{n}/files/text?root=user&path=config/rooms.json` | Open a text file for editing (up to 2 MB): `text` (always `\n` line breaks), `tag`, `kind` (`json`, `xml`, `text`), `size`, `modified`, `newline`, `encoding`. 413 over the limit, 400 for binary or a file type that isn't text |
| PUT | `/api/slots/{n}/files/text?root=user&path=...` | `{"text":"...","tag":"<tag from the read>","force":false,"allowInvalid":false}`: save, keeping the earlier content in the trash. `undo` in the answer is a trash id for `POST /api/trash/{id}/restore`. 409 `{"conflict":true}` if the file changed on disk since `tag` (send `force` to overwrite), 400 `{"invalid":true,"line":3,"column":7}` for bad JSON or XML (send `allowInvalid` to save anyway) |
| POST | `/api/slots/{n}/files/validate` | `{"name":"a.json","text":"..."}`: `{ok, line, column, error}` without saving |
| POST | `/api/export` | `{"history":false,"snapshots":false,"secrets":false,"password":"...","slots":[1,2]}`: the [export](#backups-and-export) as a download (`.openvp`). `secrets` needs a `password` of 8+ characters (400 otherwise) |
| POST | `/api/import/preview` | Multipart `file` (and `password`), or JSON `{"previewId":"...","password":"..."}` for a file already uploaded: what is in it (`slots`, `settings`, `hasSecrets`, `passwordOk`, `warnings`, ...) and a `previewId`. The file waits 30 minutes. Changes nothing. 400 `code` is `bad-archive`, `unsafe-path` or `wrong-version`, 413 `too-big` |
| POST | `/api/import` | JSON `{"previewId":"...","slots":[1,2],"replaceSettings":false,"restoreSecrets":false,"password":"...","restoreHistory":false}`, or multipart `file` plus the same fields (what a push sends): imports, returning `slots` (`slot`, `ok`, `loaded`, `started`, `message`), `settingsReplaced`, `secretsRestored` and `warnings`. 422 `code` `wrong-password` or `need-password` changes nothing. A slot's snapshot is taken first |
| GET · DELETE | `/api/backups` · `/api/backups/{id}` | Backup `settings`, the `folder`, `next`, `last` and the `backups` · delete one |
| PUT | `/api/backups/settings` | Any of `enabled`, `time` (`03:00`), `keepDays`, `folder` (null for `data/backups`), `includeHistory`, `includeSnapshots`, `includeSecrets` with `password` |
| POST | `/api/backups/run` | Back up now (500 with the reason, and a `backup.failed` alert, if it fails) |
| GET · POST | `/api/backups/{id}/download` · `.../preview` | The file · put it in the import preview (then `POST /api/import` with `"how":"restore"`) |
| GET · POST | `/api/schedules` | The [schedules](#schedules) (`mode`, `time`, `days`, `cron`, `at`, `runMissed`, `action`, `nextRun`, `lastRun`, `lastOk`, ...) with `zone`, `now` and the `history` · add one: `{"name":"Nightly","mode":"daily","time":"03:30","action":{"type":"restart","slot":1}}`. `mode` is `once` (`at`: `2026-01-05T09:00`), `daily`, `weekly` (`days`: 0 is Sunday) or `cron`. `action.type` is `restart`, `start`, `stop`, `loadVersion` (`versionId`), `loadPath` (`path`; only from this computer), `setSignal` (`signal`, `value`), `pulse` or `backup` |
| PUT · DELETE | `/api/schedules/{id}` | Replace (send everything, `enabled` too) · delete |
| POST | `/api/schedules/{id}/run` · `/api/schedules/preview` | Run now · the next five times of a schedule as typed, without saving it |
| GET · POST | `/api/targets` | [Push targets](#push-to-other-machines) (`name`, `url`, `fingerprint`, `hasKey`, `lastOk`, ...) · add `{"name":"B","url":"http://host:8080","key":"...","fingerprint":null}` |
| PUT · DELETE | `/api/targets/{id}` | Change (`key` is kept if left out, removed by `""`) · remove it and its key |
| POST | `/api/targets/{id}/test` · `/api/targets/certificate` | Connect and check the key · `{"url":"https://..."}`: the certificate that address presents (`fingerprint`, `subject`, `notAfter`, `trusted`) |
| GET · POST | `/api/push` · `/api/push/{id}` | Recent pushes and one's progress (`targets` with `state`, `message` and per-slot results; `done`) · start one: `{"what":"version","slot":1,"version":"loaded"\|"<id>","start":true,"targets":[{"id":"...","slots":[1,2]}]}` or `{"what":"export","targets":["id"],"slots":[1],"replaceSettings":false,"secrets":false,"password":"..."}` |
| GET · PUT | `/api/git` | The [git config](#git-backed-config) status: `available`, `version`, `settings`, `isRepo`, `last`, `error`, `commits` · change `enabled`, `repo`, `remote`, `push`, `branch`, `includePrograms`, `maxProgramMb` |
| POST | `/api/git/init` · `/api/git/commit` · `/api/git/push` | `{"repo":"/path"}` make the repository · `{"message":"..."}` commit now (`skipped` lists what was left out) · push now |
| GET | `/healthz` | `ok`, with no access key (not on the tunnel port). For container health checks |
| GET | `/api/slots/{n}/recording/status` | What the slot is doing: `mode` (`idle`, `recording`, `replaying`), recording elapsed time and counts, replay progress and, once finished, its report. The console polls it |
| POST | `/api/slots/{n}/recording/start` · `recording/stop` | Start recording · stop and save it, with an optional `{"name":"..."}` (409 if busy or there's no program) |
| GET / POST | `/api/slots/{n}/recordings` | List the slot's recordings (name, program, length, counts, last result) · upload one as multipart `file` (and optional `name`); it gets a new id |
| GET / PUT / DELETE | `/api/slots/{n}/recordings/{id}` | The recording as JSON · `{"name":"..."}` renames it · delete it with its reports. Ids are the ones the list returns; anything else is a 404 |
| GET | `/api/slots/{n}/recordings/{id}/download` | The recording as a file |
| POST | `/api/slots/{n}/recordings/{id}/replay` | `{"fast":false,"includeEisc":true}`: replay it in the background (409 if the slot is busy or the program isn't running) |
| POST | `/api/slots/{n}/replay/cancel` | Cancel the replay under way |
| GET | `/api/slots/{n}/recordings/{id}/report` · `reports` | The latest replay report (404 if it has never been replayed) · the last 5 |
| GET | `/api/unsupported` | Everything programs used that isn't supported yet, as the Markdown file (`?format=json` for JSON). See [Compatibility](#compatibility) |
| GET | `/api/log?slot=n&max=500` · `/api/devices` | The slot's recent log lines (`time`, `level`, `message`, and `detail` for an exception's stack trace) · every IP ID. `/ws/trace?slot=n` streams them (`trace=0` for log lines only) |
| GET | `/api/metrics` | CPU, memory, threads and per-slot activity; `?history=false` leaves out the five-minute history. `/ws/metrics` sends the same once a second |
| GET | `/api/alerts?limit=` | `status` (`critical`, `warning`, `info`, `acknowledged`, `muted`, `color`), the `active` alerts, the `history` (newest first, each with its `deliveries`), `muted` keys and the number of stream `listeners`. Severities are words (`info`, `warning`, `critical`) |
| POST | `/api/alerts/{id}/ack` · `mute` · `unmute` | Acknowledge · mute (`{"minutes":60}`, or `{}` until unmuted) · unmute |
| GET · PUT | `/api/alerts/config` | `settings`, `channels` (no secrets, only `hasPassword`, `hasToken`, `hasSecret`, `hasUrl`), `rules`, `thresholds`, `report`, and the lists of `kinds` and `metrics`. A PUT takes any of `settings`, `rules`, `thresholds`, `report` (400 says what is wrong) |
| POST · PUT · DELETE | `/api/alerts/channels` · `/{id}` | Add · change (`password`, `token`, `secret` and `url` for Teams and Slack are kept if left out, removed by `""`) · remove (also from the rules) |
| POST | `/api/alerts/channels/{id}/test` | Send a test message once and return the delivery |
| GET | `/api/alerts/stream` | Server-sent events: `status`, then `alert` for each raise, resolve and test (see [Alerts](#alerts)). Not on the tunnel port |
| GET | `/api/alerts/status` · `/api/alerts/thresholds` | The counts and colour · the enabled thresholds (for graph lines) |
| GET | `/api/uptime?range=24h\|7d\|30d\|90d&buckets=96` | Availability for each slot and device: `availability`, `upSeconds`, `downSeconds`, `drops`, `buckets` (timeline, -1 for no data), `outages` |
| GET | `/api/uptime/heatmap?range=7d&mode=hour\|day&offset=minutes` | Offline minutes and drops per device by hour of the day or by day; `offset` is the viewer's offset from UTC |
| GET | `/api/metrics/history?range=5m\|1h\|24h\|7d\|30d\|90d` | Averages and peaks for the process and each slot |
| GET | `/api/reports/preview?period=daily\|weekly[&download=1]` · `/api/reports/data` | The health report as an HTML page (or a download) · as JSON |
| POST | `/api/reports/send` | `{"period":"daily","channels":["id"]}` (the schedule's channels if left out); returns the history entry with each channel's result |
| GET | `/metrics` | Prometheus text format (see [Prometheus and Grafana](#prometheus-and-grafana)). Needs the access key unless `OpenVP:MetricsPublic=true`; not on the tunnel port |
| GET · PUT | `/api/slots/{n}/signals/{name}` | One signal: `{slot, name, kind, value}` · set it from `1`, `true`, `"text"`, `{"value": 1}`, or plain `50%` / `pulse` (400 for a value that doesn't fit, 404 for an unknown signal, 409 with no program). The name must be URL-encoded |
| POST | `/api/slots/{n}/signals/{name}/pulse` | Pulse a digital signal (high, then low) |
| GET | `/api/slots/{n}/signals/{name}/stream` | Server-sent events: `event: value` with the current value, then `event: change` for each change (see [REST per signal](#rest-per-signal)) |
| GET | `/api/integrations` | Status of all five: MQTT (with `status`), webhooks, hooks, OSC, Art-Net, Prometheus |
| GET · PUT | `/api/integrations/mqtt` | Broker settings (`enabled`, `host`, `port`, `tls`, `username`, `clientId`, `baseTopic`, `homeAssistant`, `discoveryPrefix`) with `hasPassword`, `effectiveBase` and `status`. The PUT takes any of them plus `password` (kept if left out) or `clearPassword`; the password is never returned |
| GET · PUT | `/api/integrations/osc` · `/api/integrations/artnet` | `{listenPort, targetHost, targetPort}` · `{enabled, target, port, rateHz, listen}`, each with its `status` |
| GET | `/api/integrations/prometheus` | `{path, public, keyRequired, scrapes, lastScrape}` |
| GET · PUT | `/api/slots/{n}/integrations` | The slot's `mqtt`, `osc`, `artnet` signal lists and `webhooks` (a webhook is `{id, name, url, headers, secret, signal or expression, enabled}`; a secret is never returned, only `hasSecret`, and is kept when left out of a PUT, removed by `""`). A PUT replaces just the sections it contains; 400 says what is wrong |
| POST | `/api/slots/{n}/webhooks/{id}/test` | Send a sample event once and return the delivery |
| GET | `/api/webhooks/deliveries?slot=&max=` | The delivery log, newest first |
| GET · POST | `/api/hooks` | Incoming webhooks (`id`, `token`, `path`, `label`, `slot`, `allow`, `enabled`, `count`, `lastUsed`) · make one: `{"slot":1,"label":"Doorbell","allow":["Doorbell"]}` |
| PUT · DELETE | `/api/hooks/{id}` | Change `label`, `allow` or `enabled` · revoke (the token stops working at once) |
| POST | `/hooks/{token}` | **No access key; not on the tunnel port.** `{"signal":"Room_On","value":1}` or `{"pulse":"Button1"}`; 403 if the hook doesn't allow the signal, 404 for an unknown token, 429 when rate limited |
| GET | `/api/welcome` · POST `/api/welcome/dismiss` | `{"show":true}` until someone finishes or skips the first-run welcome screen; dismiss remembers that for everyone (kept in `data/welcome.json`) |
| GET / POST | `/api/shares` | List the active share links (id, token, `path` (`/s/{token}`), label, slot, IP ID, `kind` `ch5` or `test`, `hasPin`, created, expires, last used, `connected`) · create one with `{"slot":2,"ipId":"03","label":"Client","pin":"482913","expires":"1d"}` (`pin` optional, 4-8 digits; `expires` is `1h`, `1d`, `1w` or `never`) |
| DELETE | `/api/shares/{id}` | Revoke a link; open connections close within a few seconds |
| GET | `/api/panels` | The uploaded CH5 projects (slot, IP ID, name, files, size) |
| POST / DELETE | `/api/slots/{n}/panels/{ipid}/ch5` | Upload a `.ch5z` as multipart `file` (replaces any project there; 400 with the reason if it is refused) · remove the project |
| GET | `/api/network` | This computer's LAN addresses, for building a link someone else can reach |
| GET | `/api/tunnel` | Internet access: `state` (`off`, `downloading`, `starting`, `on`, `error`), `url`, `message`, `needsDownload`, `available`, recent `cloudflared` output |
| POST | `/api/tunnel/start` · `tunnel/stop` | Start the tunnel (`{"download":true}` also allows downloading `cloudflared` if it isn't installed; without it you get `needsDownload`) · stop it and kill `cloudflared` |
| GET / WS | `/s/{token}` · `/s/{token}/ws` · `/s/{token}/app/…` · `/s/{token}/status` | What a link holder uses (no access key; the token and PIN are the credential). `410` once the link has expired or been revoked. `POST /s/{token}/pin` checks a PIN |
| GET | `/api/info` | Version, slot count, and whether you're logged in |
| POST | `/api/login` · `/api/logout` | `{"key":"..."}`; sets or clears the login cookie |

## Development

```bash
dotnet build
dotnet run --project tests/OpenVP.Tests          # dependency-free test runner
./tools/regen-facades.sh                         # after adding public types to OpenVP.SimplSharp
```

If a program fails with "needs Crestron assemblies OpenVP has no facade for: X", add `X` to `tools/facade-names.txt` and run the script.

## Roadmap

- More SIMPL# API: `TCPServer`, SSH transport (API is in place, connect not yet), smart objects, HTTP(S) client, CWS server
- The rest of the SIMPL logic library, and confirming the *documented* symbols' pins and codes with the symbol survey (see [docs/symbols.md](docs/symbols.md))
- Confirm IP ID storage from real `.smw` files
- Validate CIP against real touch panels; remote EISC between processors
- **Run compiled SIMPL Windows programs (`.lpz`) directly** so every program works, with or without its source. Progress:
  - [x] Read the package, IP table, compiled logic (`.bin`) and signal names (`.sig`). Checked against 101 real programs; see [docs/lpz-format.md](docs/lpz-format.md)
  - [x] **Inspect .lpz** in the web console: what's in a program and how much of it OpenVP can run
  - [x] Map compiled primitives to SIMPL symbols (matched across 36 programs that ship their source)
  - [x] Run compiled logic: 36 primitive types so far, plus IP devices (panels, EISC, DM/NVX joins) and permanent strings. **Load a `.lpz` like any other program**
  - [ ] Remaining primitives (Truth Table, Analog RAM, Analog Scaler, Analog to Serial, crosspoint routing, …)
  - [x] Run compiled SIMPL+ modules (.NET) through a clean-room `SplusObjects` / `SplusLibrary`: events, PUSH/RELEASE/CHANGE, WAIT, DELAY, MakeString, parameters, files, sockets. On a 230-module production program every module loads
  - [ ] Confirm behavior against a real processor (side-by-side signal traces)

## Contributing

Contributions are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md) and the [code of conduct](CODE_OF_CONDUCT.md). Please report security problems privately as described in [SECURITY.md](SECURITY.md).

## License

MIT. See [LICENSE](LICENSE). The desktop app bundles Electron and other open-source packages under their own licenses.
