using System.Globalization;
using System.Text;

namespace OpenVP.Core;

/// <summary>An expression that doesn't parse. <see cref="Position"/> is the character where it went wrong.</summary>
public sealed class ExpressionException(string message, int position) : Exception(message)
{
    public int Position { get; } = position;
}

/// <summary>
/// A small, safe expression language for watches and conditional breakpoints. It is parsed by hand into a tree and
/// walked; nothing is compiled or evaluated as code.
/// <code>
/// Room_On=1 &amp;&amp; Proj_Power=0          digital signals are 1 or 0 (true and false work too)
/// Vol &gt; 50000   Vol &gt;= 50%   Vol &lt; 1F4h   analog values: 500, 500d, 1F4h, 0x1F4 or 50%
/// Src_Name contains "HDMI"             contains, startswith, endswith ignore case; = and != compare exactly
/// (A or B) and not C                   and, or, not work like &amp;&amp;, ||, !
/// Room_On=1 for 2s                     only once it has stayed true for 2 s (ms, s, m or h)
/// [Signal with spaces]                 square brackets quote a signal name (also one that looks like a number)
/// </code>
/// A bare signal is true when it is high, non-zero or a non-empty string. A signal the program doesn't have makes a
/// comparison false; <see cref="UnknownSignals"/> lists them so a UI can warn.
/// </summary>
public sealed class SignalExpression
{
    public const int MaxLength = 500;
    private const int MaxDepth = 24;

    private readonly Node _root;

    private SignalExpression(string text, Node root, int forMs, IReadOnlyList<string> signals)
    {
        Text = text; _root = root; ForMs = forMs; Signals = signals;
    }

    /// <summary>The text that was parsed, trimmed.</summary>
    public string Text { get; }

    /// <summary>How long the expression has to stay true before it counts (0 = at once).</summary>
    public int ForMs { get; }

    /// <summary>The signals the expression reads, once each, in the order they appear.</summary>
    public IReadOnlyList<string> Signals { get; }

    /// <summary>Parse, or throw <see cref="ExpressionException"/> with a readable message.</summary>
    public static SignalExpression Parse(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) throw new ExpressionException("The expression is empty.", 0);
        if (text.Length > MaxLength) throw new ExpressionException($"The expression is longer than {MaxLength} characters.", MaxLength);
        var p = new Parser(Lex(text), text);
        var root = p.ParseAll(out var forMs);
        return new SignalExpression(text, root, forMs, p.Signals);
    }

    public static bool TryParse(string? text, out SignalExpression? expr, out string? error)
    {
        try { expr = Parse(text); error = null; return true; }
        catch (ExpressionException e) { expr = null; error = e.Position > 0 ? $"{e.Message} (at character {e.Position + 1})" : e.Message; return false; }
    }

    /// <summary>
    /// True or false for the signal values <paramref name="lookup"/> returns (null for a signal that doesn't exist).
    /// Values may be bool, any number or string. Never throws.
    /// </summary>
    public bool Evaluate(Func<string, object?> lookup) => _root.Eval(lookup).Truthy;

    /// <summary>The signals this expression reads that <paramref name="exists"/> says aren't there.</summary>
    public IReadOnlyList<string> UnknownSignals(Func<string, bool> exists) => Signals.Where(s => !exists(s)).ToList();

    public override string ToString() => Text;

    // ---------------------------------------------------------------- values

    private enum VK { Missing, Bool, Num, Str }

    private readonly struct Val
    {
        public readonly VK Kind; public readonly double Num; public readonly string? Str;
        private Val(VK k, double n, string? s) { Kind = k; Num = n; Str = s; }
        public static readonly Val Missing = new(VK.Missing, 0, null);
        public static Val Of(bool b) => new(VK.Bool, b ? 1 : 0, null);
        public static Val Of(double n) => new(VK.Num, n, null);
        public static Val Of(string s) => new(VK.Str, 0, s);
        public static Val From(object? o) => o switch
        {
            null => Missing,
            bool b => Of(b),
            string s => Of(s),
            double d => Of(d),
            float f => Of((double)f),
            sbyte or byte or short or ushort or int or uint or long or ulong => Of(Convert.ToDouble(o, CultureInfo.InvariantCulture)),
            _ => Of(o.ToString() ?? "")
        };
        public bool Truthy => Kind switch { VK.Bool or VK.Num => Num != 0, VK.Str => Str!.Length > 0, _ => false };
        public string AsText => Kind switch
        {
            VK.Bool => Num != 0 ? "1" : "0",
            VK.Num => Num.ToString("0.###", CultureInfo.InvariantCulture),
            VK.Str => Str!,
            _ => ""
        };
        /// <summary>The number this value stands for: a bool is 0/1, a string that reads as a number is that number.</summary>
        public bool TryNum(out double n)
        {
            if (Kind is VK.Bool or VK.Num) { n = Num; return true; }
            if (Kind == VK.Str && TryNumber(Str!.Trim(), out n)) return true;
            n = 0; return false;
        }
    }

    // ---------------------------------------------------------------- tree

    private abstract class Node { public abstract Val Eval(Func<string, object?> lookup); }

    private sealed class Lit(Val v) : Node { public override Val Eval(Func<string, object?> _) => v; }
    private sealed class Sig(string name) : Node { public override Val Eval(Func<string, object?> lookup) => Val.From(lookup(name)); }
    private sealed class Not(Node a) : Node { public override Val Eval(Func<string, object?> l) => Val.Of(!a.Eval(l).Truthy); }
    private sealed class And(Node a, Node b) : Node { public override Val Eval(Func<string, object?> l) => Val.Of(a.Eval(l).Truthy && b.Eval(l).Truthy); }
    private sealed class Or(Node a, Node b) : Node { public override Val Eval(Func<string, object?> l) => Val.Of(a.Eval(l).Truthy || b.Eval(l).Truthy); }

    private enum Op { Eq, Ne, Lt, Le, Gt, Ge, Contains, StartsWith, EndsWith }

    private sealed class Cmp(Op op, Node a, Node b) : Node
    {
        public override Val Eval(Func<string, object?> l)
        {
            var x = a.Eval(l); var y = b.Eval(l);
            if (x.Kind == VK.Missing || y.Kind == VK.Missing) return Val.Of(false);
            switch (op)
            {
                case Op.Contains: return Val.Of(x.AsText.Contains(y.AsText, StringComparison.OrdinalIgnoreCase));
                case Op.StartsWith: return Val.Of(x.AsText.StartsWith(y.AsText, StringComparison.OrdinalIgnoreCase));
                case Op.EndsWith: return Val.Of(x.AsText.EndsWith(y.AsText, StringComparison.OrdinalIgnoreCase));
            }
            int c;
            if (x.TryNum(out var nx) && y.TryNum(out var ny)) c = nx.CompareTo(ny);
            else c = string.CompareOrdinal(x.AsText, y.AsText);
            return Val.Of(op switch
            {
                Op.Eq => c == 0, Op.Ne => c != 0, Op.Lt => c < 0, Op.Le => c <= 0, Op.Gt => c > 0, _ => c >= 0
            });
        }
    }

    // ---------------------------------------------------------------- lexer

    private enum TK { Word, Str, Quoted, LParen, RParen, And, Or, Not, Cmp, End }
    private readonly record struct Tok(TK Kind, string Text, int Pos, Op Op = default);

    private static bool IsDelim(char c) => char.IsWhiteSpace(c) || c is '(' or ')' or '=' or '!' or '<' or '>' or '&' or '|' or '"' or '\'' or '[' or ']';

    private static List<Tok> Lex(string s)
    {
        var toks = new List<Tok>();
        int i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            var start = i;
            switch (c)
            {
                case '(': toks.Add(new Tok(TK.LParen, "(", i++)); continue;
                case ')': toks.Add(new Tok(TK.RParen, ")", i++)); continue;
                case '&':
                    if (i + 1 < s.Length && s[i + 1] == '&') { toks.Add(new Tok(TK.And, "&&", i)); i += 2; continue; }
                    throw new ExpressionException("Use && for and.", i);
                case '|':
                    if (i + 1 < s.Length && s[i + 1] == '|') { toks.Add(new Tok(TK.Or, "||", i)); i += 2; continue; }
                    throw new ExpressionException("Use || for or.", i);
                case '!':
                    if (i + 1 < s.Length && s[i + 1] == '=') { toks.Add(new Tok(TK.Cmp, "!=", i, Op.Ne)); i += 2; }
                    else toks.Add(new Tok(TK.Not, "!", i++));
                    continue;
                case '=':
                    toks.Add(new Tok(TK.Cmp, "=", i, Op.Eq)); i += i + 1 < s.Length && s[i + 1] == '=' ? 2 : 1; continue;
                case '<':
                    if (i + 1 < s.Length && s[i + 1] == '=') { toks.Add(new Tok(TK.Cmp, "<=", i, Op.Le)); i += 2; }
                    else if (i + 1 < s.Length && s[i + 1] == '>') { toks.Add(new Tok(TK.Cmp, "<>", i, Op.Ne)); i += 2; }
                    else toks.Add(new Tok(TK.Cmp, "<", i++, Op.Lt));
                    continue;
                case '>':
                    if (i + 1 < s.Length && s[i + 1] == '=') { toks.Add(new Tok(TK.Cmp, ">=", i, Op.Ge)); i += 2; }
                    else toks.Add(new Tok(TK.Cmp, ">", i++, Op.Gt));
                    continue;
                case '"' or '\'':
                {
                    var q = c; i++;
                    var sb = new StringBuilder();
                    while (true)
                    {
                        if (i >= s.Length) throw new ExpressionException("A text value is missing its closing quote.", start);
                        var d = s[i++];
                        if (d == q) break;
                        if (d != '\\' || i >= s.Length) { sb.Append(d); continue; }
                        var e = s[i++];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'x' when i + 1 < s.Length && int.TryParse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h):
                                sb.Append((char)h); i += 2; break;
                            default: sb.Append(e); break;
                        }
                    }
                    toks.Add(new Tok(TK.Str, sb.ToString(), start));
                    continue;
                }
                case '[':
                {
                    var end = s.IndexOf(']', i + 1);
                    if (end < 0) throw new ExpressionException("A [signal name] is missing its closing ].", i);
                    var name = s.Substring(i + 1, end - i - 1).Trim();
                    if (name.Length == 0) throw new ExpressionException("The [ ] has no signal name in it.", i);
                    toks.Add(new Tok(TK.Quoted, name, start));
                    i = end + 1;
                    continue;
                }
                case ']': throw new ExpressionException("Unexpected ].", i);
            }
            while (i < s.Length && !IsDelim(s[i])) i++;
            var w = s[start..i];
            var lw = w.ToLowerInvariant();
            toks.Add(lw switch
            {
                "and" => new Tok(TK.And, w, start),
                "or" => new Tok(TK.Or, w, start),
                "not" => new Tok(TK.Not, w, start),
                "contains" => new Tok(TK.Cmp, w, start, Op.Contains),
                "startswith" => new Tok(TK.Cmp, w, start, Op.StartsWith),
                "endswith" => new Tok(TK.Cmp, w, start, Op.EndsWith),
                _ => new Tok(TK.Word, w, start)
            });
        }
        toks.Add(new Tok(TK.End, "", s.Length));
        return toks;
    }

    /// <summary>500, 500d, 1F4h, 0x1F4, 50% (of 65535), -3, 1.5.</summary>
    internal static bool TryNumber(string t, out double n)
    {
        n = 0;
        if (t.Length == 0) return false;
        if (t[^1] == '%' && double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) { n = Math.Round(pct * 65535 / 100); return true; }
        if (t.Length > 2 && (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) && long.TryParse(t.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hx)) { n = hx; return true; }
        if (t.Length > 1 && (t[^1] is 'h' or 'H') && long.TryParse(t.AsSpan(0, t.Length - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) { n = h; return true; }
        if (t.Length > 1 && (t[^1] is 'd' or 'D') && long.TryParse(t.AsSpan(0, t.Length - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var d)) { n = d; return true; }
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n);
    }

    /// <summary>"2s", "500ms", "1.5s", "2m", "1h" as milliseconds, or false.</summary>
    internal static bool TryDuration(string t, out int ms)
    {
        ms = 0;
        var lt = t.Trim().ToLowerInvariant();
        double mult;
        string num;
        if (lt.EndsWith("ms")) { mult = 1; num = lt[..^2]; }
        else if (lt.EndsWith('s')) { mult = 1000; num = lt[..^1]; }
        else if (lt.EndsWith('m')) { mult = 60_000; num = lt[..^1]; }
        else if (lt.EndsWith('h')) { mult = 3_600_000; num = lt[..^1]; }
        else return false;
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0 || v > 24 * 3600) return false;
        ms = (int)Math.Min(int.MaxValue, Math.Round(v * mult));
        return true;
    }

    // ---------------------------------------------------------------- parser

    private sealed class Parser(List<Tok> toks, string text)
    {
        private int _i, _depth;
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        public List<string> Signals { get; } = new();

        private Tok Cur => toks[_i];

        public Node ParseAll(out int forMs)
        {
            forMs = 0;
            var n = ParseOr();
            if (Cur.Kind == TK.Word && Cur.Text.Equals("for", StringComparison.OrdinalIgnoreCase))
            {
                _i++;
                if (Cur.Kind != TK.Word || !TryDuration(Cur.Text, out forMs))
                    throw new ExpressionException("After for, give a time such as 2s, 500ms or 1m.", Cur.Pos);
                _i++;
            }
            if (Cur.Kind != TK.End)
                throw new ExpressionException(Cur.Kind == TK.RParen ? "There is a ) without a matching (." : $"Didn't expect '{Cur.Text}' here.", Cur.Pos);
            return n;
        }

        private Node ParseOr()
        {
            var a = ParseAnd();
            while (Cur.Kind == TK.Or) { _i++; a = new Or(a, ParseAnd()); }
            return a;
        }

        private Node ParseAnd()
        {
            var a = ParseNot();
            while (Cur.Kind == TK.And) { _i++; a = new And(a, ParseNot()); }
            return a;
        }

        private Node ParseNot()
        {
            if (Cur.Kind == TK.Not)
            {
                _i++;
                if (++_depth > MaxDepth) throw new ExpressionException("The expression is nested too deeply.", Cur.Pos);
                var n = new Not(ParseNot());
                _depth--;
                return n;
            }
            return ParseCmp();
        }

        private Node ParseCmp()
        {
            var a = ParsePrimary();
            if (Cur.Kind == TK.Cmp)
            {
                var op = Cur;
                _i++;
                var b = ParsePrimary();
                return new Cmp(op.Op, a, b);
            }
            return a;
        }

        private Node ParsePrimary()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case TK.LParen:
                {
                    _i++;
                    if (++_depth > MaxDepth) throw new ExpressionException("The expression is nested too deeply.", t.Pos);
                    var n = ParseOr();
                    if (Cur.Kind != TK.RParen) throw new ExpressionException("A ( is missing its closing ).", t.Pos);
                    _i++; _depth--;
                    return n;
                }
                case TK.Str: _i++; return new Lit(Val.Of(t.Text));
                case TK.Quoted: _i++; return Signal(t.Text);
                case TK.Word:
                {
                    _i++;
                    var lw = t.Text.ToLowerInvariant();
                    if (lw == "true") return new Lit(Val.Of(true));
                    if (lw == "false") return new Lit(Val.Of(false));
                    if (TryNumber(t.Text, out var num)) return new Lit(Val.Of(num));
                    return Signal(t.Text);
                }
                case TK.End: throw new ExpressionException("The expression ends too early: a signal or value is missing.", t.Pos);
                default: throw new ExpressionException($"Expected a signal or a value, found '{t.Text}'.", t.Pos);
            }
        }

        private Node Signal(string name)
        {
            if (_seen.Add(name)) Signals.Add(name);
            return new Sig(name);
        }
    }
}
