using System;
using System.Collections.Generic;
using OpenVP.Core;

namespace Amib.Threading.Internal
{
    /// <summary>Event handler delegate SIMPL+ modules pass for input changes, socket events and so on.</summary>
    public delegate object SmartThreadSPLUSGLobalEventHandler(object args);
}

namespace Crestron.Logos.SplusObjects
{
    using Amib.Threading.Internal;

    public class SplusExecutionContext
    {
        public int SourceCodeLine { get; set; }
    }

    /// <summary>Passed to an event handler: which input changed.</summary>
    public class SignalEventArgs : EventArgs
    {
        public SignalEventArgs(object source, uint id, uint arrayStart)
        {
            Source = source;
            Id = id;
            ArrayStart = arrayStart;
        }

        public object Source { get; }
        public uint Id { get; }
        public uint ArrayStart { get; }

        /// <summary>1-based index within an input array (0 if not in an array).</summary>
        public ushort ArrayIndex => ArrayStart == uint.MaxValue ? (ushort)0 : (ushort)(Id - ArrayStart + 1);
    }

    public class InputChangeHandlerWrapper
    {
        public InputChangeHandlerWrapper(SmartThreadSPLUSGLobalEventHandler handler, bool flag)
        {
            Handler = handler;
            Flag = flag;
        }

        public SmartThreadSPLUSGLobalEventHandler Handler { get; }
        public bool Flag { get; }
    }

    public class SocketHandlerWrapper : InputChangeHandlerWrapper
    {
        public SocketHandlerWrapper(SmartThreadSPLUSGLobalEventHandler handler, bool flag) : base(handler, flag) { }
    }

    /// <summary>DIGITAL_INPUT. Value is 1 or 0. PUSH, RELEASE and CHANGE handlers live in these lists.</summary>
    public class DigitalInput
    {
        public DigitalInput(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }

        public DigitalInput(uint id, uint arrayStart, SplusObject owner)
        {
            Id = id;
            ArrayStart = arrayStart;
            Owner = owner;
        }

        public uint Id { get; }
        public uint ArrayStart { get; }
        public SplusObject Owner { get; }

        public List<InputChangeHandlerWrapper> OnDigitalPush = new List<InputChangeHandlerWrapper>();
        public List<InputChangeHandlerWrapper> OnDigitalRelease = new List<InputChangeHandlerWrapper>();
        public List<InputChangeHandlerWrapper> OnDigitalChange = new List<InputChangeHandlerWrapper>();

        internal int RawValue;
        public int Value => RawValue;
    }

    /// <summary>ANALOG_INPUT.</summary>
    public class AnalogInput
    {
        public AnalogInput(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }

        public AnalogInput(uint id, uint arrayStart, SplusObject owner)
        {
            Id = id;
            ArrayStart = arrayStart;
            Owner = owner;
        }

        public uint Id { get; }
        public uint ArrayStart { get; }
        public SplusObject Owner { get; }

        public List<InputChangeHandlerWrapper> OnAnalogChange = new List<InputChangeHandlerWrapper>();

        internal ushort RawValue;
        public ushort UshortValue => RawValue;
        public short ShortValue => unchecked((short)RawValue);
        public int IntValue => RawValue;
        public ushort Value => RawValue;
    }

    /// <summary>DIGITAL_OUTPUT.</summary>
    public class DigitalOutput
    {
        private int _value;

        public DigitalOutput(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }
        public DigitalOutput(uint id, uint arrayStart, SplusObject owner) { Id = id; Owner = owner; }

        public uint Id { get; }
        public SplusObject Owner { get; }

        public int Value
        {
            get => _value;
            set
            {
                _value = value != 0 ? 1 : 0;
                Owner?.EmitOutput(SigKind.Digital, Id, _value != 0);
            }
        }
    }

    /// <summary>ANALOG_OUTPUT.</summary>
    public class AnalogOutput
    {
        private ushort _value;

        public AnalogOutput(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }
        public AnalogOutput(uint id, uint arrayStart, SplusObject owner) { Id = id; Owner = owner; }

        public uint Id { get; }
        public SplusObject Owner { get; }

        public ushort Value
        {
            get => _value;
            set
            {
                _value = value;
                Owner?.EmitOutput(SigKind.Analog, Id, value);
            }
        }
    }

    /// <summary>Input/output arrays. SIMPL+ arrays are 1-based; index 0 is unused.</summary>
    public class InOutArray<T>
    {
        private readonly T[] _items;

        public InOutArray(uint size, SplusObject owner)
        {
            _items = new T[size + 1];
            Owner = owner;
        }

        public SplusObject Owner { get; }
        public int Count => _items.Length - 1;

        public T this[int index]
        {
            get => index >= 0 && index < _items.Length ? _items[index] : default;
            set { if (index >= 0 && index < _items.Length) _items[index] = value; }
        }

        public T this[uint index]
        {
            get => this[(int)index];
            set => this[(int)index] = value;
        }
    }

    /// <summary>Marker for parameter objects (values come from SIMPL Windows).</summary>
    public interface ISplusParameter
    {
        void Set(object value);
    }

    public class UShortParameter : ISplusParameter
    {
        public UShortParameter(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }
        public UShortParameter(uint id, uint arrayStart, SplusObject owner) { Id = id; }
        public uint Id { get; }
        public ushort Value { get; private set; }
        void ISplusParameter.Set(object v) => Value = (ushort)Math.Clamp(Convert.ToInt64(v ?? 0), 0, 65535);
    }

    public class ShortParameter : ISplusParameter
    {
        public ShortParameter(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }
        public ShortParameter(uint id, uint arrayStart, SplusObject owner) { Id = id; }
        public uint Id { get; }
        public short Value { get; private set; }
        void ISplusParameter.Set(object v) => Value = unchecked((short)Convert.ToInt64(v ?? 0));
    }

    public class UIntParameter : ISplusParameter
    {
        public UIntParameter(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }
        public UIntParameter(uint id, uint arrayStart, SplusObject owner) { Id = id; }
        public uint Id { get; }
        public uint Value { get; private set; }
        void ISplusParameter.Set(object v) => Value = unchecked((uint)Convert.ToInt64(v ?? 0));
    }

    public class IntParameter : ISplusParameter
    {
        public IntParameter(uint id, SplusObject owner) : this(id, uint.MaxValue, owner) { }
        public IntParameter(uint id, uint arrayStart, SplusObject owner) { Id = id; }
        public uint Id { get; }
        public int Value { get; private set; }
        void ISplusParameter.Set(object v) => Value = unchecked((int)Convert.ToInt64(v ?? 0));
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class SplusStructAttribute : Attribute
    {
        public SplusStructAttribute(int order, bool a, bool b) { Order = order; }
        public int Order { get; }
    }

    /// <summary>Base of SIMPL+ STRUCTUREs, including the module's non-volatile variables.</summary>
    public class SplusStructureBase
    {
        public SplusStructureBase(SplusObject owner) : this(owner, false) { }
        public SplusStructureBase(SplusObject owner, bool flag) { Owner = owner; }
        public SplusObject Owner { get; }
        public uint PopulateCustomAttributeList(bool flag) => 0;
    }

    /// <summary>A pending WAIT.</summary>
    public class Wait
    {
        internal Wait(SplusObject owner, string name) { Owner = owner; Name = name; }
        public SplusObject Owner { get; }
        public string Name { get; }
        internal System.Threading.Timer Timer;
        internal readonly System.Threading.ManualResetEventSlim Done = new System.Threading.ManualResetEventSlim(false);
        public void RemoveFromList() => Owner?.RemoveWait(this);
    }

    public class SimplPlusException : Exception
    {
        public SimplPlusException(Exception inner, SplusObject owner) : base(inner?.Message, inner) { }
    }

    /// <summary>File search result for FindFirst/FindNext.</summary>
    public struct FILE_INFO
    {
        public CrestronString Name;
        public ushort iAttributes;
        public ushort iTime;
        public ushort iDate;
        public uint lSize;

        public void PopulateDefaults()
        {
            Name = new CrestronString("");
            iAttributes = 0; iTime = 0; iDate = 0; lSize = 0;
        }
    }
}
