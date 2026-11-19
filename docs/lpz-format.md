# Compiled SIMPL Windows programs (.lpz): format notes

These notes come from comparing 101 compiled programs (3-Series and 4-Series, CP3/CP3N/PRO3/AV3/MPC3, DMPS3, CP4/CP4N/RMC4) with their sources. Nothing here comes from Crestron documentation or code. Field names are our own labels.

## Container

A `.lpz` is a zip file:

| File | Contents |
|---|---|
| `boot.bt` | INI `[LG_BOOT_DATA]`: `BinFileName`, `SMWFileName`, `NumberSymbols`, `NumberSignals`, `NumberDigitalSigs`/`AnalogSigs`/`SerialSigs`, `RackType`, compiler and database versions |
| `<name>.bin` | Compiled logic (below) |
| `<name>.dip` | INI `[IPTable]`: `id0=03`, `addr0=127.0.0.1`, … |
| `<name>.dsc` | One line per device: slot, IP ID, record index, category, model |
| `<name>.cloud` | Device list with programmer comments (for XiO Cloud) |
| `<name>.rte`, `.cdm`, `.spp` | Binary routing, device-map and SIMPL+ parameter tables (not decoded yet) |
| `*.dll` | SIMPL+ modules compiled to .NET (Compact Framework 3.5 style), plus SIMPL# libraries (`.clz` contents) they use |
| `SplusManagerApp.exe`, `SplusLibrary.dll`, `SplusObjects.dll`, `SimplSharp*.dll` | Crestron's runtime. OpenVP doesn't use these |
| `<name>_archive.zip` | Optional: the program's sources (`.smw`, `.umc`, `.usp`, `.clz`). Present in 41 of the 101 programs |

Every program checked used the same layout regardless of target series.

## Logic file (.bin)

A flat sequence of records. Each one is a compiled symbol. The record count equals `boot.bt` `NumberSymbols` in every program checked.

```
record  := u32 size
           i32 type          primitive type (see below)
           u32 inputsAt      byte offset of inputs within the record
           u32 outputsAt     byte offset of outputs within the record
           field*            from byte 16 up to outputsAt
           u32 output*       signal indices, (inputsAt - outputsAt) / 4 of them
           u32 input*        signal indices, (size - inputsAt) / 4 of them
field   := u16 n, u16 tag, (n - 2) bytes
```

All little-endian. Signal `0` is the constant "0" and signal `1` is the constant "1".

Field tags seen:

| Tag | Meaning |
|---|---|
| 4 | Symbol path, UTF-16: `S-2.1.6.1:S-5` = top-level position 2.1.6.1 (a user module instance), symbol 5 inside it; hardware records use `Slot-01`, `IP-ID-03`, `COM-01` |
| 15 | Parameter count |
| 2 | Numeric parameter (i32). Times are in 1/100 s |
| 3 | String parameter, UTF-16. On SIMPL+ records, also the module's `.dll` name |
| 6 | Slot number or IP ID on hardware records |
| 10–13, 17, 22–24, 33 | Hardware and SIMPL+ attributes, not decoded yet |

The first record is always type 31 (`LeadingREM`), with the compile header as a string parameter.

### Primitive types

The compiler lowers SIMPL symbols into logic-engine primitives and flattens user modules. Record types are therefore **not** the `.smw` symbol codes (`SmC`). For example, the 1,393 type −12 records in one program are slots, cards and IP devices.

Across the 101 programs there are **99 distinct types**. Types are identified by matching each record's output signals, by name via the `.sig`, against the source symbol that drives the same signals. 36 programs ship their source, which gives a clean one-to-one mapping (AND = 0, NOT = 1, OR = 2, Toggle = 10, Interlock = 11, One Shot = 15, Buffer = 18, Delay = 27, Analog Initialize = 38, Analog Equate = 51, Analog Buffer = 67, Serial Buffer = 88, Serial I/O = 97, …). The full table is in `src/OpenVP.Lpz/PrimitiveCatalog.cs`.

Pins appear in the same order as on the SIMPL symbol. Unconnected pins point at signal 0 or 1. Times are in 1/100 s.

### IP devices

Records of type −12 named `IP-ID-xx` are the program's IP devices. Inputs are feedback to the device and outputs come from it. Field 10 is the number of digital inputs and field 12 the end of the analog inputs, so serial inputs follow. Fields 11 and 13 split the outputs the same way. Within each group, position n is join n.

### Hardware tree and ports

Field 7 of a hardware record is the index (in the file's record list, header record = 0) of its parent; 0 means top level. A DM-NVX's COM port reads `IP-ID-11` (−12) → `Slot-08` (−201) → `COM-01` (−200); the control processor's own IR ports read top-level `Slot-06` (−203) → `Port-01`, `Port-02` (−202). Field 6 is the slot number on slot records and the 0-based port number on port records. Paths repeat (`COM-01` appears under every device), so identify a port by walking field 7.

- **−200, COM port.** Two inputs, one output: input 1 is the serial text to send, input 2 is a constant `1` in every record seen, output 1 is the text received. Port settings are not in the record (fields 0, 1, 18 hold constants like `2500`).
- **−202, IR port.** One record per "Crestron DB IR-controlled device" symbol (SmC 107) on the port, so a port can have several records. Inputs 1 and 2 are constants `1` and `0`; input 2 + k is the trigger for IR command `params[k]` (fields tag 2, one per wired pin). Only wired pins are compiled and the numbers are a compact index of the driver commands the program uses, shared by every symbol on that driver (a symbol with only CH+ and CH− wired has params 20, 21, the same as in the fully wired symbol). They are not the `.ir` file's own command numbers, so names can only come from the `.smw`.

## Signal names (.sig)

Written next to the `.smw` at compile time. It is not inside the `.lpz`.

```
"[LOGOSSIG001.000]"
record := u16 length, UTF-16 name (length - 8 bytes), u32 index, u8 kind, u8 flags
kind   := 0 digital, 1 analog, 2 serial, 128 constant
```

Signals inside user modules are named `::localName:S-path.XXXXXXXX`, where the suffix is the index in hex. Only named signals appear. Unnamed module internals don't.

## SIMPL+ modules

Each `.usp` in a program becomes one `.dll` in the `.lpz`, a .NET assembly referencing `SplusObjects`, `SplusLibrary`, `SimplSharpHelperInterface` and `SmartThreadPoolCE`. It defines `UserModule_<NAME>.UserModuleClass_<NAME>`, built on `Crestron.Logos.SplusObjects` types (`SplusObject`, `DigitalInput`, `AnalogOutput`, `CrestronString`, `InOutArray<T>`, `SplusExecutionContext`, …) and calls `Crestron.Logos.SplusLibrary.Functions` for the SIMPL+ built-ins. OpenVP runs these through a clean-room implementation of that API (`src/OpenVP.Splus`), loaded into the slot's
load context under the original assembly names through type-forwarding facades (`src/SplusFacades`).

### The −48 record

| Field | Meaning |
|---|---|
| 10 | number of digital inputs |
| 12 | total inputs (digital + analog/serial) |
| 11 | number of digital outputs |
| 13 | total outputs |
| 3 / 2 | parameters, in order: `[0]` module name, `[1]` `.dll` name, then the module's parameters |

Input pin *n* (1-based) is digital input id *n−1* while *n ≤ field 10*; after that, the analog/serial ids restart at 0.
Outputs split the same way with fields 11/13. Parameter id *n* in the module is `Params[n]`.

### Module shape

The constructor is `(string instanceName, string referenceId, CrestronStringEncoding)`. `LogosSplusInitialize` creates
each input/output with a literal id constant (for example `NAME__DigitalInput__`), adds it to `m_DigitalInputList`,
`m_StringOutputList` and so on, and attaches handlers as
`InputChangeHandlerWrapper(SmartThreadSPLUSGLobalEventHandler, bool)` on `OnDigitalPush`/`OnDigitalRelease`/
`OnDigitalChange`/`OnAnalogChange`/`OnSerialChange`. `FunctionMain` is Function Main. Every handler starts with
`SplusThreadStartCode(...)` and ends with `ObjectFinallyHandler(...)`.

### Runtime behavior

- **Threading.** Each event runs on its own thread; one baton per module means only one runs at a time. The baton is
  taken in `SplusThreadStartCode` and given back in `ObjectFinallyHandler`. `Delay`, `ProcessLogic`, `Gather`,
  `WaitForInitializationComplete` and a blocked `CreateWait` hand it over while waiting.
- **WAIT.** `CreateWait(label, time, callback)` starts a timer (1/100 s). If a WAIT with the same label is still pending,
  the thread waits (baton released) until it has run, which is what makes `WHILE (1) { WAIT (...) }` tick once per period.
- **MakeString.** The compiler emits .NET composite formats (`"{0:d}:{1:d2}"`); printf-style is a fallback.
- **Files** map under the slot folder; **CrestronDataStore** keeps JSON per slot (local) and per processor (global).
- Set `OPENVP_SPLUS_TRACE=1` to log baton, event and output activity.

`samples/SplusModule` is a module written the way the compiler emits it; the test suite runs it inside a synthetic
`.lpz`.
