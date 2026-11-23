// Tests for the SIMPL+ highlighter tokenizer in src/OpenVP.Server/wwwroot/openvp-splus.js. Run: node tests/js/splus.test.js
const assert = require("assert");
const path = require("path");
const { tokenize } = require(path.join(__dirname, "..", "..", "src", "OpenVP.Server", "wwwroot", "openvp-splus.js"));

const tests = [];
const test = (name, fn) => tests.push([name, fn]);
const kinds = line => line.filter(t => t.k !== "ws").map(t => t.k + ":" + t.t).join(" ");

test("one entry per source line, and the text is not changed", () => {
  const src = 'DIGITAL_INPUT a;\r\n\r\nSTRING_OUTPUT b$;';
  const lines = tokenize(src);
  assert.strictEqual(lines.length, 3);
  assert.strictEqual(lines[1].length, 0);
  assert.strictEqual(lines.map(l => l.map(t => t.t).join("")).join("\n"), src.replace(/\r\n/g, "\n"));
});

test("types, keywords and identifiers, in any letter case", () => {
  assert.strictEqual(kinds(tokenize("digital_input Power;")[0]), "ty:digital_input id:Power op:;");
  assert.strictEqual(kinds(tokenize("IF (x = 1) { Print(x); }")[0]),
    "kw:IF op:( id:x op:= num:1 op:) op:{ fn:Print op:( id:x op:) op:; op:}");
});

test("events", () => {
  assert.strictEqual(kinds(tokenize("PUSH Power")[0]), "ev:PUSH id:Power");
});

test("strings, with an escaped quote inside", () => {
  assert.strictEqual(kinds(tokenize('s = "a \\" b";')[0]), 'id:s op:= str:"a \\" b" op:;');
});

test("a line comment runs to the end of the line, and hides keywords", () => {
  assert.strictEqual(kinds(tokenize("x = 1; // IF while")[0]), "id:x op:= num:1 op:; com:// IF while");
  assert.strictEqual(kinds(tokenize('"//" // c')[0]), 'str:"//" com:// c');
});

test("block comments carry across lines, both styles", () => {
  const a = tokenize("a /* one\nIF two\nthree */ b");
  assert.strictEqual(kinds(a[0]), "id:a com:/* one");
  assert.strictEqual(kinds(a[1]), "com:IF two");
  assert.strictEqual(kinds(a[2]), "com:three */ id:b");
  const b = tokenize("(* x\ny *) PUSH");
  assert.strictEqual(kinds(b[1]), "com:y *) ev:PUSH");
});

test("directives, and help text between #HELP_BEGIN and #HELP_END is a comment", () => {
  const l = tokenize("#DEFINE_CONSTANT N 4\n#HELP_BEGIN\nIF this is help\n#HELP_END\nIF");
  assert.strictEqual(kinds(l[0]), "dir:#DEFINE_CONSTANT id:N num:4");
  assert.strictEqual(kinds(l[1]), "dir:#HELP_BEGIN");
  assert.strictEqual(kinds(l[2]), "com:IF this is help");
  assert.strictEqual(kinds(l[3]), "dir:#HELP_END");
  assert.strictEqual(kinds(l[4]), "kw:IF");
});

test("numbers: decimal, 0x hex, $ hex", () => {
  assert.strictEqual(kinds(tokenize("a = 0x1F + $FF + 255;")[0]), "id:a op:= num:0x1F op:+ num:$FF op:+ num:255 op:;");
});

test("an unterminated string does not run into the next line", () => {
  const l = tokenize('x = "abc\ny = 1;');
  assert.strictEqual(kinds(l[0]), 'id:x op:= str:"abc');
  assert.strictEqual(kinds(l[1]), "id:y op:= num:1 op:;");
});

test("identifiers that only contain a keyword are identifiers", () => {
  assert.strictEqual(kinds(tokenize("IFx FORMAT PUSHED")[0]), "id:IFx id:FORMAT id:PUSHED");
});

let failed = 0;
for (const [name, fn] of tests) {
  try { fn(); console.log("  ok   " + name); }
  catch (e) { failed++; console.log("  FAIL " + name + "\n       " + (e.stack || e).toString().split("\n").slice(0, 4).join("\n       ")); }
}
console.log(`${tests.length - failed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
