# Performance plan: speed and efficiency first

This plan covers how OpenVP's virtual processor runs programs, where the time and memory go, and what to change, in order of payoff. Every number below was measured on this codebase (commit `293fd39`) unless it's marked *estimated*.

## In short

- **Most of the cost of a signal change goes on debugger plumbing that runs even with no debugger open.** Each change builds a trace record, reads the local clock, takes the step debugger's lock and looks up the signal's name. A prototype that skips this when nobody is listening, reuses boxed `true`/`false`, and skips no-op feedback writes is about 15 changed lines. It made compiled-program logic **1.8–2.2× faster** and cut allocation **6×** (144 → 24 bytes per change). Garbage collections in the benchmark went from 2 to 0.
- **Opening the debugger slows that slot's logic 6–7×**, because each change is turned into JSON on the slot's own thread.
- **The slot's event queue costs about 550 ns and 138 bytes per event.** A channel-based queue does the same job in about 100 ns and 2–4 bytes. Every panel press, timer and SIMPL+ output goes through this queue.
- **Timers leak.** Each Delay, One Shot or similar arms a `System.Threading.Timer` that the runtime keeps until the program stops. Pressing a retriggerable one-shot 100,000 times left 100,000 timers alive, taking 36 MB.
- **Shipped config logs every CIP packet.** `appsettings.json` turns CIP packet logging on, though the README says it's off. The hex string for the log is also built for every packet even when logging is off.
- **Program load and server start-up are already fast.** A 50,000-symbol program loads and starts in 75 ms, and the server answers in about 0.5 s. ReadyToRun made no measurable difference, so it's not in the plan.

Recommended order: Phase 0 (benchmark in the repo), then Phase 1 (quick wins, low risk, all measured), then Phase 2 (hot-path restructuring), then Phase 3 (only if the benchmark says it's still needed).

## Results

Measured on the same 4-vCPU VM as section 2 (.NET 8.0.34, Release, workstation GC), median of 3 runs of `tools/OpenVP.Bench`. The VM varies about ±30% between runs.

| Scenario | Baseline (section 2) | Now |
|---|---|---|
| NOT chain, depth 1,000 | 277 ns/change · 144 B/change · 2 GCs | **51 ns** · 0.1 B · 0 GCs (5.4×) |
| Fan-out 1 → 1,000 ANDs → 1,000 feedback joins | 439 ns/change · 169 B · 2 GCs | **136 ns** · 0.1 B · 0 GCs (3.2×) |
| Interlock, 200 outputs | 4.7 M sets/s · 52 B/set | **12.6 M sets/s** · 0.8 B/set (2.7×) |
| 48 wide gates of 64 inputs | not in section 2 (100 ns/eval just before 3.2) | **91 ns/eval** |
| NOT chain, debugger open | 1,708–2,064 ns/change · 735 B · 10 GCs | **610 ns** · 118 B · 1 GC (old JSON-on-slot-thread server: 1,877 ns) |
| Latency, back-to-back presses | p50 2.1 µs · p99 4.4 µs | p50 1.6 µs · p99 6.3 µs |
| Latency, slot idle 1 ms | p50 62 µs · p99 150 µs | p50 61 µs · p99 157 µs (unchanged; see 3.4) |
| SIMPL+ round trip, back-to-back / idle | 5.1 µs / 190 µs p50 | 6.1 µs / 184 µs p50 |
| 100,000 retriggers of a one-shot | 100,000 timers held, +36 MB | 1 timer, +1.7 MB |

| Item | Status |
|---|---|
| 0.1 bench and golden traces | Done |
| 1.1–1.7 quick wins | Done |
| 2.1 slot queue, 2.2 debugger/panels/CIP off the slot thread | Done |
| 2.3 typed storage, 2.4 flat fan-out | Done |
| 2.5 leaner SIMPL+, 2.6 `.smw` interpreter | Done |
| 3.1 one timer service per slot | Done (one OS timer per slot, due times kept to the millisecond so self-rearming timers don't drift; a delay of 0 is posted at once) |
| 3.2 compile the logic graph | Done as sealed fast-path nodes for NOT, AND/OR/NOR, Buffer, Analog/Serial Buffer, Interlock, Toggle and SR Latch (NOT chain 89 → 51 ns, interlock +50%). IL emission skipped: what is left is the queue, the solve loop and feedback, not dispatch |
| 3.3 running count for wide gates | Done |
| 3.4 optional spin | Done, off by default (`OpenVP:SlotSpinMicroseconds`). With 30 µs between events, p50 22 → 2 µs for about 7 points more of a core (10% → 17%); no gain when events are further apart than the spin (1 ms idle: ~60 µs either way); no CPU cost with no events |
| 3.5 once per logic wave | Deferred: changes event order, needs side-by-side traces from real hardware |

---

## 1. How the processor works

OpenVP's "hardware" is the runtime that stands in for a control processor:

| Part | Role | Key files |
|---|---|---|
| `Processor` | Owns the slots, the shared log, the global trace event and the EISC bus | `src/OpenVP.Core/Processor.cs` |
| `ProgramSlot` | One program: IP table, console commands, lifecycle, `Trace()` for the debugger | `src/OpenVP.Core/ProgramSlot.cs` |
| `SlotDispatcher` | **One thread per slot.** Every program event runs here, one at a time, from a `BlockingCollection` queue | `src/OpenVP.Core/SlotDispatcher.cs` |
| `JoinDevice` | One panel, XPanel or EISC per IP ID. Two `ConcurrentDictionary<JoinKey, object>` maps (to and from the device) plus change events | `src/OpenVP.Core/JoinDevice.cs` |
| `SignalStepper` | Step-through debugger. Every change goes through its lock and can block the thread that made it | `src/OpenVP.Core/SignalStepper.cs` |
| `LpzRuntime` | Runs compiled SIMPL Windows programs. Values in `object[]`, fan-out lists per signal, FIFO worklist solve | `src/OpenVP.Lpz/LpzRuntime*.cs`, `Primitives*.cs` |
| `SimplRuntime` | `.smw` interpreter. Same design as `LpzRuntime` but keyed by `Dictionary<int, …>` | `src/OpenVP.Simpl/SimplRuntime.cs` |
| SIMPL+ (`SplusObject`, `SplusNode`) | Each event handler runs on a thread-pool thread and takes the module's lock (the "baton"). Outputs are posted back to the slot thread | `src/OpenVP.Splus/SplusObject.cs`, `src/OpenVP.Lpz/SplusNode.cs` |
| SIMPL# host | The user's .NET program in its own load context. Device join changes are posted to the slot thread | `src/OpenVP.SimplSharp/Devices.cs`, `src/OpenVP.SimplSharpHost/` |
| Transports | CIP over TCP (one packet and one write per join) and browser panels (one JSON object and one frame per join) | `src/OpenVP.Panels/CipServer.cs`, `WebPanelSession.cs` |
| Debugger stream | `/ws/trace`: subscribes to `Processor.Traced` and turns each change into JSON on the calling thread | `src/OpenVP.Server/OpenVpServer.cs` |

### The hot path: one panel press in a compiled program

```
CIP / WebSocket thread
  JoinDevice.ReceiveInput ─ coerce (boxes), dictionary write, InputReceived event
  DeviceNode.OnDeviceInput ─ builds "IPIDxx" string, SlotDispatcher.Post(closure)
        │  thread hop: BlockingCollection, ~550 ns + 138 B
        ▼
slot thread
  LpzRuntime.Drive ─ Coerce (boxes again), _pending.Enqueue, Solve()
  Solve loop, once per signal change:
    dequeue → compare old and new (virtual Equals) → store in object[]
    ProgramSlot.Trace(NameOf(sig), …)               ◄── runs every change, debugger or not
      ├─ new TraceEvent, DateTimeOffset.Now
      ├─ Processor.Traced  (debugger open: JSON-serialize here, ~1.2 µs + 400 B)
      └─ SignalStepper.OnChange (takes lock; blocks while stepping)
    for each (node, pin) in fanout[sig]:
      Primitive.OnInput → ctx.Set(pin, bool) (boxes) → Drive → enqueue
    DeviceNode.OnInput → JoinDevice.SetFeedback
      (dictionary write even when the value is unchanged)
      → FeedbackChanged → CIP: semaphore + one socket write per join
                        → web panel: new JsonObject + one frame per join

Timers:  one System.Threading.Timer per Delay/One Shot, kept in a list → Post → Drive
SIMPL+:  OnInput → ThreadPool item per handler → baton → handler → Output → Post → Drive
```

The solve is a FIFO worklist, not recursion, so deep logic can't overflow the stack. Each primitive runs once per input change, not once per logic wave.

---

## 2. Baseline measurements

**Setup:** 4-vCPU cloud VM, .NET 8.0.34, Release build, workstation GC. Throughput covers everything from the panel input to settled logic, through a real `Processor` and slot. The programs are synthetic `.lpz` files built the same way the tests build them, with no `.sig` file. Throughput figures are the median of three runs. This VM varies ±30% between runs, the interlock scenario most of all.

### Throughput and allocation

| Scenario | Baseline | Phase 1 prototype (items 1.1–1.3) |
|---|---|---|
| NOT chain, depth 1,000, 2,000 presses | 277 ns/change · 3.6 M changes/s · 144 B/change · 2 GCs | **153 ns** · 6.5 M/s · **24 B** · 0 GCs |
| Fan-out 1 → 1,000 ANDs → 1,000 panel feedback joins | 439 ns/change · 2.3 M/s · 169 B/change · 2 GCs | **199 ns** · 5.0 M/s · **24 B** · 0 GCs |
| Interlock with 200 outputs, 4,000 presses (mostly no-op sets) | 4.7 M sets/s · 52 B/set | 6.0 M sets/s · 26 B/set |
| NOT chain with a debugger window open (server's JSON subscriber) | 1,708–2,064 ns/change · 735 B/change · 10 GCs | unchanged (needs item 2.2) |

### Latency (one press, then wait for the feedback)

| Path | p50 | p99 |
|---|---|---|
| Compiled logic, back-to-back presses | 2.1 µs | 4.4 µs |
| Compiled logic, slot idle 1 ms between presses | 62 µs | 150 µs |
| SIMPL+ `CHANGE` round trip (panel → module → panel), back-to-back | 5.1 µs | 30 µs |
| SIMPL+ round trip, slot idle 1 ms between | 190 µs | 851 µs |

Waking an idle slot thread costs about 60 µs on this VM, and SIMPL+ adds two more thread hops. None of these is noticeable to a person. They matter for throughput when a device streams data.

### Fixed costs per operation (micro-benchmarks)

| Operation | Time | Allocated |
|---|---|---|
| `slot.Trace()` with no debugger open | 140 ns | 64 B |
| `DateTimeOffset.Now` (inside every trace) | 53 ns | 0 |
| JSON-serialize one trace event (debugger open) | 1,244 ns | 400 B |
| SIMPL# trace name `$"IPID{id:X2}.in.{key}"` (every feedback set) | 322 ns | 109 B |
| `JoinDevice.SetFeedback`, value changed | 255 ns | 48 B |
| `JoinDevice.SetFeedback`, value unchanged | 160 ns | 48 B |
| `SigValue.Coerce(Digital, bool)` | 27 ns | 24 B |
| `SlotDispatcher.Post`, cross-thread | 533 ns | 42 B (+ closure) |
| Queue hand-off, current `BlockingCollection` design | 550–595 ns | 136–138 B |
| Queue hand-off, `Channel` + struct work item | 98–121 ns | 2–4 B |

### Other measurements

| | Result |
|---|---|
| Retriggerable one-shot pressed 100,000 times | **100,000 timers held, +36 MB**, released only when the program stops |
| Load and start a 5,000 / 50,000-symbol `.lpz` | 13 ms / 75 ms; live heap +3.4 / +34 MB |
| Server start to first `/api/info` response, JIT vs ReadyToRun | 454–576 ms vs 410–554 ms (no real difference) |
| SIMPL+ burst of 1,000 `CHANGE`s, last value wins? | Yes, in 20 of 20 rounds (no reordering seen, though nothing guarantees it) |

### Not measured

- Real touch panels over CIP: no hardware was available.
- Real `.lpz` programs. With a `.sig`, each change does a dictionary lookup for the name instead of building a `#123` string. The trace overhead is the same order either way.
- Disk stalls from NVRAM writes.

### Reproducing

The harness for these numbers was a throwaway console app kept outside the repo. Phase 0 adds it to the repo. The scenarios are:

- The programs above, built with the same `Record(...)` helper as `tests/OpenVP.Tests/LpzTests.cs`.
- Throughput: post N presses with `JoinDevice.ReceiveInput`, end with `slot.Dispatcher.Invoke(() => {})` as a barrier, and divide the elapsed time and `GC.GetTotalAllocatedBytes(true)` by the number of signal changes.
- Latency: timestamp the `ReceiveInput` call and the matching `FeedbackChanged` event.

---

## 3. Bottlenecks, ranked

| # | Bottleneck | Where | Evidence |
|---|---|---|---|
| B1 | Full trace work on every signal change, debugger or not: `TraceEvent` allocation, `DateTimeOffset.Now`, stepper lock, signal-name lookup. SIMPL# also builds a string per feedback set | `ProgramSlot.cs:74-78`, `LpzRuntime.cs:235`, `SimplRuntime.cs:171`, `Devices.cs:309,314`, `LpzRuntime.cs:196-197` | 140 ns + 64 B per change (322 ns + 109 B more on SIMPL# feedback). Removing it accounts for most of the 1.8–2.2× |
| B2 | Values boxed twice per change: once at `ctx.Set(pin, bool)` and again in `Coerce` (`bool b => b` re-boxes). `AreEqual` is a virtual `Equals` | `Joins.cs:49,68`, `LpzRuntime.cs:37,205` | 144 → 24 B/change with cached boxes |
| B3 | Open debugger serializes JSON on the slot thread and sends one WebSocket frame per change | `OpenVpServer.cs:270,298` | 6–7× slower logic while a debugger is open |
| B4 | Slot event queue: `BlockingCollection`, a record and a closure per event | `SlotDispatcher.cs:16,37` | 550 ns + 138 B vs 100 ns + 3 B |
| B5 | Every timer is kept until the program stops | `LpzRuntime.cs:254-257`, `SimplRuntime.cs:188-189` | 100,000 retriggers → 100,000 timers, +36 MB |
| B6 | `SetFeedback` writes the dictionary even when the value is unchanged | `JoinDevice.cs:61-65` | 160 ns + 48 B per no-op set (interlocks and buffers do many) |
| B7 | CIP: packet logging on in shipped config, hex string built for every packet regardless, one awaited write per join, Nagle's algorithm left on | `appsettings.json:7`, `CipServer.cs:298,295,204` | Config and code reading. Nagle plus delayed ACKs can hold small packets for tens of ms (*estimated*, needs a capture) |
| B8 | Web panels: a `JsonObject` built on the slot thread for each feedback join, and one frame per join | `WebPanelSession.cs:87,99` | About the same cost as B3 for each feedback change |
| B9 | SIMPL+: thread-pool hop per handler, `handlers.ToArray()` per event, one `Post` per output. Each module parks a pool thread in `Thread.Sleep` for 2 s at start, hence `SetMinThreads(256)` | `SplusObject.cs:409-410,429,40`, `SplusNode.cs:119` | 190 µs p50 / 851 µs p99 idle round trip vs 62/150 µs for plain logic |
| B10 | NVRAM: the file is written while holding the lock the slot thread needs, and the write isn't atomic | `NvramStore.cs:40,47-53` | Code reading. A slow disk stalls the logic. A crash mid-write leaves invalid JSON, so the load starts empty and **every saved value is lost** |
| B11 | Per-event string building in posted lambdas (`$"IPID{id:X2}"`) | `LpzRuntime.cs:480`, `LpzRuntime.Io.cs:124`, `SimplRuntime.cs:385` | ~100 B per press |
| B12 | Interpreter overhead: `object` values, interface calls through `IPrimitiveContext`, `List<(Node,int)>` fan-out, AND/OR scan every input on every change | `LpzRuntime.cs`, `Primitives.cs:50,63` | What's left of the ~150 ns/change after Phase 1 |
| B13 | Idle slot thread takes ~60 µs to wake | `SlotDispatcher.cs` (kernel wait) | Matters only for tight request/response streams |

---

## 4. The plan

Each item gives what to change, the expected gain, the risk to correctness and the effort. Two rules apply to every item:

- **Signal order:** changes must settle in the same order as today. The debugger's step-through must keep working.
- **Verification:** `dotnet run --project tests/OpenVP.Tests` plus the benchmark (Phase 0).

### Phase 0: measure in the repo (S)

**0.1 Add `tools/OpenVP.Bench`.** This is the harness used for this plan: the scenarios in section 2, printing ns/change, B/change, GC count and latency percentiles.
- **CI:** run it in small mode as a non-blocking job, so regressions show up.
- **Golden traces (S–M):** add a test that records the exact sequence of `(signal, value)` changes for the synthetic programs and for `samples/`. Every later phase must reproduce that sequence exactly. This is how "same order as today" gets proven, not argued.

### Phase 1: quick wins (all low risk, mostly measured)

| # | Change | Gain | Order changes? | Effort |
|---|---|---|---|---|
| 1.1 | **Skip tracing when nobody's listening.** Give `ProgramSlot` a cheap `TraceWanted` flag (true when a trace subscriber exists for *this slot* or stepping is on, read without a lock). Skip `Trace()`, `NameOf()` and the SIMPL# name strings when it's false. Move trace subscriptions from `Processor` to `ProgramSlot`, so a debugger on slot 1 doesn't slow slots 2–10 | Measured, together with 1.2–1.3: **1.8–2.2×** logic throughput, 6× less allocation, no GCs | No. With the debugger open, behavior is identical | S |
| 1.2 | **Cached boxes for `true`/`false`.** Return them from `SigValue.Coerce`, and check `ReferenceEquals` first in `AreEqual` | Measured (part of 1.1) | No | S |
| 1.3 | **`SetFeedback` returns early when the value is unchanged** (serials still always fire, as today) | Measured (part of 1.1) | No. (As first built it left joins never moved off their default out of the snapshot, so a panel that reconnected after a reload kept the old program's lit buttons; the first set of a join is now always stored.) | S |
| 1.4 | **Fix the timer leak.** Take each timer out of `_timers` when it fires or is disposed, in both runtimes. A small wrapper that removes itself on `Dispose` is enough | Measured: 100,000 dead timers (36 MB) → 0 | No | S |
| 1.5 | **CIP hygiene.** Set `CipLogPackets` back to `false` in `appsettings.json` (or update the README if on is intended). Build the hex string only when `LogPackets` is true. Set `NoDelay = true` on accepted CIP sockets | *Estimated*: removes a log line, a hex string and a console-JSON message per packet. Nagle off removes possible ~40 ms stalls on bursts of small feedback packets | No | S |
| 1.6 | **NVRAM off the lock, atomic write.** Copy the data under the lock, write outside it to `nvram.json.tmp`, then `File.Move(tmp, path, overwrite: true)` | *Estimated*: no disk I/O on the logic thread's path. Saved values survive a crash or power loss | No | S |
| 1.7 | **Precompute per-device strings** (`"IPID03"`, `"S-12 so5"`) once at attach instead of in every posted lambda | ~100 B and ~100 ns per press | No | S |

Phase 1 needs about 50 changed lines in total. 1.1–1.3 were prototyped and measured for this plan, then reverted. The repo has no performance code changes yet.

### Phase 2: hot-path restructuring (medium effort)

| # | Change | Gain | Order changes? | Effort / risk |
|---|---|---|---|---|
| 2.1 | **New slot queue.** Replace `BlockingCollection<Item>` with `Channel<WorkItem>` (unbounded, single reader). Make `WorkItem` a struct holding a static delegate and its state, so callers like `DeviceNode` stop allocating closures. Keep `Invoke`, `Quiesce` and the shutdown modes exactly as they are | Measured in isolation: **5× cheaper hand-off** (≈550 → ≈100 ns), **~40× less allocation** per event. Covers every press, timer tick, SIMPL+ output and socket event | No. Still FIFO on one thread | M / low–medium (shutdown paths need care; tests cover them) |
| 2.2 | **Debugger and panels off the slot thread.** Put the raw `TraceEvent` in the channel and serialize in the send loop. Send **batches** (one JSON array per frame, flushed every few ms or N events). Do the same for `/ws/trace` log lines and for `WebPanelSession` feedback (store `(JoinKey, value)`, build JSON in `SendLoop`). For CIP, merge pending feedback into one write per burst | *Estimated*: an open debugger drops from ~1.7 µs to ~0.1–0.2 µs per change on the slot thread (just the enqueue), so debugging no longer distorts timing 7×. Far fewer frames and syscalls on page flips | No. Logic isn't affected | M / low (`debugger.html` and `panel.html` must accept arrays) |
| 2.3 | **Typed signal storage in the LPZ engine.** Store values in typed arrays (`bool[]`, `ushort[]`, `string[]`) indexed by signal, instead of `object[]`. Add typed `Set(pin, bool)`, `Set(pin, ushort)` and `Set(pin, string)` overloads on `IPrimitiveContext`. Make the pending queue a struct with no boxing. Compare values with typed `==` | *Estimated* 1.3–2× more on top of Phase 1, and **zero allocation per change** for digital and analog | No, if done mechanically. The golden traces from 0.1 prove it | M / medium (touches all 56 primitive classes, mechanical) |
| 2.4 | **Flat fan-out.** Turn the per-signal `List<(Node,int)>` into one compact array with an offset per signal, built once at load | *Estimated* 10–20% (better cache use). Measure after 2.3 | No | S–M / low |
| 2.5 | **Leaner SIMPL+.** (a) Queue each module's outputs and post **one** drain per burst. The drain calls `Drive` once per output in order, so each output still settles before the next, as today. (b) Replace the per-module `Thread.Sleep(50)…Sleep(1950)` pool thread with a `Timer`, then check whether `SetMinThreads(256)` is still needed. (c) Keep handler lists as copy-on-write arrays instead of `ToArray()` per event | *Estimated*: 1 post per burst instead of N. At start of a 230-module program, no more 230 pool threads asleep for 2 s. Less garbage per event. The thread-per-event model (pool hop + baton) stays because SIMPL+ semantics need it | (a): only across sources. Today a panel press can land between two outputs of the same module, and afterwards it can't. Same-module order is unchanged | M / medium |
| 2.6 | **Port Phase 1 and 2.3 to the `.smw` interpreter** and swap its `Dictionary<int, …>` for arrays sized to the largest handle | *Estimated* ≥ the LPZ gains. Lower priority now that `.lpz` runs every program | No | M / low |

### Phase 3: longer term (only if the benchmark says it's still worth it)

| # | Change | Gain | Order changes? |
|---|---|---|---|
| 3.1 | **Timer wheel per slot** at 10 ms resolution (the unit SIMPL times are compiled in). One OS timer per slot instead of one per Delay or One Shot. Callbacks due on the same tick go in one post, each still followed by its own solve. Replaces 1.4 | *Estimated*: much less timer overhead for timer-heavy programs (oscillators, clocks, debounce); no per-timer allocation | No (one solve per callback, as today) |
| 3.2 | **Compile the logic graph.** After load, give each node a specialized delegate (an AND that reads `bool[]` directly), or emit IL with `DynamicMethod` | *Estimated* 2–5× for pure logic. Only after 2.3, and only if the interpreter still dominates | No, with golden traces |
| 3.3 | **Wide AND/OR keep a count of high inputs** instead of scanning every input on every change | O(1) instead of O(n) for wide gates | No |
| 3.4 | **Optional short spin before the slot thread blocks** (config switch, off by default) | Cuts the ~60 µs wake. Costs CPU while idle, so only for tight request/response streams | No |
| 3.5 | **Evaluate each symbol once per logic wave** instead of once per input change. Removes glitch pulses and repeated work | Possibly large for wide, busy logic | **Yes: changes how many changes happen and in what order.** Do it only if side-by-side traces from a real processor (a roadmap item) show that's how the hardware behaves |

### Ruled out (measured)

- **ReadyToRun for the server:** no start-up difference (both ~0.45–0.55 s). The .NET framework is already precompiled, and OpenVP's own code is small.
- **Load-time work:** a 50,000-symbol program loads and starts in 75 ms.
- **GC mode tuning (server GC, concurrent, etc.):** after Phase 1 the logic path produces no garbage, so the GC mode doesn't matter. Revisit only if a real program shows GC pauses.

---

## 5. Expected result

- **After Phase 1:** compiled logic runs about **2× faster** with **6× less garbage** and no GCs under load. The timer leak is gone, and CIP and NVRAM stop doing I/O and string work on the hot path. That's about a day of work, measured.
- **After Phase 2:** every event hand-off is about 5× cheaper, an open debugger stops slowing the program 7×, and digital/analog logic allocates nothing per change. *Estimated* 3–4× total over today for logic-heavy programs.
- **Phase 3** is a set of options, each to be justified by benchmark numbers first.

## 6. Open questions

1. **Order within a logic wave on real hardware.** Does a real processor propagate breadth-first like OpenVP's FIFO? Does it evaluate a symbol once per wave? This decides whether 3.5 is allowed, and matters for the golden traces.
2. **When SIMPL+ outputs reach the logic on real hardware:** immediately, or when the module's thread yields (`ProcessLogic`, `Delay`, end of event)? This affects 2.5(a). Today OpenVP posts each output separately.
3. **CIP packet logging:** is it on in `appsettings.json` on purpose (for the CIP bring-up) or left over? The README says the default is off.
4. **Real workload mix:** these numbers come from synthetic programs. A few real `.lpz` files (with `.sig`, ideally the 230-module one) run through the benchmark would confirm the ranking, especially SIMPL+ cost and timer counts.
5. **Target machines for the desktop app:** low-end PCs make idle CPU (3.4) and per-slot memory more important.
