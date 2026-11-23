# companion-module-openvp

A [Bitfocus Companion](https://bitfocus.io/companion) module for [OpenVP](../../README.md), the open-source virtual control processor. Companion 3.x module format (`@companion-module/base` 1.x, Node 22).

- **Actions:** press, release and pulse a digital signal; set an analog signal; send serial text; start, stop and restart a slot.
- **Feedbacks:** digital signal high, analog compares to a value, slot running, slot in a state.
- **Variables:** `slotN_state`, `slotN_program`, and one per signal listed in the config.
- **How it talks to OpenVP:** REST (`PUT /api/slots/{n}/signals/{name}`, `POST .../pulse`, `POST /api/slots/{n}/start|stop|restart`, `GET /api/slots`) and one `/ws/trace?slot=N` WebSocket per slot that has a feedback or variable on it (signal changes only; current values are read when it connects). Everything is sent with `Authorization: Bearer <token>`. An **operator** scope token is enough; **read-only** shows feedback only.

Packages: `@companion-module/base` (MIT) and `ws` (MIT). The module itself is MIT.

## Status

The message building and parsing (`lib/`) has unit tests that run with node and no network:

    npm install
    npm test

The module was written against `@companion-module/base` 1.14 and loads with it (its manifest validates against the library's schema, `main.js` loads, and the action, feedback and preset tables build), but it **has not been run inside Companion**. Try it in Companion's module developer mode (`Developer modules path`) before relying on it.

## Try it without Companion

`node scripts/check.js http://127.0.0.1:8080 TOKEN 1 Room_On` presses, reads and releases a signal over the REST API and prints what the trace stream sends (checked against a real OpenVP server with operator and read-only tokens). Note that a request from the OpenVP computer itself is let in with no token while no users are set up, so test the token from another machine.

## Using it from source

Put this folder where Companion's *Developer modules path* points, run `npm install`, and add the *OpenVP* connection. Nothing here is published anywhere.
