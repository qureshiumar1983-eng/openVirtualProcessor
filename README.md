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

Open **http://localhost:8080**. From another machine you'll be asked for the access key, which the server prints when it starts (see [Security](#security)). Pick a slot, choose a `.cpz`, `.dll` or `.smw` file, and click **Load & start**. Click **open test panel** next to a device to get a browser panel at that IP ID. Every page has a **Help** link that explains what each button on it does.

### Desktop app (Windows)

**`OpenVP.exe`** is OpenVP as a desktop app (Electron), in one file: nothing to install, no .NET needed, no console window. Run it and the web console opens in its own window. Test panels and the debugger open in their own windows too. Closing the main window stops OpenVP. Your data and the log (`openvp.log`) are kept in `%APPDATA%\OpenVP`. The first time it runs, Windows may ask whether to let it through the firewall; allow it if you want panels or other machines to connect.

Download the latest from the [Releases](https://github.com/qureshiumar1983-eng/openVirtualProcessor/releases) page: every push to `main` builds a new `OpenVP.exe`, checks that it starts, and publishes it there.

**Updates.** OpenVP.exe checks that Releases page when it starts and every 6 hours (it uses GitHub's free public API, no account needed). When there's a newer version it pops up and offers **Update now**, **Later** or **Skip this version**. Update now downloads the new `OpenVP.exe`, checks it against the release's SHA-256, replaces the old file and restarts OpenVP. That stops your running programs for a few seconds. If OpenVP can't write to its own folder (for example Program Files), it saves the new version to Downloads instead. To check right away, press Alt and choose **Help → Check for updates…**.

**Working on OpenVP** (needs the .NET 8 SDK and Node.js): double-click **`Start OpenVP.bat`**, or run `npm install` once and then `npm start` in `desktop/`. That opens the app on your source code: edits to the web pages (`src/OpenVP.Server/wwwroot`) show up in the window straight away, and C# changes rebuild and restart the server automatically (about 10–15 seconds) and the window reloads. To build a standalone `OpenVP.exe` on your PC, run **`publish-desktop.bat`** (or `npm run build` in `desktop/`); it writes `desktop\dist\OpenVP.exe`.

Or with Docker:

```bash
docker build -t openvp .
docker run -p 8080:8080 -p 41794:41794 -v $PWD/data:/app/data openvp
```

The container prints its access key at startup (`docker logs <container>`), and it's saved in `data/apikey.txt`. To choose your own, add `-e OpenVP__ApiKey=your-long-random-key`.

### Try the samples

```bash
dotnet build samples/SampleProgram          # a small SIMPL# Pro program
# then in the web console:
#   slot 1  ← samples/SampleProgram/bin/Debug/net8.0/SampleProgram.dll
#   slot 2  ← samples/SimplWindows/SampleProgram.smw  (also select SampleProgram.openvp.json)
```

Both register a panel at IP ID 03 and an EISC at IP ID 10 on 127.0.0.2. Press digital 10 on slot 1's panel and slot 2's panel lights up digital 10, and the reverse. That's a SIMPL# program and a SIMPL Windows program talking through the processor.

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

### SIMPL# programs

A compiled program references Crestron's assembly names. Each slot gets its own .NET load context in which those names resolve to **facades** that forward to OpenVP's API. The program itself is never changed. Every program event (panel presses, `CTimer` callbacks, socket data) runs on that slot's own thread, one at a time.

Before running, OpenVP reads the program's metadata and lists every Crestron type and member it uses that isn't implemented yet. Click **Compatibility** in the console, or check the log.

Implemented so far: `CrestronControlSystem`, `InitializeSystem`, program status events, `CrestronConsole` (print, console commands), `ErrorLog`, `CTimer`, `CrestronInvoke`, `CrestronThread.Thread`, `CCriticalSection`/`CMutex`/`CEvent`, `InitialParametersClass`, `TCPClient`, `BasicTriList` with all six sig collections and `SigChange`, touch panel and XPanel classes, and local EISC (`ThreeSeriesTcpIpEthernetIntersystemCommunications`).

### SIMPL Windows programs

OpenVP reads the `.smw` source, not the compiled `.lpz`, so nothing of Crestron's runtime is decompiled. The file is a list of blocks: `Sm` symbols point at `Sg` signals by handle (`I1=4` = input pin 1 is wired to signal 4), and pins are grouped digital, analog, serial (`n1I`, `n2I`, `n3I`). When a signal changes, every symbol listening to it runs and its outputs drive further signals until the logic settles. The `1` signal goes high at start, as it does on a processor.

**Logic symbols** come from one library shared with compiled programs, so a symbol behaves the same whether you load the `.smw` or the `.lpz`. [docs/symbols.md](docs/symbols.md) lists all of them with their SmC code, compiled type and how sure we are of each: *compiled* symbols were checked against real compiled programs and their source; *inferred* ones were seen compiled in real programs, with behavior worked out from how those programs wire them (mostly inside macros whose source isn't available); *documented* ones were written from the SIMPL symbol descriptions and still need a real program to confirm their pins. All 96 compiled programs in `Sample Programs/` load and run; only three compiled types in them (107, 113, 261, 37 records in all) are still unidentified. **Load the `.smw` together with its compiled `.lpz` and `.sig`** (select all three, or put them side by side) and OpenVP runs user macros (`.umc`/`.cmc`) and SIMPL+ modules from the compiled program, which the `.smw` alone can't: their source files aren't part of it. In fact every logic symbol then runs from its compiled record, with the compiler's pin order and parameter units, and signal kinds come from the `.sig`; devices, panel children, ports and crosspoints still run from the `.smw`. Each `.smw` symbol is matched to its compiled records by its position under the Logic folder, and only used if every program-level signal those records touch is wired to that symbol, so a `.smw` edited after the compile never picks up the wrong logic (those symbols run natively, or are reported unsupported). On the sample programs, a `.smw` run this way ends up with the same signal values as the `.lpz` run, apart from timing (clocks, start-up pulses) and network devices. Symbols disabled in SIMPL Windows (`OpF=0`) are left out, as the compiler does. Without the `.lpz`, macros and SIMPL+ aren't run, and a few symbols read their pins in the wrong order (Multiple One Shots, Set/Reset Latch, Toggle), so prefer loading all three. In a `.smw`, parameters are converted the way the compiler does: times to 1/100 s, `\x0D`-style escapes in strings decoded. Device symbols (panels, XPanels, EISC) map their pins to joins. Only devices on the processor's Ethernet slot get an IP ID (taken from the hardware tree in the file). What hangs off a panel is mapped too: Smart Graphics objects and Contract Editor components (their joins travel with the object ID, `P1`), and reserved-join extenders such as button toolbars, app control and hard buttons, at the join bases read off the compiled sample programs (`src/OpenVP.Simpl/ExtenderJoins.cs`). Crosspoint routing (Equipment, Control and Connect) runs as in compiled programs, and COM and IR port symbols are logged and handed to the port bridge like compiled ports (below). Other sub-devices (DM/NVX inputs and outputs, card slots) and processor hardware (relays, I/O, system control) aren't mapped yet; any that are wired are listed once in the log and in [docs/unsupported.md](docs/unsupported.md).

**Two things the format doesn't make clear, so you may need to help:**

1. **Symbol codes.** Symbols are identified by a numeric `SmC` code, and Crestron doesn't publish the list. OpenVP matches by the symbol's library name ("AND", "Interlock"…), then by the codes it knows. To teach it codes, add `MyProgram.symbols.json` next to the program (or `data/symbols.json` for all programs): `{ "1234": "AND", "1240": "Interlock" }`. Compiled programs have the same problem with primitive types; name a type with `MyProgram.primitives.json` (or `data/primitives.json`): `{ "301": "NAND" }`. Names must match [docs/symbols.md](docs/symbols.md); unknown ones are reported in the slot log.
2. **IP IDs.** Where an IP ID can't be found in the file, set it in `MyProgram.openvp.json`, keyed by symbol handle: `{ "ipids": { "21": "03" } }`.

The fastest way to close these gaps is real programs. **Symbol survey** reads any number of them and works out the codes for you:

```bash
dotnet run --project tools/SymbolSurvey -- path/to/programs --out survey
```

It lists every symbol the programs use and whether OpenVP runs it. For each compiled `.lpz` that comes with its source (archived inside, or a `.smw` of the same name next to it) and its `.sig`, it pairs each compiled record with the source symbol that drives the same signal. That identifies the symbol's SmC code and compiled type in one go, and it flags any conflict with what OpenVP already believes. With `--out` it writes `symbols.json` and `primitives.json`, ready to drop into `data/`. The quickest test program: one of each symbol from the SIMPL Windows logic library, each output on a named signal, compiled with the source archived.

### IR and COM ports

A virtual processor has no IR emitters or serial ports, so compiled programs' IR ports and COM ports (including those of DM-NVX encoders and decoders) don't talk to hardware. They still run honestly: every IR command (a rising trigger on an IR driver symbol) and every COM transmission is written once to the slot log, e.g. `IR IPID 31 port 1: DRV.CBL.1.KEYPAD.1 (cmd 0)` or `COM IPID 11 port 1 TX (7): "PWR ON\x0D"`, and the signals involved show in the debugger trace. To feed a program a COM reply, force the port's receive signal in the debugger; it is logged as `RX`. IR ports on the control processor itself read `IR processor slot 6 port 1`. Programs loaded from `.smw` source behave the same; their ports are named after the parent symbol and port number (`CP4 COM/COM-01`), and an IR command is identified by its position in the driver and its name (`IR processor port 2: MUTE (cmd 1)`). `comports.json` applies to compiled programs only.

To reach a real serial device through a serial device server, add `comports.json` to the program folder (or `data/`) mapping a port to `host:port`, e.g. `{ "IP-ID-11/Slot-08/COM-01": "192.168.1.50:4999" }`. Code that can reach real ports (say, NVX endpoints) can implement `IPortBridge` and set `LpzRuntime.PortBridgeFactory`; nothing ships that speaks to NVX hardware yet.

### Panels

- **Browser / CH5:** `ws://host:8080/ws/panel?ipid=03&slot=1`, JSON `{"t":"b","j":1,"v":true}` (`b` digital, `n` analog, `s` serial). `wwwroot/openvp-crcomlib.js` gives a page `CrComLib.publishEvent` / `subscribeState` for numeric join names, so simple CH5 projects can point at OpenVP. `panel.html` is a ready-made test panel.
- **Real panels over CIP (experimental):** listener on TCP 41794. CIP isn't publicly documented; the framing follows community write-ups and is unit tested against itself, **not yet against real hardware**. The shipped `appsettings.json` sets `OpenVP:CipLogPackets=true`, which logs every packet; compare with a Wireshark capture of a panel talking to a real processor. All encodings live in `Cip` in `CipServer.cs`. **Smart Graphics** (subpage reference lists, button lists, tab lists) are carried over CIP too, in both directions: item presses, analog and serial values from the panel, and visibility, enable, feedback and text back to it, including in the full update a panel asks for when it connects. Smart objects are identified by a 4-byte object ID inside the normal data packets. This is also experimental and untested on hardware: with packet logging on, any data sub-type OpenVP doesn't understand is logged once with its bytes.

A panel can connect before its program loads. It binds to the IP ID and comes online when the program registers it.

### Room ID

Each slot has a **Room ID**, like the Virtual Control Room ID of a VC-4 program. It defaults to `SLOT01`, `SLOT02`, and so on; change it with `PUT /api/slots/{n}/room` (1-64 letters, digits, `_`, `-` or `.`; unique across slots, not case-sensitive). It is saved in `slot.json` in the slot's folder and is what a program reads from `InitialParametersClass.RoomId` and `RoomName`. Browser panels can connect with `/ws/panel?ipid=03&room=ROOM2` and are routed to the slot with that Room ID (an unknown Room ID gets a 404). Panels connecting over CIP still route by IP ID only for now, binding to the lowest slot that has it; the log warns when two slots register the same IP ID.

### IP table

The **IP table** on the console's main page shows the selected slot's IP IDs, one row for each connected client. Each row starts with who the client is, then its address, IP ID, type, status, how long it has been connected, and how many joins went each way. An IP ID with nobody connected shows when it was last seen. Click a row to see everything known about that client and IP ID. The table refreshes every 2 seconds. The `ipt` console command prints the same list.

How OpenVP works out who a client is:

- **Without credentials, automatically:** it uses the remote address and port, and reverse DNS for the hostname. A client on the OpenVP machine itself is named after this computer. For CIP clients it also sends **Crestron autodiscovery** (UDP 41794). Crestron hardware answers with its hostname, model, firmware and MAC, so a row reads e.g. *Crestron touch panel (TSW-1070)*. XPanel and the Crestron apps don't answer, so a CIP client with no reply is listed as *Software client (XPanel or Crestron app?)* under its computer's hostname. The CIP connect request bytes are kept as well, to help tell client types apart later. Autodiscovery is **experimental**, like CIP: it follows community write-ups and hasn't been tested on every device. **Re-identify** runs these checks again.
- **Browser and CH5 panels:** browser and OS come from the User-Agent, and pages using `openvp-crcomlib.js` also report their screen size, platform, language, time zone and round-trip time.
- **EISC:** the row names the slot and program on the other end of the link.
- **Log in & read…** (optional): type the device's SSH username and password. OpenVP runs only read-only console commands (`hostname`, `ver`, `uptime`, `ipconfig`, `ipt`, `time`), shows what they return, and fills in hostname, model, firmware and MAC from them. The credentials are used for that one login and are never stored or logged.

Metrics for each client: connected since, last message, messages and bytes in and out, joins in and out, heartbeats (and the interval between them for CIP), round-trip time (browser panels), packets waiting to be sent (CIP) and errors. For each IP ID: registered, last online and offline, connections since registered, inputs received and feedback sent.

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

### Debugger

Click **Open debugger** in the console (or go to `/debugger.html?slot=N`). It's laid out like the classic SIMPL Debugger:

- **Signal list** (left): every signal in the slot with its live value. Search, filter by type, and tick **T** to choose which signals go to the trace. Click a column header to sort.
- **Trace** (right): one row per change with time, signal, value and what caused it. **Stream** (F5) pauses and resumes it, **Clear** empties it, **Save…** writes it to a text file. You can trace all signals or only the ticked ones.
- **Display options**: time as absolute, relative or delta; analogs as decimal (`500d`), hex (`01F4h`) or percent; serials as ASCII with `\x0D`-style escapes, or hex.
- **Step-through** (for logic wave problems): turn on **⏸ Step** and the slot's logic pauses on every signal change, right after the new value is set and before anything wired to that signal reacts. **Next** (F10) lets that one change through and stops at the next; **Run** (F8) turns stepping off. Set **Break on** to *marked signals* and tick the **⏸** column (or press F9 on the selected signal) to pause only on the signals you care about; everything else runs through. The bar under the toolbar shows where it's paused, and that signal is highlighted in both lists. **Back** (F7) shows an earlier step: every signal's value as it was then (values that have changed since are dotted), with that change highlighted in both lists. The program itself stays paused where it is; logic can't be run backwards. **Next** walks forward through those steps to the live one, then carries on stepping the program. The history covers the steps this debugger window has seen. Forcing a signal while paused queues it until you step on. Stopping or unloading the slot releases the pause.
- **Forcing signals**: right-click a signal for Set / Clear / Pulse / Toggle (digital) or to enter a value, double-click a digital to toggle it, or use the **Force** bar at the bottom. Analog values can be typed as `500`, `1F4h` or `50%`, and serial values can include `\x0D`, `\r` or `\n`.
- **Zoning in**: rows glow when their signal changes; sort by *recently changed* or *most changes*, or show *active only*. Search takes several terms, `*` wildcards, `/regex/`, `d:` `a:` `s:` for type, `=1` / `>500` value tests and `-term` to exclude (the **?** next to the box lists them). Save sets of signals as **group tabs**, or switch to **tree** to see signals by device and module. In the trace, **follow** shows only the selected signal (or it and its neighbours), and **this burst only** shows one press and everything it caused.
- **Cause and effect**: each trace row says which symbol produced the change (**via**), and is indented by how many hops it is from the press, panel event or force that started it; rows from the same burst share a shade. Right-click a signal for **what drives this** / **what this drives**.
- **Waveform**: a logic-analyzer view of the ticked (**T**) signals under the panes. Wheel to zoom, drag to pan, click to set a marker and read Δt.
- **Paths** (`/paths.html?slot=N&signal=NAME`, or right-click → *Show paths from here*): a map of everything a signal can reach, one column per hop, with feedback loops drawn as magenta arcs. When the program runs, the route each change actually takes lights up and fades like a trail; signals that change again in the same burst get a ring and a count, and ones that oscillate turn red. Slow motion (¼×, 1/10×) and **Replay last burst** make fast bursts watchable. Double-click a digital to press it from the map. Works for SIMPL Windows and compiled programs (not SIMPL#). The API behind it is in [docs/debugger-paths.md](docs/debugger-paths.md).

Each window shows only its own slot, so you can open one per slot and two people can debug two programs on the same processor at once. For SIMPL# slots the signals are device joins, named like `IPID03.out.d1` (panel press) and `IPID03.in.s5` (feedback).

### Metrics

Click **Metrics ↗** in the console header (or go to `/metrics.html`) for a live view that refreshes every second:

- **CPU, memory, threads and thread pool** for the OpenVP process, each with a five-minute graph.
- **Per slot:** events per second, how busy the slot thread is, events waiting in its queue, what it's running right now, the longest single event so far, and how many timers, threads and background jobs the program has.
- **Problems** are listed at the top:
  - **Stuck:** the slot thread has been inside one event for 5 s or more. Nothing else in that slot can run until it returns. The event is named, for example `CTimer ControlSystem.OnPoll`.
  - **Orphaned thread:** a thread a program started (`CrestronThread`, SIMPL+ Function Main) is still running after that program was unloaded or replaced.
  - **Long-running background work:** a `CrestronInvoke` callback or SIMPL+ event handler has been running for 30 s or more.
  - **Backlog:** 1000 or more events are queued for a slot, or background work is waiting for thread-pool threads.

The same numbers are available from the slot console: `cpuload`, `ramfree`, `taskstat` (threads and what the slot thread is doing) and `metrics` (all of it). From scripts, use `GET /api/metrics` or the `/ws/metrics` WebSocket.

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
- Requests from the same machine to `localhost` don't need the key, unless `OpenVP:LocalRequiresKey=true`. This exception never applies to requests that arrive through a reverse proxy (with `X-Forwarded-For`, `Forwarded` or `X-Real-IP` headers) or are addressed to a host name other than localhost, which also stops DNS-rebinding attacks.
- Requests from a web page on another site are refused (403) even when the browser has the cookie, and even from localhost. So a malicious page open in your browser can't upload a program.
- Panels (`/ws/panel` and CIP on 41794) don't need the key, because they can only press joins, like panels on a real processor. Set `OpenVP:PanelsRequireKey=true` to protect `/ws/panel` too. CIP has no way to send a key, so keep port 41794 on a trusted network.
- `OpenVP:ApiKey=off` turns the key off. Only do that on a network you trust completely.

Traffic is plain HTTP. To use OpenVP across an untrusted network, put it behind a reverse proxy that adds HTTPS.

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
| `OpenVP:Autoload:slot1` | | Program file to load into slot 1 at startup |
| `Urls` | `http://0.0.0.0:8080` | Web console address |

## REST API

Every endpoint except `/api/info` and `/api/login` needs the access key (see [Security](#security)), for example `curl -H "Authorization: Bearer $(cat data/apikey.txt)" http://host:8080/api/slots`. An unknown slot number returns 404.

| Method | Path | |
|---|---|---|
| GET | `/api/slots` | All slots with state and devices |
| POST | `/api/slots/{n}/program` | Multipart `file` (+ sidecar files); loads and starts |
| POST | `/api/slots/{n}/start` · `stop` · `unload` | |
| PUT | `/api/slots/{n}/room` | `{"roomId":"ROOM2"}` sets the slot's Room ID (saved in `slot.json`); returns the slot, or 400 if the ID is invalid or another slot has it |
| GET / POST | `/api/slots/{n}/signals` | List; set with `{"name":"...","value":1}` |
| GET / POST | `/api/slots/{n}/step` | Step-through state; change with `{"enabled":true,"onlyBreakSignals":true,"breakSignals":["sig"]}` |
| POST | `/api/slots/{n}/step/next` · `step/run` | Release the paused change · turn stepping off |
| POST | `/api/slots/{n}/console` | `{"command":"ipt"}` |
| GET | `/api/slots/{n}/iptable` | IP IDs with each connected client: address, hostname, model, firmware, counters, details |
| POST | `/api/slots/{n}/iptable/{ipid}/identify` | Run reverse DNS and Crestron autodiscovery again |
| POST | `/api/slots/{n}/iptable/{ipid}/clients/{id}/login` | `{"username":"...","password":"...","port":22}`; SSH in and run read-only commands (credentials not stored) |
| GET | `/api/slots/{n}/compat` | Compatibility report |
| GET | `/api/slots/{n}/files?root=user&path=config` | List a folder in the slot's NVRAM (`root=nvram`) or User (`root=user`) folder |
| GET | `/api/slots/{n}/files/missing` | Files and folders the program looked for and didn't find, with the folder each belongs in (also in the `missing` field of the folder listing) |
| POST | `/api/slots/{n}/files?root=user&path=config` | Multipart upload into that folder; a file name can include sub-folders (`rooms/r1.xml`) |
| POST | `/api/slots/{n}/files/folder` · `files/rename` | `{"root":"user","path":"config","name":"rooms"}` creates `config/rooms` · renames `path` to `name` |
| GET | `/api/slots/{n}/files/download?root=user&path=config/rooms.json` | Download a file |
| DELETE | `/api/slots/{n}/files?root=user&path=config` | Delete a file, or a folder and everything in it |
| GET | `/api/unsupported` | Everything programs used that isn't supported yet, as the Markdown file (`?format=json` for JSON). See [Compatibility](#compatibility) |
| GET | `/api/log?slot=n` · `/api/devices` | |
| GET | `/api/metrics` | CPU, memory, threads and per-slot activity; `?history=false` leaves out the five-minute history. `/ws/metrics` sends the same once a second |
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

## License

MIT. See [LICENSE](LICENSE).
