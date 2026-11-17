using System;
using System.Collections.Generic;
using System.Linq;

// SSH client API in the SIMPL# shape, on OpenVP's own SSH-2 transport (SshTransport.cs).

namespace Crestron.SimplSharp.Ssh.Messages.Transport
{
    public enum DisconnectReason
    {
        None = 0, HostNotAllowedToConnect = 1, ProtocolError = 2, KeyExchangeFailed = 3, Reserved = 4,
        MacError = 5, CompressionError = 6, ServiceNotAvailable = 7, ProtocolVersionNotSupported = 8,
        HostKeyNotVerifiable = 9, ConnectionLost = 10, ByApplication = 11, TooManyConnections = 12,
        AuthenticationCanceledByUser = 13, NoMoreAuthenticationMethodsAvailable = 14, IllegalUserName = 15
    }
}

namespace Crestron.SimplSharp.Ssh.Common
{
    using Crestron.SimplSharp.Ssh.Messages.Transport;

    public class SshException : Exception
    {
        public SshException() { }
        public SshException(string message) : base(message) { }
        public SshException(string message, Exception inner) : base(message, inner) { }
    }

    public class SshConnectionException : SshException
    {
        public SshConnectionException() { }
        public SshConnectionException(string message) : base(message) { }
        public SshConnectionException(string message, DisconnectReason reason) : base(message) { DisconnectReason = reason; }
        public DisconnectReason DisconnectReason { get; private set; }
    }

    public class SshOperationTimeoutException : SshException
    {
        public SshOperationTimeoutException() { }
        public SshOperationTimeoutException(string message) : base(message) { }
    }

    public class SshAuthenticationException : SshException
    {
        public SshAuthenticationException(string message) : base(message) { }
    }

    public class ExceptionEventArgs : EventArgs
    {
        public ExceptionEventArgs(Exception exception) { Exception = exception; }
        public Exception Exception { get; private set; }
    }

    public class HostKeyEventArgs : EventArgs
    {
        public HostKeyEventArgs(byte[] hostKey, byte[] fingerPrint) { HostKey = hostKey; FingerPrint = fingerPrint; CanTrust = true; }
        public byte[] HostKey { get; private set; }
        public byte[] FingerPrint { get; private set; }
        public string HostKeyName { get; set; }
        public bool CanTrust { get; set; }
    }

    public class AuthenticationPrompt
    {
        public AuthenticationPrompt(int id, bool isEchoed, string request) { Id = id; IsEchoed = isEchoed; Request = request; }
        public int Id { get; private set; }
        public bool IsEchoed { get; private set; }
        public string Request { get; private set; }
        public string Response { get; set; }
    }

    public class AuthenticationPromptEventArgs : EventArgs
    {
        public AuthenticationPromptEventArgs(string username, string instruction, string language, IEnumerable<AuthenticationPrompt> prompts)
        { Username = username; Instruction = instruction; Language = language; Prompts = prompts; }
        public string Username { get; private set; }
        public string Instruction { get; private set; }
        public string Language { get; private set; }
        public IEnumerable<AuthenticationPrompt> Prompts { get; private set; }
    }

    public class ShellDataEventArgs : EventArgs
    {
        public ShellDataEventArgs(byte[] data) { Data = data; Line = System.Text.Encoding.UTF8.GetString(data); }
        public ShellDataEventArgs(string line) { Line = line; Data = System.Text.Encoding.UTF8.GetBytes(line); }
        public byte[] Data { get; private set; }
        public string Line { get; private set; }
    }
}

namespace Crestron.SimplSharp.Ssh
{
    using Crestron.SimplSharp.Ssh.Common;
    using Crestron.SimplSharp.Ssh.Messages.Transport;

    public abstract class AuthenticationMethod : IDisposable
    {
        protected AuthenticationMethod(string username) { Username = username; }
        public string Username { get; private set; }
        public abstract string Name { get; }
        public virtual void Dispose() { }
    }

    public class PasswordAuthenticationMethod : AuthenticationMethod
    {
        public PasswordAuthenticationMethod(string username, string password) : base(username) { Password = password; }
        internal string Password { get; private set; }
        public override string Name => "password";
    }

    public class KeyboardInteractiveAuthenticationMethod : AuthenticationMethod
    {
        public KeyboardInteractiveAuthenticationMethod(string username) : base(username) { }
        public override string Name => "keyboard-interactive";
        public event EventHandler<AuthenticationPromptEventArgs> AuthenticationPrompt;
        internal void Raise(AuthenticationPromptEventArgs e) => AuthenticationPrompt?.Invoke(this, e);
    }

    public class ConnectionInfo : IDisposable
    {
        public void Dispose() { }
        public ConnectionInfo(string host, string username, params AuthenticationMethod[] methods) : this(host, 22, username, methods) { }
        public ConnectionInfo(string host, int port, string username, params AuthenticationMethod[] methods)
        { Host = host; Port = port; Username = username; AuthenticationMethods = methods ?? Array.Empty<AuthenticationMethod>(); }
        public string Host { get; private set; }
        public int Port { get; private set; }
        public string Username { get; private set; }
        public AuthenticationMethod[] AuthenticationMethods { get; private set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public IDictionary<string, Type> KeyExchangeAlgorithms { get; } = new Dictionary<string, Type>();
        public IDictionary<string, Type> HostKeyAlgorithms { get; } = new Dictionary<string, Type>();
        public IDictionary<string, Type> Encryptions { get; } = new Dictionary<string, Type>();
        public IDictionary<string, Type> HmacAlgorithms { get; } = new Dictionary<string, Type>();
    }

    public abstract class BaseClient : IDisposable
    {
        internal OpenVP.SimplSharp.SshCore.SshConnection Connection;

        protected BaseClient(ConnectionInfo info) { ConnectionInfo = info; }
        public ConnectionInfo ConnectionInfo { get; private set; }
        public virtual bool IsConnected => Connection?.IsConnected == true;
        public TimeSpan KeepAliveInterval { get; set; }
        public event EventHandler<ExceptionEventArgs> ErrorOccurred;
        public event EventHandler<HostKeyEventArgs> HostKeyReceived;

        public virtual void Connect()
        {
            var info = ConnectionInfo;
            var password = info.AuthenticationMethods.OfType<PasswordAuthenticationMethod>().FirstOrDefault()?.Password;
            var kbd = info.AuthenticationMethods.OfType<KeyboardInteractiveAuthenticationMethod>().FirstOrDefault();
            var conn = new OpenVP.SimplSharp.SshCore.SshConnection();
            conn.HostKeyCheck = (key, fingerprint) =>
            {
                var e = new HostKeyEventArgs(key, fingerprint);
                HostKeyReceived?.Invoke(this, e);
                return e.CanTrust;
            };
            if (kbd != null)
                conn.KeyboardInteractive = (name, instruction, prompts) =>
                {
                    var list = prompts.Select((p, i) => new AuthenticationPrompt(i, p.echo, p.prompt)).ToList();
                    kbd.Raise(new AuthenticationPromptEventArgs(info.Username, instruction, "", list));
                    return list.Select(p => p.Response ?? password ?? "").ToList();
                };
            conn.Closed += e => OnError(e);
            try
            {
                conn.Connect(info.Host, info.Port, info.Username, password, kbd != null || password != null, info.Timeout);
            }
            catch (Exception e)
            {
                conn.Dispose();
                throw new SshConnectionException(Environment.GetEnvironmentVariable("OPENVP_SSH_DEBUG") == "1" ? e.ToString() : e.Message, DisconnectReason.ConnectionLost);
            }
            Connection = conn;
        }

        public virtual void Disconnect() { Connection?.Dispose(); Connection = null; }
        public void SendKeepAlive() { }
        protected void OnError(Exception e) => ErrorOccurred?.Invoke(this, new ExceptionEventArgs(e));
        protected void OnHostKey(HostKeyEventArgs e) => HostKeyReceived?.Invoke(this, e);
        public virtual void Dispose() { Disconnect(); }
    }

    public class ShellStream : IDisposable
    {
        private readonly OpenVP.SimplSharp.SshCore.SshConnection _conn;
        private readonly System.Text.StringBuilder _buffer = new System.Text.StringBuilder();

        internal ShellStream(OpenVP.SimplSharp.SshCore.SshConnection conn)
        {
            _conn = conn;
            conn.DataReceived += data =>
            {
                lock (_buffer) _buffer.Append(System.Text.Encoding.Latin1.GetString(data));
                DataReceived?.Invoke(this, new ShellDataEventArgs(data));
            };
            conn.Closed += e => ErrorOccurred?.Invoke(this, new ExceptionEventArgs(e));
        }

        public event EventHandler<ShellDataEventArgs> DataReceived;
        public event EventHandler<ExceptionEventArgs> ErrorOccurred;
        public bool DataAvailable { get { lock (_buffer) return _buffer.Length > 0; } }
        public long Length { get { lock (_buffer) return _buffer.Length; } }

        public string Read()
        {
            lock (_buffer) { var s = _buffer.ToString(); _buffer.Clear(); return s; }
        }

        public string ReadLine()
        {
            lock (_buffer)
            {
                var s = _buffer.ToString();
                var i = s.IndexOf('\n');
                if (i < 0) return null;
                _buffer.Remove(0, i + 1);
                return s.Substring(0, i).TrimEnd('\r');
            }
        }

        public void Write(string text)
        {
            if (!_conn.IsConnected) throw new SshConnectionException("Not connected.", DisconnectReason.ConnectionLost);
            _conn.Send(System.Text.Encoding.Latin1.GetBytes(text ?? ""));
        }

        public void WriteLine(string line) => Write(line + "\r\n");
        public void Flush() { }
        public void Close() { }
        public void Dispose() { }
    }

    public class SshClient : BaseClient
    {
        public SshClient(ConnectionInfo info) : base(info) { }
        public SshClient(string host, int port, string username, string password)
            : base(new ConnectionInfo(host, port, username, new PasswordAuthenticationMethod(username, password))) { }
        public SshClient(string host, string username, string password) : this(host, 22, username, password) { }

        public ShellStream CreateShellStream(string terminalName, uint columns, uint rows, uint width, uint height, int bufferSize)
        {
            if (!IsConnected) throw new SshConnectionException("Client not connected.", DisconnectReason.ConnectionLost);
            Connection.OpenShell(terminalName, columns, rows, width, height);
            return new ShellStream(Connection);
        }
    }
}
