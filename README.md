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

Open **http://localhost:8080**. From another machine you'll be asked for the access key, which the server prints when it starts (see [Security](#security)). Pick a slot, choose a `.cpz`, `.dll` or `.smw` file, and click **Load & start**. Click **open test panel** next to a device to get a browser panel at that IP ID.

### Desktop app (Windows)

**`OpenVP.exe`** is OpenVP as a desktop app (Electron), in one file: nothing to install, no .NET needed, no console window. Run it and the web console opens in its own window. Test panels and the debugger open in their own windows too. Closing the main window stops OpenVP. Your data and the log (`openvp.log`) are kept in `%APPDATA%\OpenVP`. The first time it runs, Windows may ask whether to let it through the firewall; allow it if you want panels or other machines to connect.

Download the latest from the [Releases](https://github.com/qureshiumar1983-eng/openVirtualProcessor/releases) page: every push to `main` builds a new `OpenVP.exe`, checks that it starts, and publishes it there.

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
| `OpenVP.Simpl` | `.smw` parser and interpreter, logic symbol library |
| `OpenVP.Panels` | Browser panel WebSocket protocol and the CIP server |
| `OpenVP.Server` | The app: web console, REST API, debugger stream, autoload |

### SIMPL# programs

A compiled program references Crestron's assembly names. Each slot gets its own .NET load context in which those names resolve to **facades** that forward to OpenVP's API. The program itself is never changed. Every program event (panel presses, `CTimer` callbacks, socket data) runs on that slot's own thread, one at a time.

Before running, OpenVP reads the program's metadata and lists every Crestron type and member it uses that isn't implemented yet. Click **Compatibility** in the console, or check the log.

Implemented so far: `CrestronControlSystem`, `InitializeSystem`, program status events, `CrestronConsole` (print, console commands), `ErrorLog`, `CTimer`, `CrestronInvoke`, `CrestronThread.Thread`, `CCriticalSection`/`CMutex`/`CEvent`, `InitialParametersClass`, `TCPClient`, `BasicTriList` with all six sig collections and `SigChange`, touch panel and XPanel classes, and local EISC (`ThreeSeriesTcpIpEthernetIntersystemCommunications`).

### SIMPL Windows programs

OpenVP reads the `.smw` source, not the compiled `.lpz`, so nothing of Crestron's runtime is decompiled. The file is a list of blocks: `Sm` symbols point at `Sg` signals by handle (`I1=4` = input pin 1 is wired to signal 4), and pins are grouped digital, analog, serial (`n1I`, `n2I`, `n3I`). When a signal changes, every symbol listening to it runs and its outputs drive further signals until the logic settles. The `1` signal goes high at start, as it does on a processor.

Symbols implemented: AND, OR, XOR, NOT, Buffer (digital/analog/serial), Interlock, Toggle, SR Latch, One Shot, Delay, Analog Initialize, Equate, Serial Send. Device symbols (panels, XPanels, EISC) map their pins to joins.

**Two things the format doesn't make clear, so you may need to help:**

1. **Symbol codes.** Symbols are identified by a numeric `SmC` code, and Crestron doesn't publish the list. OpenVP matches by code when it knows it, otherwise by the symbol's default name ("AND", "Interlock"…). To teach it codes, add `MyProgram.symbols.json` next to the program (or `data/symbols.json` for all programs): `{ "1234": "AND", "1240": "Interlock" }`.
2. **IP IDs.** Where an IP ID can't be found in the file, set it in `MyProgram.openvp.json`, keyed by symbol handle: `{ "ipids": { "21": "03" } }`.

The fastest way to close these gaps is real `.smw` files. If you can share one (even a trivial program with one of each symbol), open an issue.

### Panels

- **Browser / CH5:** `ws://host:8080/ws/panel?ipid=03&slot=1`, JSON `{"t":"b","j":1,"v":true}` (`b` digital, `n` analog, `s` serial). `wwwroot/openvp-crcomlib.js` gives a page `CrComLib.publishEvent` / `subscribeState` for numeric join names, so simple CH5 projects can point at OpenVP. `panel.html` is a ready-made test panel.
- **Real panels over CIP (experimental):** listener on TCP 41794. CIP isn't publicly documented; the framing follows community write-ups and is unit tested against itself, **not yet against real hardware**. The shipped `appsettings.json` sets `OpenVP:CipLogPackets=true`, which logs every packet; compare with a Wireshark capture of a panel talking to a real processor. All encodings live in `Cip` in `CipServer.cs`.

A panel can connect before its program loads. It binds to the IP ID and comes online when the program registers it.

### IP table

The **IP table** on the console's main page shows the selected slot's IP IDs, one row for each connected client. Each row starts with who the client is, then its address, IP ID, type, status, how long it has been connected, and how many joins went each way. An IP ID with nobody connected shows when it was last seen. Click a row to see everything known about that client and IP ID. The table refreshes every 2 seconds. The `ipt` console command prints the same list.

How OpenVP works out who a client is:

- **Without credentials, automatically:** it uses the remote address and port, and reverse DNS for the hostname. A client on the OpenVP machine itself is named after this computer. For CIP clients it also sends **Crestron autodiscovery** (UDP 41794). Crestron hardware answers with its hostname, model, firmware and MAC, so a row reads e.g. *Crestron touch panel (TSW-1070)*. XPanel and the Crestron apps don't answer, so a CIP client with no reply is listed as *Software client (XPanel or Crestron app?)* under its computer's hostname. The CIP connect request bytes are kept as well, to help tell client types apart later. Autodiscovery is **experimental**, like CIP: it follows community write-ups and hasn't been tested on every device. **Re-identify** runs these checks again.
- **Browser and CH5 panels:** browser and OS come from the User-Agent, and pages using `openvp-crcomlib.js` also report their screen size, platform, language, time zone and round-trip time.
- **EISC:** the row names the slot and program on the other end of the link.
- **Log in & read…** (optional): type the device's SSH username and password. OpenVP runs only read-only console commands (`hostname`, `ver`, `uptime`, `ipconfig`, `ipt`, `time`), shows what they return, and fills in hostname, model, firmware and MAC from them. The credentials are used for that one login and are never stored or logged.

Metrics for each client: connected since, last message, messages and bytes in and out, joins in and out, heartbeats (and the interval between them for CIP), round-trip time (browser panels), packets waiting to be sent (CIP) and errors. For each IP ID: registered, last online and offline, connections since registered, inputs received and feedback sent.

### Debugger

Click **Open debugger** in the console (or go to `/debugger.html?slot=N`). It's laid out like the classic SIMPL Debugger:

- **Signal list** (left): every signal in the slot with its live value. Search, filter by type, and tick **T** to choose which signals go to the trace. Click a column header to sort.
- **Trace** (right): one row per change with time, signal, value and what caused it. **Stream** (F5) pauses and resumes it, **Clear** empties it, **Save…** writes it to a text file. You can trace all signals or only the ticked ones.
- **Display options**: time as absolute, relative or delta; analogs as decimal (`500d`), hex (`01F4h`) or percent; serials as ASCII with `\x0D`-style escapes, or hex.
- **Step-through** (for logic wave problems): turn on **⏸ Step** and the slot's logic pauses on every signal change, right after the new value is set and before anything wired to that signal reacts. **Next** (F10) lets that one change through and stops at the next; **Run** (F8) turns stepping off. Set **Break on** to *marked signals* and tick the **⏸** column (or press F9 on the selected signal) to pause only on the signals you care about; everything else runs through. The bar under the toolbar shows where it's paused, and that signal is highlighted in both lists. **Back** (F7) shows an earlier step: every signal's value as it was then (values that have changed since are dotted), with that change highlighted in both lists. The program itself stays paused where it is; logic can't be run backwards. **Next** walks forward through those steps to the live one, then carries on stepping the program. The history covers the steps this debugger window has seen. Forcing a signal while paused queues it until you step on. Stopping or unloading the slot releases the pause.
- **Forcing signals**: right-click a signal for Set / Clear / Pulse / Toggle (digital) or to enter a value, double-click a digital to toggle it, or use the **Force** bar at the bottom. Analog values can be typed as `500`, `1F4h` or `50%`, and serial values can include `\x0D`, `\r` or `\n`.

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
| `OpenVP:Autoload:slot1` | | Program file to load into slot 1 at startup |
| `Urls` | `http://0.0.0.0:8080` | Web console address |

## REST API

Every endpoint except `/api/info` and `/api/login` needs the access key (see [Security](#security)), for example `curl -H "Authorization: Bearer $(cat data/apikey.txt)" http://host:8080/api/slots`. An unknown slot number returns 404.

| Method | Path | |
|---|---|---|
| GET | `/api/slots` | All slots with state and devices |
| POST | `/api/slots/{n}/program` | Multipart `file` (+ sidecar files); loads and starts |
| POST | `/api/slots/{n}/start` · `stop` · `unload` | |
| GET / POST | `/api/slots/{n}/signals` | List; set with `{"name":"...","value":1}` |
| GET / POST | `/api/slots/{n}/step` | Step-through state; change with `{"enabled":true,"onlyBreakSignals":true,"breakSignals":["sig"]}` |
| POST | `/api/slots/{n}/step/next` · `step/run` | Release the paused change · turn stepping off |
| POST | `/api/slots/{n}/console` | `{"command":"ipt"}` |
| GET | `/api/slots/{n}/iptable` | IP IDs with each connected client: address, hostname, model, firmware, counters, details |
| POST | `/api/slots/{n}/iptable/{ipid}/identify` | Run reverse DNS and Crestron autodiscovery again |
| POST | `/api/slots/{n}/iptable/{ipid}/clients/{id}/login` | `{"username":"...","password":"...","port":22}`; SSH in and run read-only commands (credentials not stored) |
| GET | `/api/slots/{n}/compat` | Compatibility report |
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
- More SIMPL symbols: Analog Ramp, Serial I/O, Analog/Serial conversions, Stepper, Counter…
- Confirm SIMPL symbol codes and IP ID storage from real `.smw` files
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
