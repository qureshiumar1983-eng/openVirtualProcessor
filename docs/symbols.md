# Logic symbols

Generated from `src/OpenVP.Lpz/LogicSymbols.cs` (`LogicSymbolCatalog`); the tests fail if this file and the catalog disagree. Regenerate with `OPENVP_UPDATE_GOLDEN=1 dotnet run --project tests/OpenVP.Tests Docs_symbols`.

Every symbol has one implementation, used both for `.smw` source and compiled `.lpz` programs.

- **compiled**: checked against compiled programs that ship their source: primitive type, pin order and parameters.
- **documented**: written from the SIMPL Windows symbol descriptions. It runs from `.smw` (matched by name). Pin order and parameter units are best guesses, and the SmC code and compiled type are unknown until a real program shows them. To run one in a compiled program, name its type in `primitives.json`.

75 symbols: 67 compiled, 8 documented.

## Digital

| Symbol | Evidence | SmC | Compiled type | Pins |
|---|---|---|---|---|
| AND | compiled | 1 | 0 | in: i1..iN · out: o |
| NAND | documented | ? | ? | in: i1..iN · out: o |
| OR | compiled | 3 | 2 | in: i1..iN · out: o |
| NOR | compiled | 4 | 3 | in: i1..iN · out: o |
| XOR | compiled | ? | 4 | in: i1..iN · out: o (odd number high) |
| XNOR | documented | ? | ? | in: i1..iN · out: o (even number high) |
| NOT | compiled | 205 | 1 | in: i1..iN · out: o1..oN |
| Multiple NOT | compiled | 418 | 206 | in: i1..iN · out: o1..oN |
| Buffer (also: Digital Buffer) | compiled | 20 | 18 | in: enable, i1..iN · out: o1..oN (low while disabled) |
| Truth Table | compiled | 28 | 19 | in: i1..iN · out: o1..oM · params: rows |
| Transition Gate | compiled | 6 | 6 | in: i1..iN · out: pulse on any rising edge |
| Transition Gate (falling) | compiled | ? | 7 | in: i1..iN · out: pulse on any falling edge |
| Set/Reset Latch (also: SR Latch) | compiled | 12 | 9 | in: set, reset · out: out, out* |
| Toggle | compiled | 13 | 10 | in: clock, reset, set · out: out, out* |
| Interlock | compiled | 19 | 11 | in: clear, i1..iN · out: o1..oN |
| Interlock-Toggle | compiled | 419 | 207 | in: i1..iN, clear · out: o1..oN |
| Ring Counter | compiled | 17, 451 | 23 | in: clock, reset · out: o1..oN |
| Decade | compiled | 78 | 52 | in: analog · out: units 0-9, tens 0-9, hundreds 0-9 |
| Button Presser | compiled | 99 | 138 | in: i · out: o1..oN follow the input |
| Logic Wave Pulse | compiled | 409, 1309 | 204 | in: i1..iN · out: one-wave pulse |
| Logic Wave Delay | compiled | 408 | 203 | in: i1..iN · out: o1..oN · params: waves |

## Timers

| Symbol | Evidence | SmC | Compiled type | Pins |
|---|---|---|---|---|
| Oscillator | compiled | 8 | 14 | in: enable · out: o · params: high time, low time |
| One Shot | compiled | 9 | 15 | in: trigger, -, cancel · out: o, o* · params: time |
| Retriggerable One Shot | compiled | 10 | 17 | in: trigger, -, cancel · out: o, o* · params: time |
| Pulse Stretcher | compiled | 11 | 16 | in: i · out: o · params: time |
| Delay | compiled | 16 | 27 | in: i · out: o · params: time |
| Long Delay | documented | ? | ? | in: i · out: o · params: time |
| Multiple One Shots | compiled | 21 | 29 | in: t1..tN · out: o1..oN · params: time |
| Debounce | compiled | 50 | 56 | in: i · out: o · params: time |
| Stepper | compiled | 98 | 139 | in: start · out: busy, o1..oN · params: delay, duration, ... |
| Stepper with Progress & Reset | compiled | 777 | 241 | in: start, reset · out: running, ... |

## Analog

| Symbol | Evidence | SmC | Compiled type | Pins |
|---|---|---|---|---|
| Analog Initialize (also: Analog Init, Serial Initialize) | compiled | 32 | 38 | in: trigger(s) · out: value(s) · params: values |
| Analog Equate (also: Equate) | compiled | 38 | 51 | in: value, enable · out: o1..oN · params: values |
| Analog Buffer | compiled | 46 | 67 | in: enable, a1..aN · out: a1..aN (hold while disabled) |
| Analog Scaler | compiled | 24 | 30 | in: a · out: a · params: divisor, multiplier, offset |
| Analog Scaler w/o Zero Pass | compiled | 65 | 99 | in: a · out: a |
| Analog Scaler with I/O Limits | compiled | 541 | 224 | in: a · out: a · params: in lo, in hi, out lo, out hi |
| Analog RAM | compiled | 34 | 36 | in: a1..aN, store, recall, bank selects · out: a1..aN |
| Analog Step | compiled | 45 | 37 | in: clock, reset · out: a · params: values |
| Analog Ramp | compiled | ? | 20 | in: up, down · out: a · params: full-range time |
| Analog Ramp (Bounds Limited) | compiled | 540 | 223 | in: up, down · out: a · params: time, lower, upper, initial |
| Analog Rate Limiter | compiled | 66 | 80 | in: a · out: a · params: full-range time |
| Analog Flip | compiled | 58 | 87 | in: a · out: 65535 - a |
| Analog Sum | compiled | 57 | 100 | in: a1..aN · out: sum |
| Analog DivMod | compiled | 114 | 136 | in: a · out: quotient, remainder · params: divisor |
| Analog Comparison (Full Set) | compiled | 492 | 220 | in: a, b · out: =, ≠, <, ≤, >, ≥ |
| Analog Increment | compiled | 543 | 230 | in: up, down, ... · out: a · params: step, hold, repeat, ... |
| Analog Increment with Optional Feedback | compiled | 605 | 236 | in: up, down, -, feedback · out: a |
| Analog Limiter (provisional) | compiled | ? | 237 | in: lower, upper, value · out: clamped value |
| Analog to Digital | compiled | 84 | 106 | in: a · out: bits, MSB first |
| Digital to Analog (also: Binary to Analog) | documented | ? | ? | in: bits, MSB first · out: a |
| Analog 2's Offset Converter | documented | ? | ? | in: a1..aN · out: a1..aN with bit 15 flipped |
| Analog Negation | documented | ? | ? | in: a1..aN · out: -a1..-aN (two's complement) |
| Analog Debounce | documented | ? | ? | in: a · out: a once stable · params: time |

## Serial

| Symbol | Evidence | SmC | Compiled type | Pins |
|---|---|---|---|---|
| Serial Buffer | compiled | 56 | 88 | in: enable, s1..sN · out: s1..sN (hold while disabled) |
| Serial Send | compiled | 89 | 58 | in: t1..tN · out: s · params: strings |
| Multiple Serial Send | compiled | 1477 | ? | in: trigger · out: s1..sN · params: strings |
| Multiple Discrete Serial Send | compiled | ? | 266 | in: t1..tN · out: s1..sN · params: strings |
| Serial I/O | compiled | 101 | 97 | in: rx, t1..tN · out: tx, m1..mN · params: strings |
| Serial Gather | compiled | 62 | 93 | in: s · out: s · params: delimiter, max length |
| Serial Concatenation | compiled | ? | 122 | in: s1..sN · out: s (whatever arrives) |
| Serial Substring | documented | ? | ? | in: s · out: s · params: start (1-based), length |
| Serial to Analog | compiled | 96 | 103 | in: s · out: a |
| Analog to Serial | compiled | 77 | 82 | in: trigger, a1..aN · out: s · params: strings |
| Serial/Analog One-Shot | compiled | 72 | 114 | in: s or a · out: pulse · params: time |
| String Length | compiled | 2506 | 254 | in: s · out: a |
| Make String Permanent | compiled | 858 | 205 | in: s1..sN (kept across restarts) |
| Intersystem Communications | compiled | 327 | 85 | in: xsig, joins · out: xsig, joins · params: offset |

## System

| Symbol | Evidence | SmC | Compiled type | Pins |
|---|---|---|---|---|
| Clock Driver | compiled | 43 | 62 | out: time of day |
| When | compiled | 74 | 61 | in: time of day · out: pulse · params: time, when |
| Ethernet Offline Manager | compiled | 511, 633 | 156 | out: offline (always low) |
| Console | compiled | ? | 202 | in: command · out: reply |
| Clock Adjust (provisional) | compiled | ? | 121 | follows the host clock; does nothing |
| Time-of-day Relay (provisional) | compiled | ? | 101 | in: s · out: s |
| Constant (provisional) | compiled | ? | 161 | does nothing |
