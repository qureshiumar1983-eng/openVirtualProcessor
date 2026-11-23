'use strict'
const test = require('node:test')
const assert = require('node:assert/strict')
const P = require('../lib/protocol')
const { State } = require('../lib/state')

test('urls are built from host, port and tls, and a pasted url is cleaned', () => {
	assert.equal(P.baseUrl({ host: '10.0.0.5', port: 8080 }), 'http://10.0.0.5:8080')
	assert.equal(P.baseUrl({ host: 'http://openvp.local:9000/', port: 8081, tls: true }), 'https://openvp.local:8081')
	assert.equal(P.baseUrl({ host: '::1', port: 8080 }), 'http://[::1]:8080')
	assert.equal(P.baseUrl({ host: 'a' }), 'http://a:8080')
	assert.throws(() => P.baseUrl({ host: '  ' }), /No host/)
	assert.equal(P.wsUrl({ host: 'h', port: 8080 }, 2), 'ws://h:8080/ws/trace?slot=2')
	assert.equal(P.wsUrl({ host: 'h', port: 8443, tls: true }, 1), 'wss://h:8443/ws/trace?slot=1')
})

test('the token is sent as a Bearer header, and nothing when there is none', () => {
	assert.deepEqual(P.authHeaders(' abc123 '), { Authorization: 'Bearer abc123' })
	assert.deepEqual(P.authHeaders(''), {})
	assert.deepEqual(P.authHeaders(undefined), {})
})

test('digital press, release and pulse', () => {
	assert.deepEqual(P.buildSignalRequest('press', 1, 'Room_On'), { method: 'PUT', path: '/api/slots/1/signals/Room_On', body: '1' })
	assert.deepEqual(P.buildSignalRequest('release', '3', 'Room_On'), { method: 'PUT', path: '/api/slots/3/signals/Room_On', body: '0' })
	assert.deepEqual(P.buildSignalRequest('pulse', 1, 'Door bell'), { method: 'POST', path: '/api/slots/1/signals/Door%20bell/pulse' })
	assert.equal(P.buildSignalRequest('press', 1, '#12').path, '/api/slots/1/signals/%2312')
})

test('signal names cannot break out of the path', () => {
	assert.equal(P.signalPath(1, '../../etc/passwd'), '/api/slots/1/signals/..%2F..%2Fetc%2Fpasswd')
	assert.equal(P.signalPath(1, 'a?b#c'), '/api/slots/1/signals/a%3Fb%23c')
})

test('analog values: whole numbers 0 to 65535 and percentages', () => {
	assert.equal(P.buildSignalRequest('analog', 1, 'Vol', '32768').body, '32768')
	assert.equal(P.buildSignalRequest('analog', 1, 'Vol', ' 0 ').body, '0')
	assert.equal(P.buildSignalRequest('analog', 1, 'Vol', '50%').body, '"50%"')
	for (const bad of ['', 'abc', '-1', '65536', '1.5', '1e3']) assert.throws(() => P.buildSignalRequest('analog', 1, 'Vol', bad), /Analog value|No analog/, bad)
})

test('serial text is always a JSON string', () => {
	assert.equal(P.buildSignalRequest('serial', 1, 'Src', 'HDMI 1').body, '"HDMI 1"')
	assert.equal(P.buildSignalRequest('serial', 1, 'Src', '123').body, '"123"')
	assert.equal(P.buildSignalRequest('serial', 1, 'Src', 'say "hi"\n').body, '"say \\"hi\\"\\n"')
	assert.equal(P.buildSignalRequest('serial', 1, 'Src', '').body, '""')
})

test('bad slots, names and actions are refused', () => {
	for (const s of [0, 100, 1.5, 'x', '', null]) assert.throws(() => P.checkSlot(s), /Slot must be/, String(s))
	assert.throws(() => P.buildSignalRequest('press', 1, ''), /No signal name/)
	assert.throws(() => P.buildSignalRequest('shout', 1, 'a'), /Unknown signal action/)
	assert.throws(() => P.buildSlotRequest('delete', 1), /Unknown slot action/)
})

test('slot actions', () => {
	assert.deepEqual(P.buildSlotRequest('start', 2), { method: 'POST', path: '/api/slots/2/start' })
	assert.deepEqual(P.buildSlotRequest('stop', '2'), { method: 'POST', path: '/api/slots/2/stop' })
	assert.deepEqual(P.buildSlotRequest('restart', 10), { method: 'POST', path: '/api/slots/10/restart' })
	assert.deepEqual(P.slotListRequest(), { method: 'GET', path: '/api/slots' })
})

test('a trace frame gives signal changes and skips everything else', () => {
	const frame = JSON.stringify([
		{ t: 'trace', time: '2026-01-01T00:00:00Z', slot: 1, signal: 'Room_On', kind: 'Digital', value: true, source: 'x', depth: 0, chain: 1 },
		{ t: 'trace', slot: 1, signal: 'Vol', kind: 'Analog', value: 32768 },
		{ t: 'trace', slot: 2, signal: 'Src', kind: 'Serial', value: 'HDMI 1' },
		{ t: 'trace', slot: 1, signal: 'Off', kind: 'Digital', value: false },
		{ t: 'log', slot: 1, level: 'Info', message: 'hello' },
		{ t: 'presence', slot: 1, people: [] },
		{ t: 'trace', slot: 1, kind: 'Digital', value: true },
		null,
	])
	assert.deepEqual(P.parseTraceFrame(frame), [
		{ slot: 1, signal: 'Room_On', kind: 'digital', value: true },
		{ slot: 1, signal: 'Vol', kind: 'analog', value: 32768 },
		{ slot: 2, signal: 'Src', kind: 'serial', value: 'HDMI 1' },
		{ slot: 1, signal: 'Off', kind: 'digital', value: false },
	])
	assert.deepEqual(P.parseTraceFrame(JSON.stringify({ t: 'trace', slot: 1, signal: 'A', kind: 'Analog', value: 5 })), [{ slot: 1, signal: 'A', kind: 'analog', value: 5 }])
	assert.deepEqual(P.parseTraceFrame('not json'), [])
	assert.deepEqual(P.parseTraceFrame('[]'), [])
})

test('the reply for one signal', () => {
	assert.deepEqual(P.parseSignalReply({ slot: 1, name: 'Room_On', kind: 'digital', value: true }), { slot: 1, signal: 'Room_On', kind: 'digital', value: true })
	assert.deepEqual(P.parseSignalReply({ slot: 1, name: 'Vol', kind: 'analog', value: 100 }), { slot: 1, signal: 'Vol', kind: 'analog', value: 100 })
	assert.deepEqual(P.parseSignalReply({ slot: 1, name: 'S', kind: 'serial', value: null }), { slot: 1, signal: 'S', kind: 'serial', value: '' })
	assert.equal(P.parseSignalReply({ error: 'no' }), null)
	assert.equal(P.parseSignalReply(null), null)
})

test('the slot list', () => {
	assert.deepEqual(P.parseSlotList([{ number: 1, state: 'Running', program: 'Room', name: null, extra: 1 }, { number: 2, state: 'Empty', program: null }, { bad: true }]), [
		{ number: 1, state: 'Running', program: 'Room', name: '' },
		{ number: 2, state: 'Empty', program: '', name: '' },
	])
	assert.deepEqual(P.parseSlotList({ error: 'x' }), [])
})

test('the watch list takes slot:name, slot name, or just a name, once each', () => {
	assert.deepEqual(P.parseWatchList('1:Room_On\n2 Vol\n# comment\nAlone\n1:Room_On\n\n3:Has Space', 4), [
		{ slot: 1, name: 'Room_On' }, { slot: 2, name: 'Vol' }, { slot: 4, name: 'Alone' }, { slot: 3, name: 'Has Space' },
	])
	assert.deepEqual(P.parseWatchList('0:x\n100:y', 1), [], 'slots outside 1 to 99 are skipped')
	assert.deepEqual(P.parseWatchList(undefined), [])
})

test('variable ids use only letters, digits, - and _', () => {
	assert.equal(P.variableId(1, 'Room_On'), 's1_Room_On')
	assert.equal(P.variableId(2, 'Room.Light 1$'), 's2_Room_Light_1_')
})

test('comparisons', () => {
	assert.ok(P.compare('ge', 5, 5) && P.compare('gt', 6, 5) && P.compare('le', 5, 5) && P.compare('lt', 4, 5) && P.compare('eq', 5, 5) && P.compare('ne', 4, 5))
	assert.ok(!P.compare('gt', 5, 5) && !P.compare('nonsense', 1, 1))
})

test('failures are described for people', () => {
	assert.match(P.describeFailure(401, '{"error":"Sign in"}'), /token was not accepted.*Sign in/)
	assert.match(P.describeFailure(403, '{}'), /may not do this/)
	assert.match(P.describeFailure(423, '{"error":"Locked by Sam"}'), /locked.*Locked by Sam/)
	assert.match(P.describeFailure(500, 'plain text'), /HTTP 500: plain text/)
})

test('state tracks changes, slot running and clears on disconnect', () => {
	const s = new State()
	assert.equal(s.setSignal({ slot: 1, signal: 'A', kind: 'digital', value: true }), true)
	assert.equal(s.setSignal({ slot: 1, signal: 'A', kind: 'digital', value: true }), false, 'a repeat is not a change')
	assert.equal(s.getSignal(1, 'A').value, true)
	assert.deepEqual(s.setSlots([{ number: 1, state: 'Running', program: 'p', name: '' }]), [1])
	assert.deepEqual(s.setSlots([{ number: 1, state: 'Running', program: 'p', name: '' }]), [])
	assert.ok(s.slotRunning(1) && !s.slotRunning(2))
	assert.deepEqual(s.setSlots([{ number: 1, state: 'Stopped', program: 'p', name: '' }]), [1])
	assert.ok(!s.slotRunning(1))
	s.clearSignals()
	assert.equal(s.getSignal(1, 'A'), undefined)
})
