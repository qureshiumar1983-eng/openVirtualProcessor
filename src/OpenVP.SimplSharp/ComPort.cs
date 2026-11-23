using System;
using System.Collections;
using System.Collections.Generic;
using OpenVP.Core;
using OpenVP.SimplSharp;

namespace Crestron.SimplSharpPro
{
    public enum eComBaudRates
    {
        ComspecBaudRate300 = 300, ComspecBaudRate600 = 600, ComspecBaudRate1200 = 1200, ComspecBaudRate1800 = 1800, ComspecBaudRate2400 = 2400,
        ComspecBaudRate3600 = 3600, ComspecBaudRate4800 = 4800, ComspecBaudRate7200 = 7200, ComspecBaudRate9600 = 9600, ComspecBaudRate14400 = 14400,
        ComspecBaudRate19200 = 19200, ComspecBaudRate28800 = 28800, ComspecBaudRate38400 = 38400, ComspecBaudRate57600 = 57600, ComspecBaudRate115200 = 115200,
    }
    public enum eComDataBits { ComspecDataBits7 = 7, ComspecDataBits8 = 8 }
    public enum eComParityType { ComspecParityNone = 0, ComspecParityOdd = 1, ComspecParityEven = 2 }
    public enum eComStopBits { ComspecStopBits1 = 1, ComspecStopBits2 = 2 }
    public enum eComProtocolType { ComspecProtocolRS232 = 0, ComspecProtocolRS422 = 1, ComspecProtocolRS485 = 2 }
    public enum eComHardwareHandshakeType { ComspecHardwareHandshakeNone = 0, ComspecHardwareHandshakeRTS = 1, ComspecHardwareHandshakeCTS = 2, ComspecHardwareHandshakeRTSCTS = 3 }
    public enum eComSoftwareHandshakeType { ComspecSoftwareHandshakeNone = 0, ComspecSoftwareHandshakeXON = 1, ComspecSoftwareHandshakeXONT = 2, ComspecSoftwareHandshakeXONR = 3 }
    public enum eComPortSetErrorCodes { Success = 0, Failure = 1 }

    public class ComPortSerialDataEventArgs : EventArgs
    {
        public ComPortSerialDataEventArgs(string data) { SerialData = data; }
        public string SerialData { get; }
    }

    public delegate void ComPortDataEventHandler(ComPort receivingComPort, ComPortSerialDataEventArgs args);

    /// <summary>A collection indexed by port number (1-based), as on a processor.</summary>
    public class CrestronCollection<T> : IEnumerable<T>
    {
        private readonly SortedDictionary<uint, T> _items = new SortedDictionary<uint, T>();
        internal void Add(uint index, T item) => _items[index] = item;
        public T this[uint index] => _items.TryGetValue(index, out var v) ? v : throw new ArgumentOutOfRangeException(nameof(index), $"There is no port {index}.");
        public int Count => _items.Count;
        public bool Contains(uint index) => _items.ContainsKey(index);
        public IEnumerator<T> GetEnumerator() => _items.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A COM port of the (virtual) control processor. OpenVP has no serial hardware: what the program sends is logged, shown in the
    /// slot's COM terminal and answered by a device simulator attached to the port ("COM-01" ...), if there is one. Received text
    /// arrives on <see cref="SerialDataReceived"/> on the slot's thread.
    /// </summary>
    public class ComPort : IDisposable
    {
        public struct ComPortSpec
        {
            public eComBaudRates BaudRate { get; set; }
            public eComDataBits DataBits { get; set; }
            public eComParityType Parity { get; set; }
            public eComStopBits StopBits { get; set; }
            public eComProtocolType Protocol { get; set; }
            public eComHardwareHandshakeType HardwareHandShake { get; set; }
            public eComSoftwareHandshakeType SoftwareHandshake { get; set; }
        }

        private PortChannel _channel;
        private ComPortSpec _spec;

        internal ComPort(uint number)
        {
            ID = number;
            Key = $"COM-{number:00}";
            Name = $"COM port {number}";
            OpenVPBridge.Track(this);
        }

        public uint ID { get; }
        public string Name { get; }
        public bool Registered { get; private set; }
        public string RegistrationFailureReason { get; private set; }
        internal string Key { get; }
        public ComPortSpec Spec => _spec;

        public event ComPortDataEventHandler SerialDataReceived;

        public eComPortSetErrorCodes SetComPortSpec(ComPortSpec spec) { _spec = spec; return eComPortSetErrorCodes.Success; }

        public eDeviceRegistrationUnRegistrationResponse Register()
        {
            if (Registered) return eDeviceRegistrationUnRegistrationResponse.DeviceAlreadyRegistered;
            var slot = OpenVPBridge.Slot;
            if (slot == null) { RegistrationFailureReason = "no slot"; return eDeviceRegistrationUnRegistrationResponse.Failure; }
            _channel = slot.Io.Ports.Open(Key, $"COM processor port {ID}", Received);
            Registered = true;
            return eDeviceRegistrationUnRegistrationResponse.Success;
        }

        public eDeviceRegistrationUnRegistrationResponse UnRegister()
        {
            if (!Registered) return eDeviceRegistrationUnRegistrationResponse.DeviceNotRegistered;
            Registered = false;
            OpenVPBridge.Slot?.Io.Ports.Close(Key);
            return eDeviceRegistrationUnRegistrationResponse.Success;
        }

        private void Received(string text)
        {
            OpenVPBridge.Post(() =>
            {
                if (!Registered) return;
                OpenVPBridge.Log("Info", $"COM processor port {ID} RX ({text.Length}): \"{SimText.Escape(text)}\"");
                SerialDataReceived?.Invoke(this, new ComPortSerialDataEventArgs(text));
            });
        }

        public void Send(string text)
        {
            if (string.IsNullOrEmpty(text) || !Registered || _channel == null) return;
            OpenVPBridge.Log("Info", $"COM processor port {ID} TX ({text.Length}): \"{SimText.Escape(text)}\"");
            _channel.FromProgram(text);
        }

        public void Send(byte[] data, int length) => Send(System.Text.Encoding.Latin1.GetString(data, 0, length));

        public void Dispose() { UnRegister(); OpenVPBridge.Untrack(this); }
    }
}
