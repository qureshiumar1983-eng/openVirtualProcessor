namespace Crestron.SimplSharp
{
    /// <summary>String type used to pass text between SIMPL+ and SIMPL# libraries.</summary>
    public class SimplSharpString
    {
        public SimplSharpString() : this(string.Empty) { }
        public SimplSharpString(string value) { ToString_ = value ?? string.Empty; }
        private string ToString_;
        public override string ToString() => ToString_;
        public static implicit operator string(SimplSharpString s) => s?.ToString_ ?? string.Empty;
        public static implicit operator SimplSharpString(string s) => new SimplSharpString(s);
    }
}
