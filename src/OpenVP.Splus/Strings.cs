using System;
using System.Text;

namespace Crestron.Logos.SplusObjects
{
    public enum CrestronStringEncoding
    {
        eEncodingUnknown = 0,
        eEncodingASCII = 1,
        eEncodingUTF16 = 2,
        eEncodingChar = 3
    }

    /// <summary>
    /// A SIMPL+ STRING: a byte string (characters 0–255) with a maximum length. Assignments longer than
    /// the maximum are truncated, as in SIMPL+. (Member names and shapes follow the SIMPL+ runtime API so
    /// compiled modules bind to them; OpenVP's own code uses <see cref="Str"/>.)
    /// </summary>
    public partial class CrestronString
    {
        private string _value = string.Empty;

        public CrestronString() : this(CrestronStringEncoding.eEncodingASCII, 65535, null) { }

        public CrestronString(CrestronStringEncoding encoding, int maxLength, SplusObject owner)
        {
            Encoding = encoding;
            MaxLength = maxLength <= 0 ? 65535 : maxLength;
            Owner = owner;
        }

        public CrestronString(string value) : this() { _value = value ?? string.Empty; }
        public CrestronString(int maxLength, SplusObject owner) : this(Inherit(owner), maxLength, owner) { }
        public CrestronString(string value, SplusObject owner) : this(Inherit(owner), 65535, owner) { _value = value ?? ""; }
        public CrestronString(string value, int maxLength, SplusObject owner) : this(Inherit(owner), maxLength, owner) { SetRaw(value); }
        public CrestronString(string value, CrestronStringEncoding encoding, int maxLength, SplusObject owner) : this(encoding, maxLength, owner) { SetRaw(value); }
        public CrestronString(char c, SplusObject owner) : this(Inherit(owner), 65535, owner) { _value = c.ToString(); }
        public CrestronString(CrestronString other) : this(other?.Encoding ?? CrestronStringEncoding.eEncodingASCII, other?.MaxLength ?? 65535, other?.Owner) { _value = other?.Str ?? ""; }
        public CrestronString(CrestronString other, CrestronStringEncoding encoding, int maxLength, SplusObject owner) : this(encoding, maxLength, owner) { SetRaw(other?.Str); }
        public CrestronString(StringBuilder value, int maxLength, SplusObject owner) : this(Inherit(owner), maxLength, owner) { SetRaw(value?.ToString()); }
        public CrestronString(StringBuilder value, int maxLength, int unused, SplusObject owner) : this(Inherit(owner), maxLength, owner) { SetRaw(value?.ToString()); }
        public CrestronString(StringBuilder value, CrestronStringEncoding encoding, int maxLength, SplusObject owner) : this(encoding, maxLength, owner) { SetRaw(value?.ToString()); }

        private static CrestronStringEncoding Inherit(SplusObject owner) => owner?.InheritedStringEncoding ?? CrestronStringEncoding.eEncodingASCII;

        public CrestronStringEncoding Encoding { get; set; }
        public CrestronStringEncoding StringEncoding { get => Encoding; set => Encoding = value; }
        public int MaxLength { get; protected set; }
        public int MaxCapacity { get => MaxLength; set => MaxLength = value <= 0 ? 65535 : value; }
        public SplusObject Owner { get; }

        /// <summary>The text (OpenVP's accessor).</summary>
        public string Str => _value;

        /// <summary>SIMPL+ runtime shape: the value as a StringBuilder (a copy).</summary>
        public StringBuilder Value
        {
            get => new StringBuilder(_value);
            set => SetValue(value?.ToString());
        }

        public int Length() => _value.Length;
        public int Len() => _value.Length;
        public int GetCapacity() => MaxLength;

        /// <summary>Every assignment goes through here; outputs override it to notify the program.</summary>
        protected virtual void SetValue(string value)
        {
            value ??= string.Empty;
            _value = value.Length > MaxLength ? value.Substring(0, MaxLength) : value;
        }

        internal void SetRaw(string value)
        {
            value ??= string.Empty;
            _value = value.Length > MaxLength ? value.Substring(0, MaxLength) : value;
        }

        public void UpdateValue(string value) => SetValue(value);
        public void UpdateValue(CrestronString value) => SetValue(value?.Str);
        public void UpdateValue(byte[] value) => SetValue(value == null ? "" : System.Text.Encoding.Latin1.GetString(value));
        public void UpdateValue(CrestronString value, SplusStructureBase s) => SetValue(value?.Str);
        public void UpdateValue(CrestronString value, CrestronStringEncoding e) => SetValue(value?.Str);
        public void UpdateValue(CrestronString value, CrestronStringEncoding e, SplusStructureBase s) => SetValue(value?.Str);
        public void UpdateValue(string value, SplusStructureBase s) => SetValue(value);
        public virtual void UpdateValue(string value, CrestronStringEncoding e, SplusStructureBase s) => SetValue(value);
        public void UpdateValue(Crestron.SimplSharp.SimplSharpString value) => SetValue(value?.ToString());

        public virtual void AppendValue(string value) => SetValue(_value + (value ?? ""));
        public void AppendValue(CrestronString value) => AppendValue(value?.Str);
        public void AppendValue(StringBuilder value) => AppendValue(value?.ToString());

        public virtual void ClearBuffer() => SetValue("");

        /// <summary>Takes count characters off the front and returns them.</summary>
        public virtual CrestronString Remove(int count, bool unused)
        {
            count = Math.Clamp(count, 0, _value.Length);
            var head = _value.Substring(0, count);
            SetValue(_value.Substring(count));
            return new CrestronString(head);
        }

        public CrestronString Substring(int start, int length)
        {
            start = Math.Clamp(start, 0, _value.Length);
            length = Math.Clamp(length, 0, _value.Length - start);
            return new CrestronString(_value.Substring(start, length));
        }

        /// <summary>Re-send the current value (outputs only).</summary>
        public virtual void SendValueToLogic() { }

        public Crestron.SimplSharp.SimplSharpString ToSimplSharpString() => new Crestron.SimplSharp.SimplSharpString(_value);
        public Crestron.SimplSharp.SimplSharpString ToSimplSharpString(CrestronString s) => new Crestron.SimplSharp.SimplSharpString(s?.Str ?? "");
        public Crestron.SimplSharp.SimplSharpString ToSimplSharpString(string s, CrestronStringEncoding e) => new Crestron.SimplSharp.SimplSharpString(s ?? "");

        public override string ToString() => _value;

        public byte[] ToByteArray() => System.Text.Encoding.Latin1.GetBytes(_value);

        public static implicit operator CrestronString(string s) => new CrestronString(s);
        public static implicit operator Crestron.SimplSharp.SimplSharpString(CrestronString s) => new Crestron.SimplSharp.SimplSharpString(s?.Str ?? "");
        public static explicit operator string(CrestronString s) => s?.Str;

        public static CrestronString operator +(CrestronString a, CrestronString b) => new CrestronString((a?.Str ?? "") + (b?.Str ?? ""));
        public static CrestronString operator +(CrestronString a, string b) => new CrestronString((a?.Str ?? "") + (b ?? ""));
        public static CrestronString operator +(string a, CrestronString b) => new CrestronString((a ?? "") + (b?.Str ?? ""));

        public static bool operator ==(CrestronString a, CrestronString b) => (a?.Str ?? "") == (b?.Str ?? "");
        public static bool operator !=(CrestronString a, CrestronString b) => !(a == b);
        public static bool operator ==(CrestronString a, string b) => (a?.Str ?? "") == (b ?? "");
        public static bool operator !=(CrestronString a, string b) => !(a == b);
        public static bool operator ==(string a, CrestronString b) => (a ?? "") == (b?.Str ?? "");
        public static bool operator !=(string a, CrestronString b) => !(a == b);

        public override bool Equals(object obj) => obj is CrestronString c ? c.Str == Str : obj is string s && s == Str;
        public override int GetHashCode() => _value.GetHashCode();
    }

    /// <summary>STRING_INPUT: the program sets it; the latest value replaces the previous one.</summary>
    public partial class StringInput : CrestronString
    {
        public StringInput(uint id, int maxLength, SplusObject owner)
            : this(id, uint.MaxValue, maxLength, owner) { }

        public StringInput(uint id, uint arrayStart, int maxLength, SplusObject owner)
            : base(owner?.InheritedStringEncoding ?? CrestronStringEncoding.eEncodingASCII, maxLength, owner)
        {
            Id = id;
            ArrayStart = arrayStart;
        }

        public uint Id { get; }
        public uint ArrayStart { get; }
        public uint JoinNumber => Id;
        public bool IsWaiting { get; internal set; }

        public System.Collections.Generic.List<InputChangeHandlerWrapper> OnSerialChange = new System.Collections.Generic.List<InputChangeHandlerWrapper>();
        internal InputChangeHandlerWrapper[] ChangeSnap;

        /// <summary>New text from the program. BUFFER_INPUT appends instead of replacing.</summary>
        internal virtual void Receive(string text) { SetRaw(text); _generation++; Owner?.BufferChanged(null); }

        public void UpdateInputValue(SignalEventArgs args) { }
        public string GetStringFragmentForError(string s) => s?.Length > 20 ? s.Substring(0, 20) : s;

        /// <summary>Wait (handing over the baton) for new input; false on timeout (1/100 s, negative = forever).</summary>
        public bool WaitForInput(int timeoutTicks)
        {
            var before = Str;
            var gen = _generation;
            Owner?.WaitForBuffer(() => _generation != gen, timeoutTicks < 0 ? -1 : timeoutTicks * 10);
            return _generation != gen || Str != before;
        }

        internal int _generation;

        public CrestronString Gather(string delimiter, int timeoutTicks) => Crestron.Logos.SplusLibrary.Functions.Gather(delimiter, this, timeoutTicks);
        public int Gather(string delimiter, CrestronString dest, int timeoutTicks) => Crestron.Logos.SplusLibrary.Functions.Gather(delimiter, this, dest, timeoutTicks);
        public CrestronString Gather(int length) => Crestron.Logos.SplusLibrary.Functions.Gather(length, this);
        public CrestronString Gather(int length, int timeoutTicks) => Crestron.Logos.SplusLibrary.Functions.Gather(length, this, timeoutTicks);
        public int Gather(int length, CrestronString dest) => Crestron.Logos.SplusLibrary.Functions.Gather(length, this, dest);
        public int Gather(int length, CrestronString dest, int timeoutTicks) => Crestron.Logos.SplusLibrary.Functions.Gather(length, this, dest, timeoutTicks);
    }

    /// <summary>BUFFER_INPUT: incoming text is appended (oldest characters drop when full).</summary>
    public partial class BufferInput : StringInput
    {
        public BufferInput(uint id, int maxLength, SplusObject owner) : base(id, maxLength, owner) { }
        public BufferInput(uint id, uint arrayStart, int maxLength, SplusObject owner) : base(id, arrayStart, maxLength, owner) { }

        internal override void Receive(string text)
        {
            var combined = Str + text;
            if (combined.Length > MaxLength) combined = combined.Substring(combined.Length - MaxLength);
            SetRaw(combined);
            _generation++;
            Owner?.BufferChanged(this);
            CheckAsyncGather();
        }

        protected override void SetValue(string value)
        {
            base.SetValue(value);
            Owner?.BufferChanged(this);
        }
    }

    /// <summary>STRING_OUTPUT: assigning it sends the text to the program.</summary>
    public partial class StringOutput : CrestronString
    {
        public StringOutput(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }

        public StringOutput(uint id, uint arrayStart, SplusObject owner)
            : base(owner?.InheritedStringEncoding ?? CrestronStringEncoding.eEncodingASCII, 65535, owner)
        {
            Id = id;
            ArrayStart = arrayStart;
        }

        public uint Id { get; }
        public uint ArrayStart { get; }
        public uint JoinNumber => Id;

        protected override void SetValue(string value)
        {
            base.SetValue(value);
            Owner?.EmitOutput(OpenVP.Core.SigKind.Serial, Id, Str);
        }

        public override void SendValueToLogic() => Owner?.EmitOutput(OpenVP.Core.SigKind.Serial, Id, Str);

        public static bool operator ==(StringOutput a, CrestronString b) => (a?.Str ?? "") == (b?.Str ?? "");
        public static bool operator !=(StringOutput a, CrestronString b) => !(a == b);
        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => base.GetHashCode();
    }

    /// <summary>STRING_PARAMETER: a constant set in SIMPL Windows.</summary>
    public partial class StringParameter : CrestronString, ISplusParameter
    {
        public StringParameter(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }
        public StringParameter(uint id, uint arrayStart, SplusObject owner)
            : base(CrestronStringEncoding.eEncodingASCII, 65535, owner) { Id = id; }
        public uint Id { get; }
        public uint ParameterNumber => Id;
        void ISplusParameter.Set(object value) => SetRaw(value?.ToString() ?? "");
        public void GetParameterValue() { }
        public void UpdateValue(SignalEventArgs args) { }
        public void HandleQueryError(int code) { }
    }
}
