'use strict'
// A manual check against a running OpenVP server, with no Companion: node scripts/check.js URL TOKEN SLOT SIGNAL
const WebSocket = require('ws')
const P = require('../lib/protocol')

async function main() {
	const [url, token, slot, signal] = process.argv.slice(2)
	if (!signal) { console.error('usage: node scripts/check.js http://host:8080 TOKEN SLOT SIGNAL'); process.exit(2) }
	const u = new URL(url)
	const config = { host: u.hostname, port: Number(u.port) || 8080, tls: u.protocol === 'https:' }
	const call = async (req) => {
		const r = await fetch(P.baseUrl(config) + req.path, { method: req.method, body: req.body, headers: { ...P.authHeaders(token), 'Content-Type': 'application/json' } })
		const text = await r.text()
		if (!r.ok) throw new Error(P.describeFailure(r.status, text))
		return text ? JSON.parse(text) : null
	}
	console.log('slots:', JSON.stringify(P.parseSlotList(await call(P.slotListRequest()))))
	const ws = new WebSocket(P.wsUrl(config, slot), { headers: P.authHeaders(token) })
	ws.on('message', (d) => { for (const ev of P.parseTraceFrame(String(d))) if (ev.signal === signal) console.log('stream:', JSON.stringify(ev)) })
	await new Promise((res, rej) => { ws.on('open', res); ws.on('error', rej) })
	console.log('read:', JSON.stringify(P.parseSignalReply(await call(P.signalReadRequest(slot, signal)))))
	await call(P.buildSignalRequest('press', slot, signal))
	await new Promise((r) => setTimeout(r, 400))
	console.log('read:', JSON.stringify(P.parseSignalReply(await call(P.signalReadRequest(slot, signal)))))
	await call(P.buildSignalRequest('release', slot, signal))
	await new Promise((r) => setTimeout(r, 400))
	ws.close()
}
main().catch((e) => { console.error(e.message); process.exit(1) })
