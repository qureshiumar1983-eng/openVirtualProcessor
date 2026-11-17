using System;
using System.Text;
using OpenVP.SimplSharp;
using SIO = System.IO;

namespace Crestron.SimplSharp.CrestronIO
{
    public enum FileMode { CreateNew = 1, Create = 2, Open = 3, OpenOrCreate = 4, Truncate = 5, Append = 6 }
    public enum FileAccess { Read = 1, Write = 2, ReadWrite = 3 }
    public enum FileShare { None = 0, Read = 1, Write = 2, ReadWrite = 3 }
    public enum SeekOrigin { Begin = 0, Current = 1, End = 2 }

    /// <summary>Maps processor paths (\NVRAM, /user, \USER) into the slot's folder.</summary>
    internal static class Paths
    {
        public static string Map(string path)
        {
            var root = OpenVPBridge.Slot?.Directory ?? SIO.Path.GetTempPath();
            if (string.IsNullOrEmpty(path)) return root;
            var p = path.Replace('\\', '/');
            if (p.StartsWith(root.Replace('\\', '/'), StringComparison.Ordinal)) return path; // already mapped
            p = p.TrimStart('/');
            var full = SIO.Path.GetFullPath(SIO.Path.Combine(root, p.ToLowerInvariant().StartsWith("nvram") || p.ToLowerInvariant().StartsWith("user") || p.ToLowerInvariant().StartsWith("html") ? p : p));
            if (!full.StartsWith(SIO.Path.GetFullPath(root), StringComparison.Ordinal))
                throw new UnauthorizedAccessException($"Path '{path}' is outside the program folder.");
            return full;
        }
    }

    /// <summary>Stream with the SIMPL# API shape, wrapping a .NET stream.</summary>
    public class Stream : IDisposable
    {
        protected internal SIO.Stream Inner;
        protected Stream() { }
        protected internal Stream(SIO.Stream inner) { Inner = inner; }
        public virtual bool CanRead => Inner.CanRead;
        public virtual bool CanWrite => Inner.CanWrite;
        public virtual bool CanSeek => Inner.CanSeek;
        public virtual long Length => Inner.Length;
        public virtual long Position { get => Inner.Position; set => Inner.Position = value; }
        public virtual int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
        public virtual int ReadByte() => Inner.ReadByte();
        public virtual void Write(byte[] buffer, int offset, int count) => Inner.Write(buffer, offset, count);
        public virtual void WriteByte(byte value) => Inner.WriteByte(value);
        public virtual long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, (SIO.SeekOrigin)origin);
        public virtual void SetLength(long value) => Inner.SetLength(value);
        public virtual void Flush() => Inner.Flush();
        public virtual void Close() => Inner.Dispose();
        public void Dispose() => Close();
    }

    public class FileStream : Stream
    {
        public FileStream(string path, FileMode mode) : this(path, mode, mode == FileMode.Append ? FileAccess.Write : FileAccess.ReadWrite) { }
        public FileStream(string path, FileMode mode, FileAccess access) : this(path, mode, access, FileShare.Read) { }
        public FileStream(string path, FileMode mode, FileAccess access, FileShare share)
            : base(Open(path, mode, access, share)) { Name = path; }
        public string Name { get; }
        private static SIO.Stream Open(string path, FileMode mode, FileAccess access, FileShare share)
        {
            var full = Paths.Map(path);
            SIO.Directory.CreateDirectory(SIO.Path.GetDirectoryName(full)!);
            return new SIO.FileStream(full, (SIO.FileMode)mode, (SIO.FileAccess)access, (SIO.FileShare)share);
        }
    }

    public class MemoryStream : Stream
    {
        public MemoryStream() : base(new SIO.MemoryStream()) { }
        public MemoryStream(byte[] buffer) : base(new SIO.MemoryStream(buffer)) { }
        public byte[] ToArray() => ((SIO.MemoryStream)Inner).ToArray();
        public byte[] GetBuffer() => ((SIO.MemoryStream)Inner).GetBuffer();
    }

    public abstract class TextReader : IDisposable
    {
        protected SIO.TextReader Inner;
        public virtual string ReadToEnd() => Inner.ReadToEnd();
        public virtual string ReadLine() => Inner.ReadLine();
        public virtual int Read() => Inner.Read();
        public virtual int Peek() => Inner.Peek();
        public virtual void Close() => Inner.Dispose();
        public void Dispose() => Close();
    }

    public class StreamReader : TextReader
    {
        public StreamReader(Stream stream) { Inner = new SIO.StreamReader(stream.Inner); }
        public StreamReader(Stream stream, Encoding encoding) { Inner = new SIO.StreamReader(stream.Inner, encoding); }
        public StreamReader(string path) { Inner = new SIO.StreamReader(Paths.Map(path)); }
        public StreamReader(string path, Encoding encoding) { Inner = new SIO.StreamReader(Paths.Map(path), encoding); }
        public bool EndOfStream => ((SIO.StreamReader)Inner).EndOfStream;
    }

    public abstract class TextWriter : IDisposable
    {
        protected SIO.TextWriter Inner;
        public virtual void Write(string value) => Inner.Write(value);
        public virtual void Write(string format, params object[] args) => Inner.Write(format, args);
        public virtual void WriteLine() => Inner.WriteLine();
        public virtual void WriteLine(string value) => Inner.WriteLine(value);
        public virtual void WriteLine(string format, params object[] args) => Inner.WriteLine(format, args);
        public virtual void Flush() => Inner.Flush();
        public virtual void Close() => Inner.Dispose();
        public void Dispose() => Close();
    }

    public class StreamWriter : TextWriter
    {
        public StreamWriter(Stream stream) { Inner = new SIO.StreamWriter(stream.Inner); }
        public StreamWriter(Stream stream, Encoding encoding) { Inner = new SIO.StreamWriter(stream.Inner, encoding); }
        public StreamWriter(string path) : this(path, false) { }
        public StreamWriter(string path, bool append)
        {
            var full = Paths.Map(path);
            SIO.Directory.CreateDirectory(SIO.Path.GetDirectoryName(full)!);
            Inner = new SIO.StreamWriter(full, append);
        }
        public bool AutoFlush { get => ((SIO.StreamWriter)Inner).AutoFlush; set => ((SIO.StreamWriter)Inner).AutoFlush = value; }
    }

    public class BinaryWriter : IDisposable
    {
        private readonly SIO.BinaryWriter _w;
        public BinaryWriter(Stream stream) { _w = new SIO.BinaryWriter(stream.Inner); }
        public void Write(byte[] buffer, int index, int count) => _w.Write(buffer, index, count);
        public void Write(byte[] buffer) => _w.Write(buffer);
        public void Write(byte value) => _w.Write(value);
        public void Flush() => _w.Flush();
        public void Close() => _w.Dispose();
        public void Dispose() => Close();
    }

    public class BinaryReader : IDisposable
    {
        private readonly SIO.BinaryReader _r;
        public BinaryReader(Stream stream) { _r = new SIO.BinaryReader(stream.Inner); }
        public byte[] ReadBytes(int count) => _r.ReadBytes(count);
        public byte ReadByte() => _r.ReadByte();
        public void Close() => _r.Dispose();
        public void Dispose() => Close();
    }

    public static class File
    {
        public static bool Exists(string path) => SIO.File.Exists(Paths.Map(path));
        public static void Delete(string path) => SIO.File.Delete(Paths.Map(path));
        public static void Move(string from, string to) => SIO.File.Move(Paths.Map(from), Paths.Map(to), true);
        public static void Copy(string from, string to) => SIO.File.Copy(Paths.Map(from), Paths.Map(to));
        public static void Copy(string from, string to, bool overwrite) => SIO.File.Copy(Paths.Map(from), Paths.Map(to), overwrite);
        public static FileStream Create(string path) => new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        public static FileStream Open(string path, FileMode mode) => new FileStream(path, mode);
        public static FileStream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read);
        public static FileStream OpenWrite(string path) => new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
        public static StreamReader OpenText(string path) => new StreamReader(path);
        public static string ReadToEnd(string path, Encoding encoding) => SIO.File.ReadAllText(Paths.Map(path), encoding ?? Encoding.UTF8);
        public static void WriteText(string path, string text) => SIO.File.WriteAllText(Paths.Map(path), text);
        public static DateTime GetCreationTime(string path) => SIO.File.GetCreationTime(Paths.Map(path));
        public static DateTime GetLastWriteTime(string path) => SIO.File.GetLastWriteTime(Paths.Map(path));
    }

    public static class Directory
    {
        public static bool Exists(string path) => SIO.Directory.Exists(Paths.Map(path));
        public static void Create(string path) => SIO.Directory.CreateDirectory(Paths.Map(path));
        public static void CreateDirectory(string path) => SIO.Directory.CreateDirectory(Paths.Map(path));
        public static void Delete(string path) => SIO.Directory.Delete(Paths.Map(path));
        public static void Delete(string path, bool recursive) => SIO.Directory.Delete(Paths.Map(path), recursive);
        public static string[] GetFiles(string path) => SIO.Directory.GetFiles(Paths.Map(path));
        public static string[] GetFiles(string path, string pattern) => SIO.Directory.GetFiles(Paths.Map(path), pattern);
        public static string[] GetDirectories(string path) => SIO.Directory.GetDirectories(Paths.Map(path));
        public static string GetApplicationDirectory() => InitialParametersClass.ProgramDirectory.TrimEnd(SIO.Path.DirectorySeparatorChar);
        public static string GetApplicationRootDirectory() => OpenVPBridge.Slot?.Processor.DataDirectory ?? SIO.Path.GetTempPath();
    }

    public class FileInfo
    {
        private readonly SIO.FileInfo _f;
        public FileInfo(string path) { _f = new SIO.FileInfo(Paths.Map(path)); }
        public long Length => _f.Exists ? _f.Length : 0;
        public bool Exists => _f.Exists;
        public string Name => _f.Name;
        public string FullName => _f.FullName;
        public DateTime LastWriteTime => _f.LastWriteTime;
        public DateTime CreationTime => _f.CreationTime;
    }

    public static class Path
    {
        public static string Combine(string a, string b) => SIO.Path.Combine(a, b);
        public static string GetFileName(string p) => SIO.Path.GetFileName(p);
        public static string GetFileNameWithoutExtension(string p) => SIO.Path.GetFileNameWithoutExtension(p);
        public static string GetExtension(string p) => SIO.Path.GetExtension(p);
        public static string GetDirectoryName(string p) => SIO.Path.GetDirectoryName(p);
        public static char DirectorySeparatorChar => SIO.Path.DirectorySeparatorChar;
    }
}
