# Remote EISC

A Labs feature (off by default): link an EISC on this OpenVP to an EISC (a slot and an IP ID) on **another OpenVP machine**, so joins flow both ways over the network. Two slots on one OpenVP already link by themselves with an EISC pointing at `127.0.0.2`; this is for two machines.

Nothing here has been tried against a real Crestron processor or a real EISC. It links two OpenVPs with OpenVP's own protocol, not Crestron's.

## Setting it up

Machine **B** (the one that is called):

1. Labs → switch on **Remote EISC**.
2. Load a program that has an EISC (for example the **EISC pair B** example, IP ID `10`).
3. Security → API tokens → new token with scope **eisc-link**, optionally limited to the EISC's slot. Copy it.

Machine **A** (the one that calls):

1. Labs → switch on **Remote EISC**.
2. Push to other machines → add B (`http://b-address:8080`) with the token as its key. For https with a self-signed certificate, fetch and pin the certificate there.
3. Remote EISC → **Add link**: A's slot and EISC IP ID, B as the target, B's slot and EISC IP ID.

The page shows the state. The rack view shows the same state and the round-trip time, and B lists the link under *Incoming*. B needs no link of its own.

## What the token can do

Scope `eisc-link` carries one permission, `Eisc`, which opens `ws(s)://B/ws/eisc?slot=N&ipid=XX`. It can't read or set any other signal, list slots, or call any other endpoint, and it follows a token's slot limit. An Admin token or the access key also opens the socket. If Remote EISC is off in Labs on B, the socket answers 404. A web page on another site can't open it even with the token (the cross-origin check). The socket is never served on the tunnel port.

## Behaviour

- **Direction**: whatever a program sets on its EISC (the EISC's feedback) shows up as an input on the other EISC, both ways, the way two slots link locally.
- **Reconnect**: after a drop, A tries again after 1 s, then 2, 4, … up to 30 s, with a little jitter. The wait resets once a link has stayed up a few seconds.
- **Resync**: every connection begins with each side sending all of its joins, so after a reconnect the two programs agree. Serials equal to what the other side already holds are not delivered again by a resync.
- **Reloaded programs**: if the EISC's program is reloaded (a new device), the link ends and reconnects to the new one. With no EISC in the slot, the link waits.
- **Loops**: the link never sends back a value it just received. If two programs mirror a serial to each other, a serial equal to the one that just arrived (within 150 ms) isn't returned; a join that changes more than 200 times a second in either direction is paused for 3 seconds with a warning in the slot log. A program that sets a join to the value it already has sends nothing.
- **Identity**: each OpenVP has an instance id in `data/instance-id.txt`; a link to oneself is refused.
- **One link per EISC**: an EISC links to one remote EISC. A second incoming link to the same EISC replaces the first.
- **Latency** is a ping every 5 s over the link.

## Wire format

JSON text frames on a WebSocket, version 1: `hello` (`instance`, `version`, `slot`, `ipid`), `sync` (`joins`: every feedback join), `j` (`k` = `d`/`a`/`s`, `n`, `v`), `ping`/`pong`. It may change.

## Security notes

Plain `http` sends the token and every join unencrypted: use https between networks you don't trust. The token lives in the secret store, like a push target's key.
