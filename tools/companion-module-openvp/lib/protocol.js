'use strict'
// Everything the module says to an OpenVP server and everything it understands back, with no network and no
// Companion code in it, so it can be tested with plain node (see test/).

const SLOT_ACTIONS = ['start', 'stop', 'restart']

/** The host from what a person typed: "host", "host:8080" and "http://host/" all give "host" (the port has its own field). */
function cleanHost(host) {
	const h = String(host ?? '')
		.trim()
		.replace(/^[a-z]+:\/\//i, '')
		.replace(/\/.*$/, '')
	const bracket = /^\[([^\]]+)\]/.exec(h)
	if (bracket) return bracket[1] // [::1]:8080
	return h.split(':').length === 2 ? h.replace(/:\d*$/, '') : h // host:port, but leave an IPv6 address alone
}

function baseUrl(config) {
	const host = cleanHost(config.host)
	const port = Number(config.port) || 8080
	if (!host) throw new Error('No host set')
	const h = host.includes(':') && !host.startsWith('[') ? `[${host}]` : host // an IPv6 address
	return `${config.tls ? 'https' : 'http'}://${h}:${port}`
}

function wsUrl(config, slot) {
	const u = baseUrl(config).replace(/^http/, 'ws')
	return `${u}/ws/trace?slot=${encodeURIComponent(checkSlot(slot))}`
}

function authHeaders(token) {
	const t = String(token ?? '').trim()
	return t ? { Authorization: `Bearer ${t}` } : {}
}

function checkSlot(slot) {
	const n = Number(slot)
	if (!Number.isInteger(n) || n < 1 || n > 99) throw new Error(`Slot must be a whole number from 1 up, not "${slot}"`)
	return n
}

function checkName(name) {
	const s = String(name ?? '').trim()
	if (!s) throw new Error('No signal name given')
	if (s.length > 200) throw new Error('Signal name is too long')
	return s
}

const signalPath = (slot, name) => `/api/slots/${checkSlot(slot)}/signals/${encodeURIComponent(checkName(name))}`

/**
 * What to send for a signal action. `kind` is one of press, release, pulse, analog, serial.
 * A digital press and release set the signal to 1 and 0 (a held button); a pulse is high then low in one step.
 */
function buildSignalRequest(kind, slot, name, value) {
	const path = signalPath(slot, name)
	switch (kind) {
		case 'press':
			return { method: 'PUT', path, body: '1' }
		case 'release':
			return { method: 'PUT', path, body: '0' }
		case 'pulse':
			return { method: 'POST', path: `${path}/pulse` }
		case 'analog': {
			const text = String(value ?? '').trim()
			if (text === '') throw new Error('No analog value given')
			// 0 to 65535, or a percentage such as 50% (OpenVP reads the percentage itself)
			if (/^\d+%$/.test(text)) return { method: 'PUT', path, body: JSON.stringify(text) }
			const n = Number(text)
			if (!/^\d+$/.test(text) || n > 65535) throw new Error(`Analog value must be a whole number from 0 to 65535, or a percentage, not "${text}"`)
			return { method: 'PUT', path, body: String(n) }
		}
		case 'serial':
			// Always sent as a JSON string, so "123" and "true" stay text.
			return { method: 'PUT', path, body: JSON.stringify(String(value ?? '')) }
		default:
			throw new Error(`Unknown signal action ${kind}`)
	}
}

function buildSlotRequest(action, slot) {
	if (!SLOT_ACTIONS.includes(action)) throw new Error(`Unknown slot action ${action}`)
	return { method: 'POST', path: `/api/slots/${checkSlot(slot)}/${action}` }
}

const slotListRequest = () => ({ method: 'GET', path: '/api/slots' })
const signalReadRequest = (slot, name) => ({ method: 'GET', path: signalPath(slot, name) })

/** The key a signal is stored under. Signal names are case sensitive on the server. */
const keyOf = (slot, name) => `${Number(slot)}:${name}`

/**
 * The events in one /ws/trace frame: `[{ t: 'trace', slot, signal, kind, value }, { t: 'log', ... }, ...]`
 * (a frame is a JSON array, though a single object is accepted too). Only signal changes are returned, as
 * `{ slot, signal, kind: 'digital'|'analog'|'serial', value }`; log, step, presence and lock frames are skipped.
 */
function parseTraceFrame(text) {
	let data
	try {
		data = JSON.parse(text)
	} catch {
		return []
	}
	const out = []
	for (const m of Array.isArray(data) ? data : [data]) {
		if (!m || m.t !== 'trace' || typeof m.signal !== 'string') continue
		const kind = String(m.kind).toLowerCase()
		if (kind === 'digital') out.push({ slot: m.slot, signal: m.signal, kind, value: m.value === true || m.value === 1 })
		else if (kind === 'analog') out.push({ slot: m.slot, signal: m.signal, kind, value: Number(m.value) || 0 })
		else if (kind === 'serial') out.push({ slot: m.slot, signal: m.signal, kind, value: m.value == null ? '' : String(m.value) })
	}
	return out
}

/** The reply to GET /api/slots/{n}/signals/{name}: `{ slot, name, kind, value }` -> a stored value. */
function parseSignalReply(json) {
	if (!json || typeof json.name !== 'string') return null
	const kind = String(json.kind).toLowerCase()
	if (kind === 'digital') return { slot: json.slot, signal: json.name, kind, value: json.value === true || json.value === 1 }
	if (kind === 'analog') return { slot: json.slot, signal: json.name, kind, value: Number(json.value) || 0 }
	if (kind === 'serial') return { slot: json.slot, signal: json.name, kind, value: json.value == null ? '' : String(json.value) }
	return null
}

/** The reply to GET /api/slots: the fields the module uses. */
function parseSlotList(json) {
	if (!Array.isArray(json)) return []
	return json
		.filter((s) => s && Number.isInteger(s.number))
		.map((s) => ({
			number: s.number,
			state: String(s.state ?? 'Empty'),
			program: s.program == null ? '' : String(s.program),
			name: s.name == null ? '' : String(s.name),
		}))
}

/** "1:Room_On" lines (also "1 Room_On" and "Room_On" for slot 1) from the config's watch list. */
function parseWatchList(text, defaultSlot = 1) {
	const out = []
	const seen = new Set()
	for (const raw of String(text ?? '').split(/\r?\n|,/)) {
		const line = raw.trim()
		if (!line || line.startsWith('#')) continue
		const m = /^(\d+)\s*[:\s]\s*(.+)$/.exec(line)
		const slot = m ? Number(m[1]) : defaultSlot
		const name = (m ? m[2] : line).trim()
		if (!Number.isInteger(slot) || slot < 1 || slot > 99 || !name) continue
		const key = keyOf(slot, name)
		if (!seen.has(key)) {
			seen.add(key)
			out.push({ slot, name })
		}
	}
	return out
}

/** A variable id Companion accepts (letters, digits, - and _) for a slot signal. */
const variableId = (slot, name) => `s${Number(slot)}_${String(name).replace(/[^A-Za-z0-9_-]/g, '_')}`

/** Compares a stored analog value for the "analog compare" feedback. */
function compare(op, a, b) {
	switch (op) {
		case 'eq': return a === b
		case 'ne': return a !== b
		case 'gt': return a > b
		case 'ge': return a >= b
		case 'lt': return a < b
		case 'le': return a <= b
		default: return false
	}
}

/** A human message for a failed HTTP call. */
function describeFailure(status, bodyText) {
	let detail = ''
	try {
		const j = JSON.parse(bodyText)
		detail = j.error || j.message || ''
	} catch {
		detail = String(bodyText ?? '').slice(0, 120)
	}
	const why =
		status === 401 ? 'the token was not accepted'
		: status === 403 ? 'the token may not do this (an operator token or above can press and send; a read-only one cannot)'
		: status === 404 ? 'not found'
		: status === 409 ? 'the slot has no program running'
		: status === 423 ? 'the slot is locked'
		: ''
	return [`HTTP ${status}`, why, detail].filter(Boolean).join(': ')
}

module.exports = {
	SLOT_ACTIONS, cleanHost, baseUrl, wsUrl, authHeaders, checkSlot, checkName, signalPath,
	buildSignalRequest, buildSlotRequest, slotListRequest, signalReadRequest, keyOf,
	parseTraceFrame, parseSignalReply, parseSlotList, parseWatchList, variableId, compare, describeFailure,
}
