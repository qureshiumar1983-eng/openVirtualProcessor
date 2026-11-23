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
        public SshAuthenticationException(string message, Exception inner) : base(message, inner) { }
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
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Crestron.SimplSharp.Ssh.Common;
    using Crestron.SimplSharp.Ssh.Messages.Transport;
    using OpenVP.SimplSharp.SshCore;

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
        public PasswordAuthenticationMethod(string username, byte[] password) : base(username) { Password = password == null ? null : Encoding.UTF8.GetString(password); }
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

    /// <summary>A private key for public-key login: an RSA or ECDSA key in PEM form (PKCS#1, PKCS#8, SEC1, or an unencrypted OpenSSH file).</summary>
    public class PrivateKeyFile : IDisposable
    {
        internal SshKey Key { get; private set; }

        public PrivateKeyFile(string fileName) : this(fileName, null) { }
        public PrivateKeyFile(string fileName, string passPhrase)
        {
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));
            Load(System.IO.File.ReadAllText(Crestron.SimplSharp.CrestronIO.Paths.Map(fileName)), passPhrase);
        }
        public PrivateKeyFile(Crestron.SimplSharp.CrestronIO.Stream privateKey) : this(privateKey, null) { }
        public PrivateKeyFile(Crestron.SimplSharp.CrestronIO.Stream privateKey, string passPhrase)
        {
            using var r = new System.IO.StreamReader(privateKey.Inner, Encoding.ASCII, false, 4096, leaveOpen: true);
            Load(r.ReadToEnd(), passPhrase);
        }

        private void Load(string text, string pass)
        {
            try { Key = SshKey.Parse(text, pass); }
            catch (Exception e) when (e is System.IO.InvalidDataException or NotSupportedException or FormatException)
            { throw new SshException("The private key couldn't be used: " + e.Message, e); }
        }

        /// <summary>"ssh-rsa" or "ecdsa-sha2-nistp256" and so on.</summary>
        public string HostKeyAlgorithm => Key.KeyType;
        public void Dispose() { }
    }

    public class PrivateKeyAuthenticationMethod : AuthenticationMethod
    {
        public PrivateKeyAuthenticationMethod(string username, params PrivateKeyFile[] keyFiles) : base(username) { KeyFiles = keyFiles ?? Array.Empty<PrivateKeyFile>(); }
        public ICollection<PrivateKeyFile> KeyFiles { get; private set; }
        public override string Name => "publickey";
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
        /// <summary>The server's identification string ("SSH-2.0-...") once connected.</summary>
        public string ServerVersion { get; internal set; }
        public string ClientVersion => "SSH-2.0-OpenVP_1.0";
        public IDictionary<string, Type> KeyExchangeAlgorithms { get; } = new Dictionary<string, Type>();
        public IDictionary<string, Type> HostKeyAlgorithms { get; } = new Dictionary<string, Type>();
        public IDictionary<string, Type> Encryptions { get; } = new Dictionary<string, Type>();
        public IDictionary<string, Type> HmacAlgorithms { get; } = new Dictionary<string, Type>();
        /// <summary>The pre-login banner a server sent.</summary>
        public event EventHandler<AuthenticationBannerEventArgs> AuthenticationBanner;
        internal void RaiseBanner(string text) => AuthenticationBanner?.Invoke(this, new AuthenticationBannerEventArgs(Username, text));
    }

    public class AuthenticationBannerEventArgs : EventArgs
    {
        public AuthenticationBannerEventArgs(string username, string banner) { Username = username; BannerMessage = banner; }
        public string Username { get; private set; }
        public string BannerMessage { get; private set; }
    }

    public abstract class BaseClient : IDisposable
    {
        internal SshConnection Connection;
        private Timer _keepAlive;
        private TimeSpan _keepAliveInterval;

        protected BaseClient(ConnectionInfo info) { ConnectionInfo = info; }
        public ConnectionInfo ConnectionInfo { get; private set; }
        public virtual bool IsConnected => Connection?.IsConnected == true;
        /// <summary>Send a keep-alive this often while connected (zero: never).</summary>
        public TimeSpan KeepAliveInterval
        {
            get => _keepAliveInterval;
            set { _keepAliveInterval = value; ArmKeepAlive(); }
        }
        public event EventHandler<ExceptionEventArgs> ErrorOccurred;
        public event EventHandler<HostKeyEventArgs> HostKeyReceived;

        private void ArmKeepAlive()
        {
            _keepAlive?.Dispose(); _keepAlive = null;
            if (_keepAliveInterval > TimeSpan.Zero && IsConnected) _keepAlive = new Timer(_ => SendKeepAlive(), null, _keepAliveInterval, _keepAliveInterval);
        }

        public virtual void Connect()
        {
            var info = ConnectionInfo;
            var fallbackPassword = info.AuthenticationMethods.OfType<PasswordAuthenticationMethod>().FirstOrDefault()?.Password;
            var kbd = info.AuthenticationMethods.OfType<KeyboardInteractiveAuthenticationMethod>().FirstOrDefault();
            var conn = new SshConnection();
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
                    return list.Select(p => p.Response ?? fallbackPassword ?? "").ToList();
                };
            conn.Banner += text => info.RaiseBanner(text);
            conn.Closed += e => OnError(e);
            try
            {
                conn.Open(info.Host, info.Port, info.Timeout);
                info.ServerVersion = conn.ServerVersion;
                var tried = new List<string>();
                var ok = false;
                foreach (var m in info.AuthenticationMethods)
                {
                    tried.Add(m.Name);
                    switch (m)
                    {
                        case PasswordAuthenticationMethod p: ok = conn.AuthPassword(p.Username, p.Password); break;
                        case KeyboardInteractiveAuthenticationMethod k: ok = conn.AuthKeyboardInteractive(k.Username, fallbackPassword); break;
                        case PrivateKeyAuthenticationMethod pk: ok = pk.KeyFiles.Any(f => conn.AuthPublicKey(pk.Username, f.Key)); break;
                    }
                    if (ok) break;
                }
                if (!ok) throw new SshAuthenticationException("Permission denied (" + (tried.Count == 0 ? "no authentication method was given" : string.Join(",", tried)) + ").");
                conn.LoggedIn();
            }
            catch (Exception e)
            {
                conn.Dispose();
                var inner = e is AggregateException { InnerException: { } ie } ? ie : e;
                var text = Environment.GetEnvironmentVariable("OPENVP_SSH_DEBUG") == "1" ? inner.ToString() : inner.Message;
                if (inner is SshAuthenticationException) throw;
                if (inner is TimeoutException) throw new SshOperationTimeoutException(text);
                if (inner.Message.StartsWith("Host key")) throw new SshConnectionException(text, DisconnectReason.HostKeyNotVerifiable);
                throw new SshConnectionException(text, DisconnectReason.ConnectionLost);
            }
            Connection = conn;
            ArmKeepAlive();
        }

        public virtual void Disconnect() { _keepAlive?.Dispose(); _keepAlive = null; Connection?.Dispose(); Connection = null; }

        public void SendKeepAlive()
        {
            try { Connection?.KeepAlive(); }
            catch (Exception e) { OnError(e); }
        }

        protected void OnError(Exception e) => ErrorOccurred?.Invoke(this, new ExceptionEventArgs(e));
        protected void OnHostKey(HostKeyEventArgs e) => HostKeyReceived?.Invoke(this, e);
        public virtual void Dispose() { Disconnect(); }
    }

    /// <summary>An interactive shell on a pseudo-terminal. What the server sends collects in a buffer; read it or wait for a prompt with Expect.</summary>
    public class ShellStream : IDisposable
    {
        private readonly SshConnection.SshChannel _ch;
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
        private readonly object _gate = new object();
        private bool _closed;

        internal ShellStream(SshConnection.SshChannel ch, int bufferSize)
        {
            _ch = ch;
            BufferSize = bufferSize;
            ch.Data += OnBytes;
            ch.ExtendedData += OnBytes;
            ch.Closed += () => { lock (_gate) { _closed = true; Monitor.PulseAll(_gate); } Closed?.Invoke(this, EventArgs.Empty); };
        }

        private void OnBytes(byte[] data)
        {
            string text;
            lock (_gate)
            {
                var chars = new char[_decoder.GetCharCount(data, 0, data.Length, false)];
                var n = _decoder.GetChars(data, 0, data.Length, chars, 0, false);
                text = new string(chars, 0, n);
                _buffer.Append(text);
                if (BufferSize > 0 && _buffer.Length > BufferSize * 4) _buffer.Remove(0, _buffer.Length - BufferSize * 4);
                Monitor.PulseAll(_gate);
            }
            DataReceived?.Invoke(this, new ShellDataEventArgs(data));
        }

        public int BufferSize { get; private set; }
        public event EventHandler<ShellDataEventArgs> DataReceived;
        public event EventHandler<ExceptionEventArgs> ErrorOccurred;
        public event EventHandler Closed;
        public bool DataAvailable { get { lock (_gate) return _buffer.Length > 0; } }
        public long Length { get { lock (_gate) return _buffer.Length; } }
        public bool CanRead => !_closed;
        public bool CanWrite => !_closed;
        public bool CanSeek => false;

        /// <summary>Everything received so far; empties the buffer.</summary>
        public string Read()
        {
            lock (_gate) { var s = _buffer.ToString(); _buffer.Clear(); return s; }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                var bytes = Encoding.UTF8.GetBytes(_buffer.ToString());
                var n = Math.Min(count, bytes.Length);
                Array.Copy(bytes, 0, buffer, offset, n);
                var remaining = Encoding.UTF8.GetString(bytes, n, bytes.Length - n);
                _buffer.Clear(); _buffer.Append(remaining);
                return n;
            }
        }

        /// <summary>The next complete line, or null when there isn't one yet.</summary>
        public string ReadLine() => ReadLine(TimeSpan.Zero);

        /// <summary>The next complete line, waiting up to <paramref name="timeout"/> for it; null if none arrived.</summary>
        public string ReadLine(TimeSpan timeout)
        {
            var deadline = timeout == Timeout.InfiniteTimeSpan ? DateTime.MaxValue : DateTime.UtcNow + timeout;
            lock (_gate)
            {
                while (true)
                {
                    var s = _buffer.ToString();
                    var i = s.IndexOf('\n');
                    if (i >= 0) { _buffer.Remove(0, i + 1); return s.Substring(0, i).TrimEnd('\r'); }
                    var left = deadline - DateTime.UtcNow;
                    if (_closed || left <= TimeSpan.Zero) return null;
                    Monitor.Wait(_gate, left > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : left);
                }
            }
        }

        /// <summary>Waits for <paramref name="text"/> to appear; returns everything up to and including it (what follows stays in the buffer).</summary>
        public string Expect(string text) => Expect(text, Timeout.InfiniteTimeSpan);
        public string Expect(string text, TimeSpan timeout) => ExpectCore(s => { var i = s.IndexOf(text, StringComparison.Ordinal); return i < 0 ? -1 : i + text.Length; }, timeout);
        public string Expect(Regex regex) => Expect(regex, Timeout.InfiniteTimeSpan);
        public string Expect(Regex regex, TimeSpan timeout) => ExpectCore(s => { var m = regex.Match(s); return m.Success ? m.Index + m.Length : -1; }, timeout);

        /// <summary>Waits for the first of several expressions; runs its action with the text and returns that text. Null on timeout.</summary>
        public string Expect(TimeSpan timeout, params ExpectAction[] expectActions)
        {
            ExpectAction hit = null;
            var text = ExpectCore(s =>
            {
                foreach (var a in expectActions)
                {
                    var m = a.Expect.Match(s);
                    if (m.Success) { hit = a; return m.Index + m.Length; }
                }
                return -1;
            }, timeout);
            if (text != null) hit?.Action(text);
            return text;
        }

        public string Expect(params ExpectAction[] expectActions) => Expect(Timeout.InfiniteTimeSpan, expectActions);

        private string ExpectCore(Func<string, int> find, TimeSpan timeout)
        {
            var deadline = timeout == Timeout.InfiniteTimeSpan ? DateTime.MaxValue : DateTime.UtcNow + timeout;
            lock (_gate)
            {
                while (true)
                {
                    var s = _buffer.ToString();
                    var end = find(s);
                    if (end >= 0) { _buffer.Remove(0, end); return s.Substring(0, end); }
                    var left = deadline - DateTime.UtcNow;
                    if (_closed || left <= TimeSpan.Zero) return null;
                    Monitor.Wait(_gate, left > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : left);
                }
            }
        }

        public void Write(string text) => Write(Encoding.UTF8.GetBytes(text ?? ""), 0, Encoding.UTF8.GetByteCount(text ?? ""));

        public void Write(byte[] buffer, int offset, int count)
        {
            if (_closed || _ch.IsClosed) throw new SshConnectionException("The shell is closed.", DisconnectReason.ConnectionLost);
            try { _ch.Send(buffer.Skip(offset).Take(count).ToArray()); }
            catch (System.IO.IOException e) { throw new SshConnectionException(e.Message, DisconnectReason.ConnectionLost); }
        }

        public void WriteLine(string line) => Write(line + "\r\n");
        public void Flush() { }
        public void Close() { _ch.Close(); }
        public void Dispose() { Close(); }
    }

    /// <summary>Waits in <see cref="ShellStream.Expect(TimeSpan, ExpectAction[])"/> for a pattern, then runs an action with the text up to it.</summary>
    public class ExpectAction
    {
        public ExpectAction(string expect, Action<string> action) : this(new Regex(Regex.Escape(expect)), action) { }
        public ExpectAction(Regex expect, Action<string> action) { Expect = expect; Action = action; }
        public Regex Expect { get; private set; }
        public Action<string> Action { get; private set; }
    }

    /// <summary>A command run on the server with no terminal: its output, its error output and its exit status.</summary>
    public class SshCommand : IDisposable
    {
        private readonly SshClient _client;
        private SshConnection.SshChannel _ch;

        internal SshCommand(SshClient client, string commandText) { _client = client; CommandText = commandText; }

        public string CommandText { get; private set; }
        /// <summary>How long Execute waits for the command to finish. Infinite by default.</summary>
        public TimeSpan CommandTimeout { get; set; } = Timeout.InfiniteTimeSpan;
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        /// <summary>What the command wrote to standard output.</summary>
        public string Result { get; private set; }
        /// <summary>What the command wrote to standard error.</summary>
        public string Error { get; private set; }
        public int? ExitStatus { get; private set; }

        public string Execute() => Execute(CommandText);

        public string Execute(string commandText)
        {
            CommandText = commandText;
            if (!_client.IsConnected) throw new SshConnectionException("Client not connected.", DisconnectReason.ConnectionLost);
            var stdout = new System.IO.MemoryStream(); var stderr = new System.IO.MemoryStream();
            var done = new ManualResetEventSlim(false);
            SshConnection.SshChannel ch;
            try
            {
                ch = _ch = _client.Connection.OpenSession();
                ch.Data += d => { lock (stdout) stdout.Write(d, 0, d.Length); };
                ch.ExtendedData += d => { lock (stderr) stderr.Write(d, 0, d.Length); };
                ch.Closed += () => done.Set();
                if (!ch.Request("exec", w => w.String(commandText ?? ""))) { ch.Close(); throw new SshException("The server refused to run the command."); }
                ch.SendEof();
            }
            catch (System.IO.IOException e) { throw new SshConnectionException(e.Message, DisconnectReason.ConnectionLost); }
            var ok = CommandTimeout == Timeout.InfiniteTimeSpan ? done.Wait(-1) : done.Wait(CommandTimeout);
            if (!ok) { ch.Close(); throw new SshOperationTimeoutException("Command execution timed out."); }
            ExitStatus = ch.ExitStatus;
            lock (stdout) Result = Encoding.GetString(stdout.ToArray());
            lock (stderr) Error = Encoding.GetString(stderr.ToArray());
            return Result;
        }

        /// <summary>Stop a running Execute (from another thread): the channel is closed and Execute returns what arrived.</summary>
        public void CancelAsync() { try { _ch?.Close(); } catch (Exception) { } }
        public void Dispose() { try { _ch?.Close(); } catch (Exception) { } }
    }

    public class SshClient : BaseClient
    {
        public SshClient(ConnectionInfo info) : base(info) { }
        public SshClient(string host, int port, string username, string password)
            : base(new ConnectionInfo(host, port, username, new PasswordAuthenticationMethod(username, password))) { }
        public SshClient(string host, string username, string password) : this(host, 22, username, password) { }
        public SshClient(string host, int port, string username, params PrivateKeyFile[] keyFiles)
            : base(new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, keyFiles))) { }
        public SshClient(string host, string username, params PrivateKeyFile[] keyFiles) : this(host, 22, username, keyFiles) { }

        public ShellStream CreateShellStream(string terminalName, uint columns, uint rows, uint width, uint height, int bufferSize)
        {
            if (!IsConnected) throw new SshConnectionException("Client not connected.", DisconnectReason.ConnectionLost);
            try
            {
                ShellStream stream = null;
                Connection.OpenShellChannel(terminalName, columns, rows, width, height, ch => stream = new ShellStream(ch, bufferSize));
                return stream;
            }
            catch (System.IO.IOException e) { throw new SshException(e.Message, e); }
        }

        /// <summary>A command to run later with <see cref="SshCommand.Execute()"/>.</summary>
        public SshCommand CreateCommand(string commandText)
        {
            if (!IsConnected) throw new SshConnectionException("Client not connected.", DisconnectReason.ConnectionLost);
            return new SshCommand(this, commandText);
        }

        /// <summary>Runs a command and waits for it to finish.</summary>
        public SshCommand RunCommand(string commandText)
        {
            var c = CreateCommand(commandText);
            c.Execute();
            return c;
        }
    }
}
