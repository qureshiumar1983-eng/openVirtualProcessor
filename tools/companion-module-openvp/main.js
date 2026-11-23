'use strict'
// Bitfocus Companion module for an OpenVP server (an open-source virtual control processor).
// Talks to the server's REST API (press, release, pulse, analog, serial, start/stop/restart) and listens on
// /ws/trace for signal changes, so feedbacks and variables follow the program. All the message building and
// parsing is in lib/protocol.js; this file only connects it to Companion.
const { InstanceBase, InstanceStatus, Regex, combineRgb, runEntrypoint } = require('@companion-module/base')
const WebSocket = require('ws')
const P = require('./lib/protocol')
const { State } = require('./lib/state')

const GREEN = combineRgb(0, 140, 70)
const RED = combineRgb(170, 30, 30)
const WHITE = combineRgb(255, 255, 255)
const BLACK = combineRgb(0, 0, 0)

class OpenVpInstance extends InstanceBase {
	constructor(internal) {
		super(internal)
		this.state = new State()
		this.sockets = new Map() // slot -> ws
		this.watched = new Map() // "1:Room_On" -> { slot, name, refs }
		this.timers = { poll: null, reconnect: new Map() }
		this.destroyed = false
		this.slotVariables = new Set()
	}

	// ------------------------------------------------------------------ life cycle

	async init(config, _isFirstInit, secrets) {
		this.destroyed = false
		this.configure(config, secrets)
		this.setActionDefinitions(this.actions())
		this.setFeedbackDefinitions(this.feedbacks())
		this.setPresetDefinitions(this.presets())
		this.defineVariables()
		this.start()
	}

	async configUpdated(config, secrets) {
		this.stop()
		this.configure(config, secrets)
		this.defineVariables()
		this.start()
	}

	async destroy() {
		this.destroyed = true
		this.stop()
	}

	configure(config, secrets) {
		this.config = config ?? {}
		this.token = (secrets && secrets.token) || this.config.token || ''
		this.watchList = P.parseWatchList(this.config.watch, 1)
	}

	getConfigFields() {
		return [
			{ type: 'static-text', id: 'info', width: 12, label: 'OpenVP', value: 'Connects to an OpenVP server\'s REST API and /ws/trace stream. Make an API token in OpenVP under Security (scope operator is enough to press and send; read-only can only show feedback).' },
			{ type: 'textinput', id: 'host', label: 'Host', width: 8, default: '127.0.0.1', required: true },
			{ type: 'number', id: 'port', label: 'Port', width: 4, min: 1, max: 65535, default: 8080, required: true },
			{ type: 'secret-text', id: 'token', label: 'API token', width: 8, default: '', regex: Regex.SOMETHING },
			{ type: 'checkbox', id: 'tls', label: 'Use HTTPS', width: 4, default: false },
			{ type: 'textinput', id: 'watch', label: 'Signals to show as variables (one per line: slot:name, e.g. 1:Room_On)', width: 12, default: '' },
			{ type: 'number', id: 'poll', label: 'Slot state refresh (seconds)', width: 6, min: 1, max: 60, default: 3 },
		]
	}

	// ------------------------------------------------------------------ connection

	start() {
		this.updateStatus(InstanceStatus.Connecting)
		for (const w of this.watchList) this.watch(w.slot, w.name)
		this.pollSlots()
		const every = Math.max(1, Number(this.config.poll) || 3) * 1000
		this.timers.poll = setInterval(() => this.pollSlots(), every)
	}

	stop() {
		clearInterval(this.timers.poll)
		this.timers.poll = null
		for (const t of this.timers.reconnect.values()) clearTimeout(t)
		this.timers.reconnect.clear()
		for (const ws of this.sockets.values()) {
			ws.removeAllListeners()
			ws.on('error', () => {})
			try { ws.close() } catch { /* already closed */ }
		}
		this.sockets.clear()
		this.state.clearSignals()
	}

	async http(req) {
		const url = P.baseUrl(this.config) + req.path
		const headers = { ...P.authHeaders(this.token), Accept: 'application/json' }
		const init = { method: req.method, headers, signal: AbortSignal.timeout(8000) }
		if (req.body !== undefined) { init.body = req.body; headers['Content-Type'] = 'application/json' }
		const res = await fetch(url, init)
		const text = await res.text()
		if (!res.ok) throw new Error(P.describeFailure(res.status, text))
		try { return text ? JSON.parse(text) : null } catch { return null }
	}

	async pollSlots() {
		try {
			const list = P.parseSlotList(await this.http(P.slotListRequest()))
			const changed = this.state.setSlots(list)
			this.updateStatus(InstanceStatus.Ok)
			if (changed.length) {
				this.defineVariables()
				this.publishSlotVariables()
				this.checkFeedbacks('slot_running', 'slot_state')
			}
			// Make sure each slot we watch has a stream (it is opened when the first signal in it is watched).
			for (const slot of new Set([...this.watched.values()].map((w) => w.slot))) this.ensureSocket(slot)
		} catch (e) {
			this.updateStatus(/not accepted|may not/.test(String(e.message)) ? InstanceStatus.AuthenticationFailure : InstanceStatus.ConnectionFailure, String(e.message))
		}
	}

	ensureSocket(slot) {
		if (this.destroyed || this.sockets.has(slot) || this.timers.reconnect.has(slot)) return
		let url
		try { url = P.wsUrl(this.config, slot) } catch { return }
		const ws = new WebSocket(url, { headers: P.authHeaders(this.token), handshakeTimeout: 8000 })
		this.sockets.set(slot, ws)
		ws.on('open', () => this.refreshSlotSignals(slot))
		ws.on('message', (data) => this.onFrame(String(data)))
		const lost = () => {
			if (this.sockets.get(slot) !== ws) return
			this.sockets.delete(slot)
			for (const w of this.watched.values()) if (w.slot === slot) this.state.signals.delete(P.keyOf(slot, w.name))
			this.checkFeedbacks('digital', 'analog_compare')
			this.publishSignalVariables()
			if (this.destroyed) return
			this.timers.reconnect.set(slot, setTimeout(() => { this.timers.reconnect.delete(slot); this.ensureSocket(slot) }, 3000))
		}
		ws.on('close', lost)
		ws.on('error', (e) => { this.log('debug', `Stream for slot ${slot}: ${e.message}`); lost() })
	}

	onFrame(text) {
		let any = false
		for (const ev of P.parseTraceFrame(text)) {
			if (!this.watched.has(P.keyOf(ev.slot, ev.signal))) continue
			if (this.state.setSignal(ev)) any = true
		}
		if (any) { this.checkFeedbacks('digital', 'analog_compare'); this.publishSignalVariables() }
	}

	// Current values: read on connect, since the stream carries changes only.
	async refreshSlotSignals(slot) {
		for (const w of this.watched.values()) {
			if (w.slot !== slot) continue
			try {
				const v = P.parseSignalReply(await this.http(P.signalReadRequest(w.slot, w.name)))
				if (v) this.state.setSignal(v)
			} catch (e) { this.log('debug', `Reading ${w.slot}:${w.name}: ${e.message}`) }
		}
		this.checkFeedbacks('digital', 'analog_compare')
		this.publishSignalVariables()
	}

	// ------------------------------------------------------------------ what is being watched

	watch(slot, name) {
		const k = P.keyOf(slot, name)
		const w = this.watched.get(k)
		if (w) { w.refs++; return }
		this.watched.set(k, { slot: Number(slot), name, refs: 1 })
		if (this.sockets.has(Number(slot))) this.refreshSlotSignals(Number(slot))
		else this.ensureSocket(Number(slot))
	}

	unwatch(slot, name) {
		const k = P.keyOf(slot, name)
		const w = this.watched.get(k)
		if (!w) return
		if (--w.refs <= 0) { this.watched.delete(k); this.state.signals.delete(k) }
	}

	// ------------------------------------------------------------------ variables

	defineVariables() {
		const defs = []
		this.slotVariables = new Set()
		for (const n of [...this.state.slots.keys()].sort((a, b) => a - b)) {
			defs.push({ variableId: `slot${n}_state`, name: `Slot ${n} state (Empty, Loaded, Running, Stopped, Error)` })
			defs.push({ variableId: `slot${n}_program`, name: `Slot ${n} program name` })
			this.slotVariables.add(n)
		}
		for (const w of this.watchList) defs.push({ variableId: P.variableId(w.slot, w.name), name: `Slot ${w.slot} signal ${w.name}` })
		this.setVariableDefinitions(defs)
	}

	publishSlotVariables() {
		const values = {}
		for (const [n, s] of this.state.slots) { values[`slot${n}_state`] = s.state; values[`slot${n}_program`] = s.program }
		this.setVariableValues(values)
	}

	publishSignalVariables() {
		const values = {}
		for (const w of this.watchList) {
			const v = this.state.getSignal(w.slot, w.name)
			values[P.variableId(w.slot, w.name)] = v === undefined ? '' : v.kind === 'digital' ? (v.value ? '1' : '0') : String(v.value)
		}
		this.setVariableValues(values)
	}

	// ------------------------------------------------------------------ actions

	async send(req) {
		try {
			await this.http(req)
		} catch (e) {
			this.log('error', `${req.method} ${req.path}: ${e.message}`)
		}
	}

	async expand(context, text) {
		return context && context.parseVariablesInString ? context.parseVariablesInString(String(text ?? '')) : String(text ?? '')
	}

	actions() {
		const slot = { type: 'textinput', id: 'slot', label: 'Slot', default: '1', useVariables: true, regex: Regex.SOMETHING }
		const signal = { type: 'textinput', id: 'signal', label: 'Signal name (the name in the program\'s .sig file, or #12 for a join number when it has none)', default: '', useVariables: true, regex: Regex.SOMETHING }
		const signalAction = (kind, name, extra = []) => ({
			name,
			options: [slot, signal, ...extra],
			callback: async (action, context) => {
				try {
					const o = action.options
					const req = P.buildSignalRequest(kind, await this.expand(context, o.slot), await this.expand(context, o.signal), o.value === undefined ? undefined : await this.expand(context, o.value))
					await this.send(req)
				} catch (e) { this.log('error', e.message) }
			},
		})
		const slotAction = (kind, name) => ({
			name,
			options: [slot],
			callback: async (action, context) => {
				try { await this.send(P.buildSlotRequest(kind, await this.expand(context, action.options.slot))) } catch (e) { this.log('error', e.message) }
			},
		})
		return {
			press: signalAction('press', 'Digital join: press (hold high)'),
			release: signalAction('release', 'Digital join: release (low)'),
			pulse: signalAction('pulse', 'Digital join: pulse (high then low)'),
			analog: signalAction('analog', 'Analog join: set', [{ type: 'textinput', id: 'value', label: 'Value (0 to 65535, or 50%)', default: '0', useVariables: true }]),
			serial: signalAction('serial', 'Serial join: send text', [{ type: 'textinput', id: 'value', label: 'Text', default: '', useVariables: true }]),
			slot_start: slotAction('start', 'Slot: start'),
			slot_stop: slotAction('stop', 'Slot: stop'),
			slot_restart: slotAction('restart', 'Slot: restart'),
		}
	}

	// ------------------------------------------------------------------ feedbacks

	feedbacks() {
		const slot = { type: 'number', id: 'slot', label: 'Slot', default: 1, min: 1, max: 99 }
		const signal = { type: 'textinput', id: 'signal', label: 'Signal name', default: '', regex: Regex.SOMETHING }
		const subscribe = (fb) => this.watch(fb.options.slot, String(fb.options.signal))
		const unsubscribe = (fb) => this.unwatch(fb.options.slot, String(fb.options.signal))
		return {
			digital: {
				type: 'boolean',
				name: 'Digital join is high',
				description: 'True while the digital signal is high',
				defaultStyle: { bgcolor: GREEN, color: WHITE },
				options: [slot, signal, { type: 'checkbox', id: 'invert', label: 'Invert (true while low)', default: false }],
				callback: (fb) => {
					const v = this.state.getSignal(fb.options.slot, String(fb.options.signal))
					return !!(v && v.kind === 'digital' && v.value) !== !!fb.options.invert
				},
				subscribe, unsubscribe,
			},
			analog_compare: {
				type: 'boolean',
				name: 'Analog join compares to a value',
				defaultStyle: { bgcolor: GREEN, color: WHITE },
				options: [
					slot, signal,
					{ type: 'dropdown', id: 'op', label: 'Is', default: 'ge', choices: [
						{ id: 'eq', label: '=' }, { id: 'ne', label: '≠' }, { id: 'gt', label: '>' }, { id: 'ge', label: '≥' }, { id: 'lt', label: '<' }, { id: 'le', label: '≤' },
					] },
					{ type: 'number', id: 'value', label: 'Value', default: 1, min: 0, max: 65535 },
				],
				callback: (fb) => {
					const v = this.state.getSignal(fb.options.slot, String(fb.options.signal))
					return !!v && v.kind === 'analog' && P.compare(fb.options.op, v.value, Number(fb.options.value))
				},
				subscribe, unsubscribe,
			},
			slot_running: {
				type: 'boolean',
				name: 'Slot is running',
				defaultStyle: { bgcolor: GREEN, color: WHITE },
				options: [{ type: 'number', id: 'slot', label: 'Slot', default: 1, min: 1, max: 99 }, { type: 'checkbox', id: 'invert', label: 'Invert (true while not running)', default: false }],
				callback: (fb) => this.state.slotRunning(fb.options.slot) !== !!fb.options.invert,
			},
			slot_state: {
				type: 'boolean',
				name: 'Slot is in a state',
				defaultStyle: { bgcolor: RED, color: WHITE },
				options: [
					{ type: 'number', id: 'slot', label: 'Slot', default: 1, min: 1, max: 99 },
					{ type: 'dropdown', id: 'state', label: 'State', default: 'Error', choices: ['Empty', 'Loaded', 'Running', 'Stopped', 'Error'].map((s) => ({ id: s, label: s })) },
				],
				callback: (fb) => this.state.slots.get(Number(fb.options.slot))?.state === fb.options.state,
			},
		}
	}

	// ------------------------------------------------------------------ presets

	presets() {
		const button = (category, name, text, down, up, feedbacks = []) => ({
			type: 'button', category, name,
			style: { text, size: 'auto', color: WHITE, bgcolor: BLACK },
			steps: [{ down, up }],
			feedbacks,
		})
		return {
			press: button('Digital', 'Press and hold', 'Press\\nd1', [{ actionId: 'press', options: { slot: '1', signal: 'Room_On' } }], [{ actionId: 'release', options: { slot: '1', signal: 'Room_On' } }],
				[{ feedbackId: 'digital', options: { slot: 1, signal: 'Room_On', invert: false }, style: { bgcolor: GREEN, color: WHITE } }]),
			pulse: button('Digital', 'Pulse', 'Pulse', [{ actionId: 'pulse', options: { slot: '1', signal: 'Doorbell' } }], []),
			start: button('Slot', 'Start slot 1', 'Start\\n1', [{ actionId: 'slot_start', options: { slot: '1' } }], [],
				[{ feedbackId: 'slot_running', options: { slot: 1, invert: false }, style: { bgcolor: GREEN, color: WHITE } }]),
			stop: button('Slot', 'Stop slot 1', 'Stop\\n1', [{ actionId: 'slot_stop', options: { slot: '1' } }], []),
			restart: button('Slot', 'Restart slot 1', 'Restart\\n1', [{ actionId: 'slot_restart', options: { slot: '1' } }], []),
		}
	}
}

runEntrypoint(OpenVpInstance, [])
