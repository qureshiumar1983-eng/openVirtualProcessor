using System;
using System.Collections.Generic;
using OpenVP.Core;
using OpenVP.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;

namespace Crestron.SimplSharpPro
{
    public enum eDeviceRegistrationUnRegistrationResponse
    {
        Success = 0,
        Failure = 1,
        ProductIdNotFound = 2,
        ParentRegistered = 3,
        ParentNotRegistered = 4,
        NoAvailableSlots = 5,
        DeviceAlreadyRegistered = 6,
        DeviceNotRegistered = 7,
        InvalidId = 8
    }

    public enum eSigType { NA = 0, Bool = 1, UShort = 2, String = 3 }

    public enum eSigEvent { NA = 0, BoolChange = 1, UShortChange = 2, StringChange = 3 }

    public class OnlineOfflineEventArgs : EventArgs
    {
        public OnlineOfflineEventArgs(bool online) { DeviceOnLine = online; }
        public bool DeviceOnLine { get; }
    }

    public delegate void OnlineStatusChangeEventHandler(GenericBase currentDevice, OnlineOfflineEventArgs args);

    /// <summary>Base of every device a program can register.</summary>
    public abstract class GenericBase
    {
        protected GenericBase(uint id, string description)
        {
            ID = id;
            Description = description;
            Name = GetType().Name;
        }

        public uint ID { get; }
        public virtual string Description { get; set; }
        public string Name { get; protected set; }
        public bool Registered { get; private set; }
        public virtual bool IsOnline => false;
        public string RegistrationFailureReason { get; private set; }

        public event OnlineStatusChangeEventHandler OnlineStatusChange;

        public eDeviceRegistrationUnRegistrationResponse Register()
        {
            if (Registered) return eDeviceRegistrationUnRegistrationResponse.DeviceAlreadyRegistered;
            var result = OnRegister();
            Registered = result == eDeviceRegistrationUnRegistrationResponse.Success;
            if (!Registered) RegistrationFailureReason = result.ToString();
            return result;
        }

        public eDeviceRegistrationUnRegistrationResponse UnRegister()
        {
            if (!Registered) return eDeviceRegistrationUnRegistrationResponse.DeviceNotRegistered;
            OnUnregister();
            Registered = false;
            return eDeviceRegistrationUnRegistrationResponse.Success;
        }

        protected virtual eDeviceRegistrationUnRegistrationResponse OnRegister() => eDeviceRegistrationUnRegistrationResponse.Success;
        protected virtual void OnUnregister() { }

        protected void RaiseOnline(bool online) => OnlineStatusChange?.Invoke(this, new OnlineOfflineEventArgs(online));

        public override string ToString() => $"{Name} ID {ID:X2}";
    }

    public abstract class GenericDevice : GenericBase
    {
        protected GenericDevice(uint id, string description) : base(id, description) { }
    }

    // ------------------------------------------------------------------ sigs

    public abstract class Sig
    {
        internal Sig(BasicTriList owner, eSigType type, uint number)
        {
            Owner = owner;
            Type = type;
            Number = number;
        }

        internal BasicTriList Owner { get; }
        public uint Number { get; }
        public eSigType Type { get; }
        public string Name { get; set; } = string.Empty;
        public object UserObject { get; set; }

        /// <summary>For output sigs: the value delivered with the most recent SigChange, so handlers see
        /// the value the event carried even if newer input is already queued.</summary>
        internal object Delivered { get; set; }

        internal JoinKey Key => new JoinKey(Type switch
        {
            eSigType.Bool => SigKind.Digital,
            eSigType.UShort => SigKind.Analog,
            _ => SigKind.Serial
        }, Number);

        public virtual bool BoolValue { get => false; set => throw new InvalidOperationException("This sig is read-only."); }
        public virtual ushort UShortValue { get => 0; set => throw new InvalidOperationException("This sig is read-only."); }
        public virtual string StringValue { get => string.Empty; set => throw new InvalidOperationException("This sig is read-only."); }
        public short ShortValue { get => unchecked((short)UShortValue); set => UShortValue = unchecked((ushort)value); }

        public override string ToString() => $"{Type} {Number} = {(Type == eSigType.Bool ? BoolValue : Type == eSigType.UShort ? UShortValue : (object)StringValue)}";
    }

    /// <summary>Program to device (feedback). Setting the value updates the panel.</summary>
    public class BoolInputSig : Sig
    {
        internal BoolInputSig(BasicTriList o, uint n) : base(o, eSigType.Bool, n) { }
        public override bool BoolValue
        {
            get => (bool)Owner.Feedback(Key);
            set => Owner.SetFeedback(Key, value);
        }

        public void Pulse() => Pulse(100);

        public void Pulse(int ms)
        {
            BoolValue = true;
            var timer = default(Crestron.SimplSharp.CTimer);
            timer = new Crestron.SimplSharp.CTimer(_ => { BoolValue = false; timer?.Dispose(); }, ms);
        }
    }

    public class UShortInputSig : Sig
    {
        internal UShortInputSig(BasicTriList o, uint n) : base(o, eSigType.UShort, n) { }
        public override ushort UShortValue
        {
            get => (ushort)Owner.Feedback(Key);
            set => Owner.SetFeedback(Key, value);
        }
    }

    public class StringInputSig : Sig
    {
        internal StringInputSig(BasicTriList o, uint n) : base(o, eSigType.String, n) { }
        public override string StringValue
        {
            get => (string)Owner.Feedback(Key);
            set => Owner.SetFeedback(Key, value ?? string.Empty);
        }
    }

    /// <summary>Device to program (press, value, text). Read-only.</summary>
    public class BoolOutputSig : Sig
    {
        internal BoolOutputSig(BasicTriList o, uint n) : base(o, eSigType.Bool, n) { }
        public override bool BoolValue { get => (bool)(Delivered ?? Owner.Input(Key)); set => throw new InvalidOperationException("Output sigs are read-only."); }
    }

    public class UShortOutputSig : Sig
    {
        internal UShortOutputSig(BasicTriList o, uint n) : base(o, eSigType.UShort, n) { }
        public override ushort UShortValue { get => (ushort)(Delivered ?? Owner.Input(Key)); set => throw new InvalidOperationException("Output sigs are read-only."); }
    }

    public class StringOutputSig : Sig
    {
        internal StringOutputSig(BasicTriList o, uint n) : base(o, eSigType.String, n) { }
        public override string StringValue { get => (string)(Delivered ?? Owner.Input(Key)); set => throw new InvalidOperationException("Output sigs are read-only."); }
    }

    public class SigEventArgs : EventArgs
    {
        public SigEventArgs(Sig sig, eSigEvent evt) { Sig = sig; Event = evt; }
        public Sig Sig { get; }
        public eSigEvent Event { get; }
    }

    /// <summary>Indexed by join number (1-based), created on first use.</summary>
    public abstract class SigCollectionBase<T> : IEnumerable<T> where T : Sig
    {
        private readonly Dictionary<uint, T> _sigs = new Dictionary<uint, T>();
        private readonly Func<uint, T> _factory;
        private readonly uint _max;

        internal SigCollectionBase(Func<uint, T> factory, uint max) { _factory = factory; _max = max; }

        public T this[uint join]
        {
            get
            {
                if (join == 0 || join > _max) throw new IndexOutOfRangeException($"Join {join} is outside 1–{_max}.");
                lock (_sigs)
                {
                    if (!_sigs.TryGetValue(join, out var s)) _sigs[join] = s = _factory(join);
                    return s;
                }
            }
        }

        public uint Count => _max;

        public IEnumerator<T> GetEnumerator()
        {
            for (uint i = 1; i <= _max; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class DeviceBooleanInputCollection : SigCollectionBase<BoolInputSig> { internal DeviceBooleanInputCollection(Func<uint, BoolInputSig> f, uint m) : base(f, m) { } }
    public class DeviceUShortInputCollection : SigCollectionBase<UShortInputSig> { internal DeviceUShortInputCollection(Func<uint, UShortInputSig> f, uint m) : base(f, m) { } }
    public class DeviceStringInputCollection : SigCollectionBase<StringInputSig> { internal DeviceStringInputCollection(Func<uint, StringInputSig> f, uint m) : base(f, m) { } }
    public class DeviceBooleanOutputCollection : SigCollectionBase<BoolOutputSig> { internal DeviceBooleanOutputCollection(Func<uint, BoolOutputSig> f, uint m) : base(f, m) { } }
    public class DeviceUShortOutputCollection : SigCollectionBase<UShortOutputSig> { internal DeviceUShortOutputCollection(Func<uint, UShortOutputSig> f, uint m) : base(f, m) { } }
    public class DeviceStringOutputCollection : SigCollectionBase<StringOutputSig> { internal DeviceStringOutputCollection(Func<uint, StringOutputSig> f, uint m) : base(f, m) { } }
}

namespace Crestron.SimplSharpPro.DeviceSupport
{
    using Crestron.SimplSharpPro;

    public delegate void SigEventHandler(BasicTriList currentDevice, SigEventArgs args);

    /// <summary>
    /// A device with digital, analog and serial joins in both directions: touch panels, XPanels and EISCs.
    /// Inputs = program to device (feedback); outputs = device to program (presses).
    /// </summary>
    public abstract class BasicTriList : GenericDevice
    {
        public const uint MaxJoins = 65535;
        private readonly CrestronControlSystem _cs;
        private JoinDevice _device;

        protected BasicTriList(uint ipId, CrestronControlSystem controlSystem, string description = null)
            : base(ipId, description ?? string.Empty)
        {
            _cs = controlSystem;
            BooleanInput = new DeviceBooleanInputCollection(n => new BoolInputSig(this, n), MaxJoins);
            UShortInput = new DeviceUShortInputCollection(n => new UShortInputSig(this, n), MaxJoins);
            StringInput = new DeviceStringInputCollection(n => new StringInputSig(this, n), MaxJoins);
            BooleanOutput = new DeviceBooleanOutputCollection(n => new BoolOutputSig(this, n), MaxJoins);
            UShortOutput = new DeviceUShortOutputCollection(n => new UShortOutputSig(this, n), MaxJoins);
            StringOutput = new DeviceStringOutputCollection(n => new StringOutputSig(this, n), MaxJoins);
        }

        public DeviceBooleanInputCollection BooleanInput { get; }
        public DeviceUShortInputCollection UShortInput { get; }
        public DeviceStringInputCollection StringInput { get; }
        public DeviceBooleanOutputCollection BooleanOutput { get; }
        public DeviceUShortOutputCollection UShortOutput { get; }
        public DeviceStringOutputCollection StringOutput { get; }

        public CrestronControlSystem ControlSystem => _cs;

        public event SigEventHandler SigChange;

        public override bool IsOnline => _device?.Online ?? false;

        /// <summary>Device type name shown in the IP table.</summary>
        protected virtual string DeviceTypeName => GetType().Name;

        /// <summary>The OpenVP device backing this object (null until registered).</summary>
        protected JoinDevice Device => _device;

        protected override eDeviceRegistrationUnRegistrationResponse OnRegister()
        {
            if (ID == 0 || ID > 0xFE) return eDeviceRegistrationUnRegistrationResponse.InvalidId;
            var slot = OpenVPBridge.RequireSlot;
            _device = slot.RegisterDevice(ID, DeviceTypeName, Description);
            _device.InputReceived += OnInputFromDevice;
            _device.OnlineChanged += OnDeviceOnline;
            AfterRegister(slot);
            return eDeviceRegistrationUnRegistrationResponse.Success;
        }

        protected virtual void AfterRegister(ProgramSlot slot) { }

        protected override void OnUnregister()
        {
            if (_device == null) return;
            _device.InputReceived -= OnInputFromDevice;
            _device.OnlineChanged -= OnDeviceOnline;
            BeforeUnregister(OpenVPBridge.Slot);
            OpenVPBridge.Slot?.UnregisterDevice(ID);
            _device = null;
        }

        protected virtual void BeforeUnregister(ProgramSlot slot) { }

        internal object Feedback(JoinKey key) => _device?.GetFeedback(key) ?? SigValue.Default(key.Kind);

        internal object Input(JoinKey key) => _device?.GetInput(key) ?? SigValue.Default(key.Kind);

        internal void SetFeedback(JoinKey key, object value)
        {
            if (_device == null)
            {
                OpenVPBridge.Log("Warning", $"{this}: set {key} before Register(); value ignored.");
                return;
            }
            _device.SetFeedback(key, value);
            // Only build the trace name when someone is listening.
            if (OpenVPBridge.Slot is { TraceWanted: true } slot) slot.Trace($"IPID{ID:X2}.in.{key}", key.Kind, value, "program");
        }

        private void OnInputFromDevice(JoinDevice dev, JoinKey key, object value)
        {
            if (OpenVPBridge.Slot is { TraceWanted: true } slot) slot.Trace($"IPID{ID:X2}.out.{key}", key.Kind, value, "device");
            OpenVPBridge.Post(() =>
            {
                Sig sig = key.Kind switch
                {
                    SigKind.Digital => BooleanOutput[key.Join],
                    SigKind.Analog => UShortOutput[key.Join],
                    _ => StringOutput[key.Join]
                };
                sig.Delivered = value;
                var evt = key.Kind switch
                {
                    SigKind.Digital => eSigEvent.BoolChange,
                    SigKind.Analog => eSigEvent.UShortChange,
                    _ => eSigEvent.StringChange
                };
                SigChange?.Invoke(this, new SigEventArgs(sig, evt));
            });
        }

        private void OnDeviceOnline(JoinDevice dev, bool online) => OpenVPBridge.Post(() => RaiseOnline(online));
    }

    public abstract class BasicTriListWithSmartObject : BasicTriList
    {
        protected BasicTriListWithSmartObject(uint ipId, CrestronControlSystem cs, string description = null)
            : base(ipId, cs, description) { }

        /// <summary>Smart objects are not implemented yet. The collection is always empty.</summary>
        public IDictionary<uint, object> SmartObjects { get; } = new Dictionary<uint, object>();

        public int LoadSmartObjects(string sgdFilePath)
        {
            OpenVPBridge.Log("Warning", $"{this}: smart objects are not supported yet (LoadSmartObjects '{sgdFilePath}' ignored).");
            return 0;
        }
    }
}
