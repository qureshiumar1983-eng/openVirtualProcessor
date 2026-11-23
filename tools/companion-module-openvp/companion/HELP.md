# OpenVP

Controls and monitors programs running on an [OpenVP](https://github.com/qureshiumar1983-eng/openVirtualProcessor) server.

## Set up

1. In OpenVP, open **Security** and make an **API token**. Scope **operator** can press, set, send and start/stop slots; **read-only** can only show feedback and variables. Limit it to the slots the buttons use.
2. Add the module: host, port (8080 unless you changed it), the token, and **Use HTTPS** if the server uses it.
3. Optional: list signals under *Signals to show as variables*, one per line as `slot:name` (for example `1:Room_On`). Each becomes a variable `$(openvp:s1_Room_On)`.

With no users or key set up on the OpenVP computer, the token can stay empty when Companion runs on that same computer.

## Signal names

A signal is named as in the program's `.sig` file (`Room_On`). A compiled program with no `.sig` has only numbers: use `#12` for the signal with join number 12 (see `GET /api/slots/1/signals` on your server for the exact names).

## Actions

- **Digital join: press / release / pulse.** Press holds the signal high, release lowers it, pulse is high then low.
- **Analog join: set** (0 to 65535, or a percentage like `50%`).
- **Serial join: send text.**
- **Slot: start / stop / restart.**

Slot and signal fields accept variables.

## Feedbacks

Digital join is high, analog join compares to a value, slot is running, slot is in a state.

## Variables

`slotN_state`, `slotN_program` for every slot, and one per listed signal.
