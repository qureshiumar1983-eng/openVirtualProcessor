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
           u32 outputsAt     byte offset of outputs within the record
           u32 inputsAt      byte offset of inputs within the record
           field*            from byte 16 up to inputsAt
           u32 input*        signal indices, (outputsAt - inputsAt) / 4 of them
           u32 output*       signal indices, (size - outputsAt) / 4 of them
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

Across the 101 programs there are **99 distinct types**. About 40 of them account for nearly all records. The working names and status are in `src/OpenVP.Lpz/PrimitiveCatalog.cs`. Types are identified by matching each record's input and output signals, by name via the `.sig` file, against the source symbol that drives the same signals.

## Signal names (.sig)

Written next to the `.smw` at compile time. It is not inside the `.lpz`.

```
"[LOGOSSIG001.000]"
record := u16 length, UTF-16 name (length - 8 bytes), u32 index, u8 kind, u8 flags
kind   := 0 digital, 1 analog, 2 serial, 128 constant
```

Signals inside user modules are named `::localName:S-path.XXXXXXXX`, where the suffix is the index in hex. Only named signals appear. Unnamed module internals don't.

## SIMPL+ modules

Each `.usp` in a program becomes one `.dll` in the `.lpz`, a .NET assembly referencing `SplusObjects`, `SplusLibrary`, `SimplSharpHelperInterface` and `SmartThreadPoolCE`. It defines `UserModule_<NAME>.UserModuleClass_<NAME>`, built on `Crestron.Logos.SplusObjects` types (`SplusObject`, `DigitalInput`, `AnalogOutput`, `CrestronString`, `InOutArray<T>`, `SplusExecutionContext`, …) and calls `Crestron.Logos.SplusLibrary.Functions` for the SIMPL+ built-ins. OpenVP's plan is to run these through a clean-room implementation of that API, the same way it runs SIMPL# programs.
