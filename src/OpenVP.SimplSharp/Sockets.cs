using System;
using System.Net.Sockets;
using OpenVP.SimplSharp;

namespace Crestron.SimplSharp.CrestronSockets
{
    public enum SocketStatus
    {
        SOCKET_STATUS_NO_CONNECT = 0,
        SOCKET_STATUS_WAITING = 1,
        SOCKET_STATUS_CONNECTED = 2,
        SOCKET_STATUS_CONNECT_FAILED = 3,
        SOCKET_STATUS_BROKEN_REMOTELY = 4,
        SOCKET_STATUS_BROKEN_LOCALLY = 5,
        SOCKET_STATUS_DNS_LOOKUP = 6,
        SOCKET_STATUS_DNS_FAILED = 7,
        SOCKET_STATUS_DNS_RESOLVED = 8,
        SOCKET_STATUS_LINK_LOST = 9,
        SOCKET_STATUS_SOCKET_NOT_EXIST = 10
    }

    public enum SocketErrorCodes
    {
        SOCKET_OK = 0,
        SOCKET_OPERATION_PENDING = 1,
        SOCKET_MAX_CONNECTIONS_REACHED = 2,
        SOCKET_CONNECTION_IN_PROGRESS = 3,
        SOCKET_NOT_CONNECTED = 4,
        SOCKET_INVALID_STATE = 5,
        SOCKET_CONNECT_FAILED = 6,
        SOCKET_INVALID_ADDRESS_ADAPTER_BINDING = 7,
        SOCKET_INVALID_PORT_NUMBER = 8,
        SOCKET_NO_HOSTNAME_RESOLVE = 9,
        SOCKET_BUFFER_NOT_ALLOCATED = 10,
        SOCKET_SPECIFIED_INDEX_NOT_FOUND = 11,
        SOCKET_INVALID_CLIENT_INDEX = 12
    }

    public delegate void TCPClientConnectCallback(TCPClient myTCPClient);
    public delegate void TCPClientReceiveCallback(TCPClient myTCPClient, int numberOfBytesReceived);
    public delegate void TCPClientSendCallback(TCPClient myTCPClient, int numberOfBytesSent);
    public delegate void TCPClientSocketStatusChangeEventHandler(TCPClient myTCPClient, SocketStatus clientSocketStatus);

    /// <summary>TCP client with the SIMPL# API shape. Callbacks run on the slot thread.</summary>
    public class TCPClient : IDisposable
    {
        private System.Net.Sockets.TcpClient _client;
        private NetworkStream _stream;
        private SocketStatus _status = SocketStatus.SOCKET_STATUS_NO_CONNECT;

        public TCPClient(string addressToConnectTo, int portNumber, int bufferSize)
        {
            AddressClientConnectedTo = addressToConnectTo;
            PortNumber = portNumber;
            IncomingDataBuffer = new byte[Math.Max(bufferSize, 1)];
            OpenVPBridge.Track(this);
        }

        public TCPClient(string addressToConnectTo, int portNumber, int bufferSize, Crestron.SimplSharp.EthernetAdapterType adapter)
            : this(addressToConnectTo, portNumber, bufferSize) { }

        public string AddressClientConnectedTo { get; set; }
        public int PortNumber { get; set; }
        public byte[] IncomingDataBuffer { get; }
        public bool Nagle { get; set; } = true;
        public int SocketSendOrReceiveTimeOutInMs { get; set; }
        public SocketStatus ClientStatus => _status;
        public bool DataAvailable => _stream?.DataAvailable ?? false;

        public event TCPClientSocketStatusChangeEventHandler SocketStatusChange;

        public SocketErrorCodes ConnectToServer()
        {
            try
            {
                Open();
                _client.Connect(AddressClientConnectedTo, PortNumber);
                Connected();
                return SocketErrorCodes.SOCKET_OK;
            }
            catch (Exception)
            {
                SetStatus(SocketStatus.SOCKET_STATUS_CONNECT_FAILED);
                return SocketErrorCodes.SOCKET_CONNECT_FAILED;
            }
        }

        public SocketErrorCodes ConnectToServerAsync(TCPClientConnectCallback callback)
        {
            if (_status == SocketStatus.SOCKET_STATUS_CONNECTED) return SocketErrorCodes.SOCKET_INVALID_STATE;
            Open();
            SetStatus(SocketStatus.SOCKET_STATUS_WAITING);
            _client.ConnectAsync(AddressClientConnectedTo, PortNumber).ContinueWith(t =>
            {
                if (t.IsFaulted) SetStatus(SocketStatus.SOCKET_STATUS_CONNECT_FAILED);
                else Connected();
                if (callback != null) OpenVPBridge.Post(() => callback(this));
            });
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        public SocketErrorCodes DisconnectFromServer()
        {
            Close(SocketStatus.SOCKET_STATUS_BROKEN_LOCALLY);
            return SocketErrorCodes.SOCKET_OK;
        }

        public SocketErrorCodes SendData(byte[] dataToSend, int lengthToSend) => SendData(dataToSend, 0, lengthToSend);

        public SocketErrorCodes SendData(byte[] dataToSend, int offset, int lengthToSend)
        {
            if (_stream == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            try { _stream.Write(dataToSend, offset, lengthToSend); return SocketErrorCodes.SOCKET_OK; }
            catch (Exception) { Close(SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY); return SocketErrorCodes.SOCKET_NOT_CONNECTED; }
        }

        public SocketErrorCodes SendDataAsync(byte[] dataToSend, int lengthToSend, TCPClientSendCallback callback)
        {
            var r = SendData(dataToSend, lengthToSend);
            if (r == SocketErrorCodes.SOCKET_OK && callback != null) OpenVPBridge.Post(() => callback(this, lengthToSend));
            return r;
        }

        /// <summary>Blocking read. Returns bytes read; 0 means the connection closed.</summary>
        public int ReceiveData()
        {
            if (_stream == null) return 0;
            try
            {
                var n = _stream.Read(IncomingDataBuffer, 0, IncomingDataBuffer.Length);
                if (n == 0) Close(SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY);
                return n;
            }
            catch (Exception) { Close(SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY); return 0; }
        }

        /// <summary>Read once in the background; the callback gets the byte count (0 = closed).</summary>
        public SocketErrorCodes ReceiveDataAsync(TCPClientReceiveCallback callback)
        {
            if (_stream == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            _stream.ReadAsync(IncomingDataBuffer, 0, IncomingDataBuffer.Length).ContinueWith(t =>
            {
                var n = t.IsFaulted || t.IsCanceled ? 0 : t.Result;
                if (n == 0) Close(SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY);
                OpenVPBridge.Post(() => callback?.Invoke(this, n));
            });
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        public void Dispose()
        {
            Close(SocketStatus.SOCKET_STATUS_NO_CONNECT);
            OpenVPBridge.Untrack(this);
        }

        private void Open()
        {
            _client?.Dispose();
            _client = new System.Net.Sockets.TcpClient { NoDelay = !Nagle };
        }

        private void Connected()
        {
            _stream = _client.GetStream();
            SetStatus(SocketStatus.SOCKET_STATUS_CONNECTED);
        }

        private void Close(SocketStatus status)
        {
            var wasOpen = _client != null;
            _stream = null;
            try { _client?.Dispose(); } catch { }
            _client = null;
            if (wasOpen) SetStatus(status);
        }

        private void SetStatus(SocketStatus s)
        {
            if (_status == s) return;
            _status = s;
            OpenVPBridge.Post(() => SocketStatusChange?.Invoke(this, s));
        }
    }
}
