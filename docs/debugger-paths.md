# Debugger: signal paths, causality and the program graph

This is the contract between the server and the debugger pages (`debugger.html`, `paths.html`,
`debugger-wave.js`). Both sides build against it.

## 1. Program graph: `GET /api/slots/{n}/graph`

Returns the wiring of the program loaded in slot `n`. `404` with `{ "error": "..." }` when the slot
has no program or its runtime cannot describe its wiring (SIMPL# programs).

```json
{
  "kind": "SIMPL Windows",               // runtime Kind
  "symbols": [
    {
      "id": "S-2.1.6.1:S-5",             // unique; equals TraceEvent.symbol for changes this symbol drives
      "name": "AND",                      // human type name (SIMPL symbol name, or "type 12" if unknown)
      "path": "S-2.1.6.1:S-5",            // hierarchical position; module path before ':' (may equal id)
      "group": "S-2.1.6.1",               // module / device the symbol sits in ("" at top level)
      "device": false,                    // true for hardware: panels, IP devices, cards, ports
      "inputs":  ["Sig_A", "Sig_B"],      // signal names, in pin order; constants "0"/"1" included
      "outputs": ["Sig_Out"]
    }
  ],
  "signals": [ { "name": "Sig_A", "kind": "Digital" } ]   // kind: Digital | Analog | Serial
}
```

Signal names are exactly the names used by `GET /api/slots/{n}/signals` and by trace events.
Unnamed internal signals keep whatever name the runtime already shows for them (e.g. `#123`).
`inputs`/`outputs` keep pin order, so an unwired pin is an empty string `""`: skip those. Symbols with
nothing wired are left out. SIMPL Windows symbol ids are `S<handle>`; compiled programs use the record path.

### Compact form, caching and compression

`GET /api/slots/{n}/graph?compact=1` returns the same JSON shape with these differences:

- Pins that are an empty string (unwired) or the constants `"0"` and `"1"` are removed from each symbol's
  `inputs` and `outputs`. The remaining pins keep their order, but a pin's position in the array is then
  no longer its pin number.
- A symbol left with no pins is omitted.
- `signals` is unchanged.

Without `compact=1` the response is as described above. For a large program most pins are empty, so the
compact form is much smaller (the biggest sample, `Sunlife - Toronto - York 1 - 22nd - BES - Main.smw`,
goes from 10.5 MB to 4.7 MB; with gzip 0.5 MB).

The wiring of a loaded program does not change while it is loaded. The server serializes each form once,
the first time it is asked, and keeps the bytes until the slot's program is loaded again, replaced or
unloaded. Stopping and starting a program keeps them.

Both forms carry a strong `ETag` (a hash of the bytes; the two forms have different tags) and
`Cache-Control: no-cache`, so a client may keep the response but must ask before using it. A request with
a matching `If-None-Match` gets `304 Not Modified` with no body. A different program, even one with the
same name, gives a different tag.

The server compresses responses with brotli or gzip (fastest level) when the request sends
`Accept-Encoding`, over http and https. This covers JSON (including `/graph` and `/signals`), html, js and
css. The WebSocket endpoints, program downloads and other binary files are not compressed. Static pages
are sent with an `ETag` and `Cache-Control: no-cache`, so an edited page is picked up on the next load and an
unchanged one costs a 304.

## 2. Causality on trace events

`TraceEvent` gains four fields, sent on `/ws/trace` as camelCase JSON next to the existing
`t, time, slot, signal, kind, value, source`:

| field    | type            | meaning |
|----------|-----------------|---------|
| `symbol` | string \| null  | id of the graph symbol whose output produced this change; `null` when the change came from outside the logic (panel, debugger force, device, timer, start-up) |
| `cause`  | string \| null  | name of the signal whose change made `symbol` act; `null` when `symbol` is null or unknown |
| `depth`  | int             | hops from the outside event that started this burst: 0 for an external change, `cause.depth + 1` otherwise |
| `chain`  | long            | id of the burst: a new value for every external change (depth 0); every change it causes carries the same id |

How the runtimes fill them: while `Solve()` dispatches signal `s` to an edge's node, the runtime
remembers `(node, s, depth(s), chain(s))`. Any `Enqueue` made during that call records the node's
symbol id, `s`, `depth+1` and the chain. An `Enqueue` made with no node being dispatched (inputs,
timers, deferred actions, start-up) starts a new chain at depth 0 with `symbol = null` (the chain id is assigned when the change is processed).

Work deferred to a later wave or a timer counts as external (new chain). That's a known limit.

These fields are only computed while someone is listening (`ProgramSlot.TraceWanted`), so the
cost when no debugger is open stays the same as before.

## 3. Pages

- `debugger.html`: signal list + trace + step mode. Links to `paths.html?slot=N&signal=NAME`.
- `paths.html`: "maze" view. Pick a start signal, see everything reachable from it (forward
  through the graph), and watch live changes light up the route taken, with fading trails and
  loop/oscillation markers.
- `debugger-wave.js`: logic-analyzer waveform panel used by `debugger.html`:

```js
// Global, no modules, no dependencies.
window.WaveView = {
  create(container, opts)  // returns a view; opts: { format(kind, value) => string }
};
// view methods:
view.setSignals([{ name, kind, value }])  // lanes, in order; keeps history for signals already shown
view.push({ time /*ms epoch*/, signal, kind, value })  // a change; ignored if the signal has no lane
view.clear()
view.setCursor(timeMs | null)            // optional highlight (e.g. step-back position)
view.destroy()
```
