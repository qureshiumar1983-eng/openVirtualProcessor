# Logic symbols

Generated from `src/OpenVP.Lpz/LogicSymbols.cs` (`LogicSymbolCatalog`); the tests fail if this file and the catalog disagree. Regenerate with `OPENVP_UPDATE_GOLDEN=1 dotnet run --project tests/OpenVP.Tests Docs_symbols`.

Every symbol has one implementation, used both for `.smw` source and compiled `.lpz` programs.

- **compiled**: checked against compiled programs that ship their source: primitive type, pin order and parameters.
- **documented**: written from the SIMPL Windows symbol descriptions. It runs from `.smw` (matched by name). Pin order and parameter units are best guesses, and the compiled type is unknown until a real program shows it. To run one in a compiled program, name its type in `primitives.json`. Where a documented symbol has an SmC, it was found in real `.smw` programs: its pins come from how they wire and name it (`src/OpenVP.Lpz/Primitives6.cs` says what each rests on), and `tests/OpenVP.Tests/logic-test` checks it.
- **inferred**: the compiled type and pins were seen in real programs (a private test set, not in this repo), but the behavior is inferred from how those programs wire and name the pins, usually because the symbol sits inside a macro whose source isn't available. Each one's code comment (`src/OpenVP.Lpz/Primitives4.cs`) says what the inference rests on.

120 symbols: 67 compiled, 9 inferred, 44 documented.

## Digital

| Symbol | Evidence | SmC | Compiled type | Pins |
|---|---|---|---|---|
| AND | compiled | 1 | 0 | in: i1..iN · out: o |
| NAND | documented | 2 | ? | in: i1..iN · out: o |
| OR | compiled | 3 | 2 | in: i1..iN · out: o |
| NOR | compiled | 4 | 3 | in: i1..iN · out: o |
| XOR (also: Exclusive OR) | compiled | 5 | 4 | in: i1..iN · out: o (odd number high) |
| XNOR | documented | ? | ? | in: i1..iN · out: o (even number high) |
| NOT | compiled | 205 | 1 | in: i1..iN · out: o1..oN |
| Multiple NOT | compiled | 418 | 206 | in: i1..iN · out: o1..oN |
| Buffer (also: Digital Buffer) | compiled | 20 | 18 | in: enable, i1..iN · out: o1..oN (low while disabled) |
| Truth Table | compiled | 28 | 19 | in: i1..iN · out: o1..oM · params: rows |
| Transition Gate | compiled | 6 | 6 | in: i1..iN · out: pulse on any rising edge |
| Transition Gate (falling) | compiled | ? | 7 | in: i1..iN · out: pulse on any falling edge |
| Set/Reset Latch (also: SR Latch) | compiled | 12 | 9 | in: reset, set (the .smw lists set, reset) · out: out, out* |
| Toggle | compiled | 13 | 10 | in: clock, reset, set (the .smw lists set, reset, clock) · out: out, out* |
| Interlock | compiled | 19 | 11 | in: i1..iN, clear (the .smw lists clear, a spare pin, then i1..iN) · out: o1..oN |
| Interlock-Toggle | compiled | 419 | 207 | in: i1..iN, clear (the .smw lists clear, a spare pin, then i1..iN) · out: o1..oN |
| Ring Counter | compiled | 17, 451 | 23 | in: clock, reset · out: o1..oN |
| Decade | compiled | 78 | 52 | in: analog · out: units 0-9, tens 0-9, hundreds 0-9 |
| Button Presser | compiled | 99 | 138 | in: i · out: o1..oN follow the input |
| Logic Wave Pulse | compiled | 409, 1309 | 204 | in: i1..iN · out: one-wave pulse |
| Logic Wave Delay | compiled | 408 | 203 | in: i1..iN · out: o1..oN · params: waves |
| Digital Multiplexer (also: Digital Mux) | documented | ? | ? | in: select (analog), i1..iN · out: i(select), low if none |
| Digital Demultiplexer (also: Digital Demux) | documented | ? | ? | in: select (analog), i · out: o1..oN, only o(select) follows |
| Digital RAM | documented | 90 | ? | in: d1..dN, store, recall, bank selects (the .smw lists store, recall, d1..dN, bank selects) · out: d1..dN |
| Digital Force | documented | 471 | ? | in: d1..dN (no outputs; does nothing) |

## Timers

| Symbol | Evidence | SmC | Compiled type | Pins |
|---|---|---|---|---|
| Oscillator | compiled | 8 | 14 | in: enable · out: o · params: high time, low time |
| One Shot | compiled | 9 | 15 | in: trigger, -, cancel · out: o, o* · params: time |
| Retriggerable One Shot | compiled | 10 | 17 | in: trigger, -, cancel · out: o, o* · params: time |
| Pulse Stretcher | compiled | 11 | 16 | in: i · out: o · params: time |
| Delay | compiled | 16 | 27 | in: i · out: o · params: time |
| Delay (Analog Time) | inferred | ? | 200 | in: start, cancel, time (analog, 1/100 s) · out: o |
| Long Delay | documented | ? | ? | in: i · out: o · params: time |
| Multiple One Shots | compiled | 21 | 29 | in: t1..tN, spare (the .smw lists the spare pin first) · out: o1..oN · params: time |
| Debounce | compiled | 50 | 56 | in: i · out: o · params: time |
| Stepper | compiled | 98 | 139 | in: start · out: busy, o1..oN · params: delay, duration, ... |
| Stepper with Progress & Reset | compiled | 777 | 241 | in: start, reset · out: running, progress, 2 analog, steps from output 5 |
| Variable Oscillator | documented | 410 | ? | in: enable, high time, low time (analog, 1/100 s) · out: o |

## Analog

| Symbol | Evidence | SmC | Compiled type | Pins |
|---|---|---|---|---|
| Analog Initialize (also: Analog Init, Serial Initialize) | compiled | 32 | 38 | in: trigger(s) · out: value(s) · params: values |
| Analog Equate (also: Equate) | compiled | 38 | 51 | in: value, enable · out: o1..oN · params: values |
| Analog Buffer | compiled | 46 | 67 | in: enable, a1..aN · out: a1..aN (hold while disabled) |
| Analog Scaler | compiled | 24 | 30 | in: a · out: a · params: divisor, multiplier, offset |
| Analog Scaler w/o Zero Pass | compiled | 65 | 99 | in: a · out: a |
| Analog Scaler with I/O Limits | compiled | 541 | 224 | in: a · out: a · params: in lo, in hi, out lo, out hi |
| Analog RAM | compiled | 34 | 36 | in: a1..aN, store, recall, bank selects (the .smw lists store, recall, a1..aN, bank selects) · out: a1..aN |
| Digital To Scaled Analog | documented | 36 | ? | in: d · out: a (high = 65535, low = 0) |
| Analog Value Sample | documented | 122 | ? | in: (tied to 0), sample, a1..aN · out: a1..aN |
| Double-Precision Analog Initialize | documented | 635 | ? | in: t1..tN · out: high word, low word · params: v1..vN (32-bit) |
| Analog Step | compiled | 45 | 37 | in: clock, reset · out: a · params: values |
| Analog Ramp | compiled | 22 | 20 | in: up, down · out: a · params: full-range time |
| Analog Ramp (Bounds Limited) | compiled | 540 | 223 | in: up, down · out: a · params: time, lower, upper, initial |
| Analog Rate Limiter | compiled | 66 | 80 | in: a · out: a · params: full-range time |
| Analog Flip | compiled | 58 | 87 | in: a · out: 65535 - a |
| Analog Sum (also: Digital Sum) | compiled | 57, 61 | 100 | in: a1..aN, or digitals · out: sum (a high digital counts 1) |
| Analog DivMod | compiled | 114 | 136 | in: a · out: quotient, remainder · params: divisor |
| Analog Compare | inferred | 35 | 45 | in: a, b · out: a > b, a = b, a < b |
| Preset (also: Analog Preset) | inferred | 23 | 21 | in: trigger · out: v1..vN · params: ramp time (not modeled: values are set at once), v1..vN |
| Numeric Keypad | inferred | ? | 41 | in: 0-9, clear, up, down, enter · out: value, entered · params: max, min |
| Analog Comparison (Full Set) | compiled | 492 | 220 | in: a, b · out: =, ≠, <, ≤, >, ≥ |
| Analog Increment | compiled | 543 | 230 | in: up, down, ... · out: a · params: step, hold, repeat, ... |
| Analog Increment with Optional Feedback | compiled | 605 | 236 | in: up, down, -, feedback · out: a |
| Analog Limiter (provisional) | compiled | ? | 237 | in: lower, upper, value · out: clamped value |
| Analog to Digital | compiled | 84 | 106 | in: a · out: bits, MSB first |
| Digital to Analog (also: Binary to Analog) | documented | ? | ? | in: bits, MSB first · out: a |
| Analog 2's Offset Converter | documented | ? | ? | in: a1..aN · out: a1..aN with bit 15 flipped |
| Analog Negation | documented | ? | ? | in: a1..aN · out: -a1..-aN (two's complement) |
| Analog Debounce | documented | ? | ? | in: a · out: a once stable · params: time |
| Analog Subtract (also: Analog Subtractor, Analog Difference) | documented | ? | ? | in: a1..aN · out: a1 - a2 - ... (wraps at 16 bits) |
| Analog Multiplier (also: Analog Multiply) | documented | ? | ? | in: a1..aN · out: product (low 16 bits) |
| Analog Divider (also: Analog Divide) | documented | ? | ? | in: a1..aN · out: a1 / a2 / ... (integer; divide by 0 gives 0) |
| Analog Minimum (also: Analog Min) | documented | ? | ? | in: a1..aN · out: smallest wired input |
| Analog Maximum (also: Analog Max) | documented | ? | ? | in: a1..aN · out: largest wired input |
| Analog Average | documented | ? | ? | in: a1..aN · out: mean of the wired inputs, rounded down |
| Analog Delay | documented | ? | ? | in: a · out: a, every change P1 later · params: time |
| Up/Down Counter (also: Analog Counter, Counter) | documented | ? | ? | in: up, down, reset · out: count (stops at the limits) · params: min, max, initial |
| Hex to Analog | documented | ? | ? | in: hex text · out: a (0 if not hex) |
| Analog Multiplexer (also: Analog Mux) | documented | ? | ? | in: select, a1..aN · out: a(select), 0 if none |
| Analog Demultiplexer (also: Analog Demux) | documented | ? | ? | in: select, a · out: o1..oN, only o(select) follows |
| Time Split (also: Time to Analog) | documented | ? | ? | in: time of day text · out: hour, minute, second |
| Analog Force | documented | 472 | ? | in: a1..aN (no outputs; does nothing) |

## Serial

| Symbol | Evidence | SmC | Compiled type | Pins |
|---|---|---|---|---|
| Serial Buffer | compiled | 56 | 88 | in: enable, s1..sN · out: s1..sN (hold while disabled) |
| Serial Send | compiled | 89 | 58 | in: t1..tN · out: s · params: strings |
| Multiple Serial Send | compiled | 1477 | ? | in: trigger · out: s1..sN · params: strings |
| Multiple Discrete Serial Send | compiled | 3106 | 266 | in: t1..tN · out: s1..sN · params: strings |
| Serial I/O | compiled | 101 | 97 | in: rx, t1..tN · out: tx, m1..mN · params: strings |
| Serial Gather | compiled | 62 | 93 | in: s · out: s · params: delimiter, max length |
| Serial Concatenation | compiled | 126 | 122 | in: s1..sN · out: s (whatever arrives) |
| ASCII Serial Decoder | inferred | 54 | 65 | in: text, play · out: busy, then char n at pin n + 2 · params: time per char |
| ASCII Keypad | inferred | 123 | 109 | in: clear, enable, backspace, then char n at pin n + 4 · out: text · params: max length |
| Digit Keypad | inferred | ? | 63 | in: clear, select, 0-9, *, # · out: text · params: max length |
| Password Memory | inferred | ? | 66 | in: text, store, recall, enter · out: stored text · params: default |
| Password Match | inferred | ? | 144 | in: text, enter, enable, reference · out: match · params: password |
| Serial Force | documented | 470 | ? | in: s1..sN (no outputs; does nothing) |
| Serial Demultiplexor | documented | 86 | ? | in: s, selector · out: s1..sN · params: base (s goes to output selector − base) |
| Serial Substring (Expandable) | documented | 1317 | ? | in: s · out: s1..sN · params: start, end per output (position/length, or CCXXh delimiter) |
| Message to Computer Port | documented | 94 | ? | in: trigger · params: text (written to the console) |
| Serial Substring | documented | ? | ? | in: s · out: s · params: start (1-based), length |
| Serial to Analog | compiled | 96 | 103 | in: s · out: a |
| Analog to Serial | compiled | 77 | 82 | in: trigger, a1..aN · out: s · params: strings |
| Serial/Analog One-Shot | compiled | 72 | 114 | in: s or a · out: pulse · params: time |
| String Length | compiled | 2506 | 254 | in: s · out: a |
| Make String Permanent | compiled | 858 | 205 | in: s1..sN (kept across restarts) |
| Intersystem Communications | compiled | 327 | 85 | in: xsig, joins · out: xsig, joins · params: offset |
| Serial Delay | documented | ? | ? | in: s · out: s, every change P1 later · params: time |
| Serial to Hex | documented | ? | ? | in: s · out: two hex digits per character |
| Hex to Serial | documented | ? | ? | in: hex text · out: the characters |
| Analog to Hex | documented | ? | ? | in: a · out: upper-case hex text · params: digits (1-4, default 4) |
| Serial Equate | documented | ? | ? | in: s, enable · out: o1..oN, high while s = string parameter k |
| Serial Compare | documented | ? | ? | in: s1, s2 · out: same, different |
| Serial RAM | documented | ? | ? | in: s1..sN, store, recall, bank selects · out: s1..sN |
| Serial Multiplexer (also: Serial Mux) | documented | ? | ? | in: select (analog), s1..sN · out: s(select), empty if none |
| Serial Demultiplexer (also: Serial Demux) | documented | ? | ? | in: select (analog), s · out: o1..oN, only o(select) follows |
| Time Join (also: Analog to Time) | documented | ? | ? | in: hour, minute, second · out: HH:MM:SS |

## System

| Symbol | Evidence | SmC | Compiled type | Pins |
|---|---|---|---|---|
| Clock Driver (also: Clock Driver w/out DST) | compiled | 43, 3078 | 62 | out: time of day |
| When | compiled | 74 | 61 | in: time of day · out: pulse · params: time, when |
| Ethernet Offline Manager (also: Activity Detection) | compiled | 511, 633, 1069 | 156 | out: offline / activity (always low) |
| Poll Manager | documented | 345 | ? | in: ... · out: offline (always low) |
| Console | compiled | ? | 202 | in: command · out: reply |
| Set System Clock | compiled | 154 | 121 | in: hour up, hour down, minute up, minute down (follows the host clock; does nothing) |
| Time-of-day Relay (provisional) | compiled | ? | 101 | in: s · out: s |
| Touchpanel Sleep/Wake Manager | compiled | 392 | 161 | in: sleep, wake (does nothing) |
