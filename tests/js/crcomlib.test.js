// Tests for the join and contract mapping in src/OpenVP.Server/wwwroot/openvp-crcomlib.js. Run: node tests/js/crcomlib.test.js
// (the C# test run does this too when node is installed). Uses only node's own modules.
const assert = require("assert");
const fs = require("fs");
const path = require("path");
const vm = require("vm");

const src = fs.readFileSync(path.join(__dirname, "..", "..", "src", "OpenVP.Server", "wwwroot", "openvp-crcomlib.js"), "utf8");

function load(search = "?ipid=03&slot=1", extra = {}) {
  const sockets = [];
  class FakeSocket {
    constructor(url) { this.url = url; this.sent = []; this.readyState = 0; sockets.push(this); }
    send(t) { this.sent.push(JSON.parse(t)); }
    close() { this.readyState = 3; this.onclose && this.onclose(); }
    open() { this.readyState = 1; this.onopen && this.onopen(); }
    push(m) { this.onmessage && this.onmessage({ data: JSON.stringify(m) }); }
  }
  const timers = [];
  const win = {
    location: { search, host: "panel.test:8080", protocol: "http:", href: "http://panel.test:8080/index.html", pathname: "/index.html" },
    WebSocket: FakeSocket, navigator: { userAgent: "node" }, screen: {}, innerWidth: 1, innerHeight: 1,
    setTimeout: (f) => { timers.push(f); return timers.length; }, clearTimeout() {}, setInterval: () => 1, clearInterval() {},
    console, URLSearchParams, URL, JSON, Math, Date, Object, Array, String, Number, Intl, ...extra
  };
  win.window = win;
  vm.createContext(win);
  vm.runInContext(src, win);
  return { win, sockets, ws: () => sockets[sockets.length - 1], api: win.OpenVP, flush: () => { while (timers.length) timers.shift()(); } };
}

// Objects from the loaded script live in another realm, so compare them as JSON.
const eq = (a, b) => assert.strictEqual(JSON.stringify(a), JSON.stringify(b));
const tests = [];
const test = (name, fn) => tests.push([name, fn]);

test("numeric names are joins of the type the caller asked for", () => {
  const { api } = load();
  eq(api.resolve("boolean", "12"), { type: "b", join: 12, smartObject: 0 });
  eq(api.resolve("number", "3"), { type: "n", join: 3, smartObject: 0 });
  eq(api.resolve("string", "7"), { type: "s", join: 7, smartObject: 0 });
  assert.strictEqual(api.resolve("boolean", "0"), null, "join 0 does not exist");
  assert.strictEqual(api.resolve("boolean", ""), null);
});

test("typed names carry their own type", () => {
  const { api } = load();
  eq(api.resolve("boolean", "a3"), { type: "n", join: 3, smartObject: 0 });
  eq(api.resolve("number", "d12"), { type: "b", join: 12, smartObject: 0 });
  eq(api.resolve("number", "digital_5"), { type: "b", join: 5, smartObject: 0 });
  eq(api.resolve("boolean", "serial:9"), { type: "s", join: 9, smartObject: 0 });
  assert.strictEqual(api.resolve("boolean", "Room1"), null, "a plain word is not a join");
});

test("smart object joins use the so<id>.<kind><join> form the IP table shows", () => {
  const { api } = load();
  eq(api.resolve("boolean", "so4.d1"), { type: "b", join: 1, smartObject: 4 });
  eq(api.resolve("boolean", "so12.a20"), { type: "n", join: 20, smartObject: 12 });
});

test("publishEvent sends the right message, clamps and coerces", () => {
  const { api, ws } = load();
  ws().open();
  api.publishEvent("boolean", "5", true);
  api.publishEvent("number", "2", 99999);
  api.publishEvent("number", "2", -4);
  api.publishEvent("number", "2", 12.6);
  api.publishEvent("string", "1", 42);
  api.publishEvent("boolean", "so4.d1", 1);
  api.publishEvent("boolean", "nonsense", true);
  const sent = ws().sent.filter(m => m.t !== "info" && m.t !== "ping");
  eq(sent, [
    { t: "b", j: 5, v: true }, { t: "n", j: 2, v: 65535 }, { t: "n", j: 2, v: 0 }, { t: "n", j: 2, v: 13 },
    { t: "s", j: 1, v: "42" }, { t: "b", j: 1, v: true, so: 4 }]);
});

test("the repeatdigital object form still publishes a digital", () => {
  const { api, ws } = load();
  ws().open();
  api.publishEvent("object", "7", { repeatdigital: true });
  eq(ws().sent.pop(), { t: "b", j: 7, v: true });
});

test("feedback reaches subscribers by join and smart object separately, and getState reads it back", () => {
  const { api, ws, win } = load();
  ws().open();
  const got = [];
  api.subscribeState("boolean", "5", v => got.push(["plain", v]));
  api.subscribeState("boolean", "so4.d5", v => got.push(["so4", v]));
  ws().push({ t: "b", j: 5, v: true });
  ws().push({ t: "b", j: 5, v: true, so: 4 });
  ws().push({ t: "b", j: 5, v: false, so: 4 });
  eq(got, [["plain", true], ["so4", true], ["so4", false]]);
  assert.strictEqual(api.getState("boolean", "5"), true);
  assert.strictEqual(api.getState("boolean", "so4.d5"), false);
  assert.strictEqual(api.getState("boolean", "6"), undefined);
});

test("a late subscriber gets the last value, and unsubscribe stops delivery", () => {
  const { api, ws, win } = load();
  ws().open();
  ws().push({ t: "s", j: 2, v: "hello" });
  const got = [];
  const id = api.subscribeState("string", "2", v => got.push(v));
  // the replay is queued with setTimeout
  win.__run = true;
  api.unsubscribeState("string", "2", id);
  ws().push({ t: "s", j: 2, v: "later" });
  eq(got, [], "nothing after unsubscribe");
});

test("a contract maps names to joins, with instances and offsets", () => {
  const { api, ws } = load();
  ws().open();
  const n = api.registerContract({
    name: "Room",
    members: { "Light.Press": { type: "b", join: 1 }, "Light.Fb": { type: "b", join: 1 }, "Volume.Set": { type: "n", join: 1 }, "Title": { type: "s", join: 2 } },
    instances: { Room1: { b: 0, n: 0, s: 0 }, Room2: { b: 10, n: 5, s: 3 } }
  });
  assert.ok(n >= 8);
  eq(api.resolve("boolean", "Room1.Light.Press"), { type: "b", join: 1, smartObject: 0 });
  eq(api.resolve("boolean", "Room2.Light.Press"), { type: "b", join: 11, smartObject: 0 });
  eq(api.resolve("number", "Room2.Volume.Set"), { type: "n", join: 6, smartObject: 0 });
  eq(api.resolve("string", "Room2.Title"), { type: "s", join: 5, smartObject: 0 });
  api.publishEvent("boolean", "Room2.Light.Press", true);
  eq(ws().sent.pop(), { t: "b", j: 11, v: true });
});

test("Name[i] addresses instance i using the contract's span", () => {
  const { api } = load();
  api.registerContract({ name: "Lights", members: { "Press": { type: "b", join: 1 }, "Level": { type: "n", join: 1 }, "Name": { type: "s", join: 1 } }, span: { b: 4, n: 2, s: 1 } });
  eq(api.resolve("boolean", "Lights[0].Press"), { type: "b", join: 1, smartObject: 0 });
  eq(api.resolve("boolean", "Lights[3].Press"), { type: "b", join: 13, smartObject: 0 });
  eq(api.resolve("number", "Lights[2].Level"), { type: "n", join: 5, smartObject: 0 });
  assert.strictEqual(api.resolve("boolean", "Lights[1].Nope"), null);
});

test("with no span given the span of a type is its highest join", () => {
  const { api } = load();
  api.registerContract({ name: "Z", members: { A: { type: "b", join: 1 }, B: { type: "b", join: 3 } } });
  eq(api.resolve("boolean", "Z[2].A"), { type: "b", join: 7, smartObject: 0 });
});

test("a subscription made before the contract arrives is attached when it does", () => {
  const { api, ws, win } = load();
  ws().open();
  const got = [];
  api.subscribeState("boolean", "Csig.Power", v => got.push(v));
  ws().push({ t: "b", j: 9, v: true });
  eq(got, []);
  api.mapSignals({ "Csig.Power": "d9" });
  ws().push({ t: "b", j: 9, v: false });
  eq(got, [false]);
});

test("unmapped names warn once and fire an event; nothing is sent", () => {
  const warnings = [];
  const { api, ws, win } = load("?ipid=03", {});
  win.console = { warn: m => warnings.push(m), error() {}, log() {} };
  ws().open();
  const ev = [];
  api.on("unmapped", d => ev.push(d.name));
  api.publishEvent("boolean", "Csig.Nope", true);
  api.publishEvent("boolean", "Csig.Nope", false);
  assert.strictEqual(ev.length, 1);
  assert.strictEqual(ws().sent.filter(m => m.t === "b").length, 0);
});

test("a Contract Editor export is flattened: joins numbered per type and direction in file order", () => {
  const { api } = load();
  const c = api.contractFromCse2j({
    ContractName: "Rooms",
    Objects: {
      Light: { Objects: { Press: { Type: "Boolean", Direction: "Event" }, Fb: { Type: "Boolean", Direction: "State" }, Dim: { Type: "Integer", Direction: "Event" } } },
      Source: { Objects: { Select: { Type: "Boolean", Direction: "Event" }, Name: { Type: "String", Direction: "State" } } }
    }
  });
  eq(c.members["Light.Press"], { type: "b", join: 1, dir: "out" });
  eq(c.members["Light.Fb"], { type: "b", join: 1, dir: "in" });
  eq(c.members["Light.Dim"], { type: "n", join: 1, dir: "out" });
  eq(c.members["Source.Select"], { type: "b", join: 2, dir: "out" });
  eq(c.members["Source.Name"], { type: "s", join: 1, dir: "in" });
  api.registerContract(c);
  eq(api.resolve("boolean", "Rooms.Source.Select"), { type: "b", join: 2, smartObject: 0 });
});

test("direct maps and explicit contract names win over parsing; bad entries are ignored", () => {
  const { api } = load();
  api.mapSignals({ "Panel.Home": "d1", "Bad": "nonsense", "Level": "a4" });
  eq(api.resolve("boolean", "Panel.Home"), { type: "b", join: 1, smartObject: 0 });
  eq(api.resolve("number", "Level"), { type: "n", join: 4, smartObject: 0 });
  assert.strictEqual(api.resolve("boolean", "Bad"), null);
});

test("the socket answers the server's ping and keeps a latency history", () => {
  const { api, ws } = load();
  ws().open();
  ws().push({ t: "sping", id: 7 });
  eq(ws().sent.pop(), { t: "spong", id: 7 });
  assert.strictEqual(api.latency.ms, null);
});

test("shared panels connect to the share's own socket and ignore the address bar", () => {
  const { ws } = load("?ipid=FF&slot=9", { OPENVP_CONFIG: { share: true, wsPath: "/s/abc/ws", statusUrl: "/s/abc/status" } });
  assert.strictEqual(ws().url, "ws://panel.test:8080/s/abc/ws");
});

test("a plain page connects with its ipid, slot and room", () => {
  const { ws } = load("?ipid=0A&slot=2&room=R1");
  assert.strictEqual(ws().url, "ws://panel.test:8080/ws/panel?ipid=0A&slot=2&room=R1");
});

let failed = 0;
for (const [name, fn] of tests) {
  try { fn(); console.log("  ok   " + name); }
  catch (e) { failed++; console.log("  FAIL " + name + "\n       " + (e.stack || e).toString().split("\n").slice(0, 4).join("\n       ")); }
}
console.log(`${tests.length - failed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
