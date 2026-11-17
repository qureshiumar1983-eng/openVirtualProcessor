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
    /// the maximum are truncated, as in SIMPL+.
    /// </summary>
    public class CrestronString
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

        public CrestronStringEncoding Encoding { get; set; }
        public int MaxLength { get; protected set; }
        public SplusObject Owner { get; }

        public string Value
        {
            get => _value;
            set => SetValue(value);
        }

        public int Length => _value.Length;

        /// <summary>Every assignment goes through here; outputs override it to notify the program.</summary>
        protected virtual void SetValue(string value)
        {
            value ??= string.Empty;
            _value = value.Length > MaxLength ? value.Substring(0, MaxLength) : value;
        }

        internal void SetRaw(string value) => _value = value ?? string.Empty;

        public void UpdateValue(string value) => SetValue(value);
        public void UpdateValue(CrestronString value) => SetValue(value?.Value);
        public void UpdateValue(byte[] value) => SetValue(value == null ? "" : System.Text.Encoding.Latin1.GetString(value));

        public Crestron.SimplSharp.SimplSharpString ToSimplSharpString() => new Crestron.SimplSharp.SimplSharpString(_value);

        public override string ToString() => _value;

        public byte[] ToByteArray() => System.Text.Encoding.Latin1.GetBytes(_value);

        public static implicit operator CrestronString(string s) => new CrestronString(s);

        public static CrestronString operator +(CrestronString a, CrestronString b) => new CrestronString((a?.Value ?? "") + (b?.Value ?? ""));
        public static CrestronString operator +(CrestronString a, string b) => new CrestronString((a?.Value ?? "") + (b ?? ""));
        public static CrestronString operator +(string a, CrestronString b) => new CrestronString((a ?? "") + (b?.Value ?? ""));

        public static bool operator ==(CrestronString a, CrestronString b) => (a?.Value ?? "") == (b?.Value ?? "");
        public static bool operator !=(CrestronString a, CrestronString b) => !(a == b);
        public static bool operator ==(CrestronString a, string b) => (a?.Value ?? "") == (b ?? "");
        public static bool operator !=(CrestronString a, string b) => !(a == b);

        public override bool Equals(object obj) => obj is CrestronString c ? c.Value == Value : obj is string s && s == Value;
        public override int GetHashCode() => _value.GetHashCode();
    }

    /// <summary>STRING_INPUT: the program sets it; the latest value replaces the previous one.</summary>
    public class StringInput : CrestronString
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

        public System.Collections.Generic.List<InputChangeHandlerWrapper> OnSerialChange = new System.Collections.Generic.List<InputChangeHandlerWrapper>();

        /// <summary>New text from the program. BUFFER_INPUT appends instead of replacing.</summary>
        internal virtual void Receive(string text) => SetRaw(text.Length > MaxLength ? text.Substring(0, MaxLength) : text);
    }

    /// <summary>BUFFER_INPUT: incoming text is appended (oldest characters drop when full).</summary>
    public class BufferInput : StringInput
    {
        public BufferInput(uint id, int maxLength, SplusObject owner) : base(id, maxLength, owner) { }
        public BufferInput(uint id, uint arrayStart, int maxLength, SplusObject owner) : base(id, arrayStart, maxLength, owner) { }

        internal override void Receive(string text)
        {
            var combined = Value + text;
            if (combined.Length > MaxLength) combined = combined.Substring(combined.Length - MaxLength);
            SetRaw(combined);
            Owner?.BufferChanged(this);
        }

        protected override void SetValue(string value)
        {
            base.SetValue(value);
            Owner?.BufferChanged(this);
        }
    }

    /// <summary>STRING_OUTPUT: assigning it sends the text to the program.</summary>
    public class StringOutput : CrestronString
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

        protected override void SetValue(string value)
        {
            base.SetValue(value);
            Owner?.EmitOutput(OpenVP.Core.SigKind.Serial, Id, Value);
        }
    }

    /// <summary>STRING_PARAMETER: a constant set in SIMPL Windows.</summary>
    public class StringParameter : CrestronString, ISplusParameter
    {
        public StringParameter(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }
        public StringParameter(uint id, uint arrayStart, SplusObject owner)
            : base(CrestronStringEncoding.eEncodingASCII, 65535, owner) { Id = id; }
        public uint Id { get; }
        void ISplusParameter.Set(object value) => SetRaw(value?.ToString() ?? "");
    }
}
