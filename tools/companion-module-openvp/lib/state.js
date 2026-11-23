'use strict'
const { keyOf } = require('./protocol')

/** The last value seen for each signal and the state of each slot, with what changed reported to a callback. */
class State {
	constructor() {
		this.signals = new Map() // "1:Room_On" -> { kind, value }
		this.slots = new Map() // 1 -> { number, state, program, name }
	}

	/** Returns true when the value is new. */
	setSignal(ev) {
		const k = keyOf(ev.slot, ev.signal)
		const old = this.signals.get(k)
		if (old && old.kind === ev.kind && old.value === ev.value) return false
		this.signals.set(k, { kind: ev.kind, value: ev.value })
		return true
	}

	getSignal(slot, name) {
		return this.signals.get(keyOf(slot, name))
	}

	/** Replace the slot table. Returns the numbers of slots whose state or program changed. */
	setSlots(list) {
		const changed = []
		const next = new Map()
		for (const s of list) {
			next.set(s.number, s)
			const old = this.slots.get(s.number)
			if (!old || old.state !== s.state || old.program !== s.program || old.name !== s.name) changed.push(s.number)
		}
		for (const n of this.slots.keys()) if (!next.has(n)) changed.push(n)
		this.slots = next
		return changed
	}

	slotRunning(slot) {
		return this.slots.get(Number(slot))?.state === 'Running'
	}

	/** Forget signal values (when the connection to the server is lost, so nothing shows a stale "on"). */
	clearSignals() {
		this.signals.clear()
	}
}

module.exports = { State }
