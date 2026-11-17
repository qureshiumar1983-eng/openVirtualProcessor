using System;
using System.Text;

namespace Crestron.SandboxCustomAttributes
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class AssemblyInfoAttribute : Attribute
    {
        public AssemblyInfoAttribute(string info) { Info = info; }
        public string Info { get; }
    }
}

namespace Crestron.SimplSharp
{
    public static class Interlocked
    {
        public static int Increment(ref int v) => System.Threading.Interlocked.Increment(ref v);
        public static int Decrement(ref int v) => System.Threading.Interlocked.Decrement(ref v);
        public static int Exchange(ref int v, int value) => System.Threading.Interlocked.Exchange(ref v, value);
        public static int CompareExchange(ref int v, int value, int comparand) => System.Threading.Interlocked.CompareExchange(ref v, value, comparand);
        public static long Increment(ref long v) => System.Threading.Interlocked.Increment(ref v);
        public static long Decrement(ref long v) => System.Threading.Interlocked.Decrement(ref v);
    }

    public class Stopwatch
    {
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        public static Stopwatch StartNew() { var s = new Stopwatch(); s.Start(); return s; }
        public void Start() => _sw.Start();
        public void Stop() => _sw.Stop();
        public void Reset() => _sw.Reset();
        public void Restart() => _sw.Restart();
        public bool IsRunning => _sw.IsRunning;
        public long ElapsedMilliseconds => _sw.ElapsedMilliseconds;
        public long ElapsedTicks => _sw.ElapsedTicks;
        public TimeSpan Elapsed => _sw.Elapsed;
        public static long Frequency => System.Diagnostics.Stopwatch.Frequency;
        public static long GetTimestamp() => System.Diagnostics.Stopwatch.GetTimestamp();
    }
}

namespace Crestron.SimplSharp.Cryptography
{
    public abstract class HashAlgorithm : IDisposable
    {
        protected abstract System.Security.Cryptography.HashAlgorithm Inner { get; }
        public byte[] ComputeHash(byte[] data) => Inner.ComputeHash(data);
        public byte[] ComputeHash(byte[] data, int offset, int count) => Inner.ComputeHash(data, offset, count);
        public byte[] Hash => Inner.Hash;
        public void Clear() => Inner.Dispose();
        public void Dispose() => Inner.Dispose();
    }

    public class MD5 : HashAlgorithm
    {
        private readonly System.Security.Cryptography.MD5 _md5 = System.Security.Cryptography.MD5.Create();
        protected override System.Security.Cryptography.HashAlgorithm Inner => _md5;
        public static MD5 Create() => new MD5();
    }

    public class MD5CryptoServiceProvider : MD5 { }

    public class SHA1 : HashAlgorithm
    {
        private readonly System.Security.Cryptography.SHA1 _h = System.Security.Cryptography.SHA1.Create();
        protected override System.Security.Cryptography.HashAlgorithm Inner => _h;
        public static SHA1 Create() => new SHA1();
    }

    public class SHA1CryptoServiceProvider : SHA1 { }

    public class SHA256 : HashAlgorithm
    {
        private readonly System.Security.Cryptography.SHA256 _h = System.Security.Cryptography.SHA256.Create();
        protected override System.Security.Cryptography.HashAlgorithm Inner => _h;
        public static SHA256 Create() => new SHA256();
    }

    public class SHA256Managed : SHA256 { }
}

namespace Crestron.SimplSharp.Cryptography
{
    public enum CipherMode { CBC = 1, ECB = 2, OFB = 3, CFB = 4, CTS = 5 }
    public enum PaddingMode { None = 1, PKCS7 = 2, Zeros = 3, ANSIX923 = 4, ISO10126 = 5 }
    public enum CryptoStreamMode { Read = 0, Write = 1 }

    public interface ICryptoTransform : IDisposable
    {
        int TransformBlock(byte[] input, int inputOffset, int inputCount, byte[] output, int outputOffset);
        byte[] TransformFinalBlock(byte[] input, int inputOffset, int inputCount);
        int InputBlockSize { get; }
        int OutputBlockSize { get; }
    }

    internal sealed class Transform : ICryptoTransform
    {
        internal readonly System.Security.Cryptography.ICryptoTransform Inner;
        public Transform(System.Security.Cryptography.ICryptoTransform t) { Inner = t; }
        public int TransformBlock(byte[] i, int io, int ic, byte[] o, int oo) => Inner.TransformBlock(i, io, ic, o, oo);
        public byte[] TransformFinalBlock(byte[] i, int io, int ic) => Inner.TransformFinalBlock(i, io, ic);
        public int InputBlockSize => Inner.InputBlockSize;
        public int OutputBlockSize => Inner.OutputBlockSize;
        public void Dispose() => Inner.Dispose();
    }

    public abstract class SymmetricAlgorithm : IDisposable
    {
        protected abstract System.Security.Cryptography.SymmetricAlgorithm Alg { get; }
        public byte[] Key { get => Alg.Key; set => Alg.Key = value; }
        public byte[] IV { get => Alg.IV; set => Alg.IV = value; }
        public int KeySize { get => Alg.KeySize; set => Alg.KeySize = value; }
        public int BlockSize { get => Alg.BlockSize; set => Alg.BlockSize = value; }
        public CipherMode Mode { get => (CipherMode)(int)Alg.Mode; set => Alg.Mode = (System.Security.Cryptography.CipherMode)(int)value; }
        public PaddingMode Padding { get => (PaddingMode)(int)Alg.Padding; set => Alg.Padding = (System.Security.Cryptography.PaddingMode)(int)value; }
        public void GenerateKey() => Alg.GenerateKey();
        public void GenerateIV() => Alg.GenerateIV();
        public ICryptoTransform CreateEncryptor() => new Transform(Alg.CreateEncryptor());
        public ICryptoTransform CreateDecryptor() => new Transform(Alg.CreateDecryptor());
        public ICryptoTransform CreateEncryptor(byte[] key, byte[] iv) => new Transform(Alg.CreateEncryptor(key, iv));
        public ICryptoTransform CreateDecryptor(byte[] key, byte[] iv) => new Transform(Alg.CreateDecryptor(key, iv));
        public void Clear() => Alg.Dispose();
        public void Dispose() => Alg.Dispose();
    }

    public class Rijndael : SymmetricAlgorithm
    {
        private readonly System.Security.Cryptography.Aes _aes = System.Security.Cryptography.Aes.Create();
        protected override System.Security.Cryptography.SymmetricAlgorithm Alg => _aes;
        public static Rijndael Create() => new Rijndael();
    }

    public class RijndaelManaged : Rijndael { }

    public class Aes : Rijndael
    {
        public static new Aes Create() => new Aes();
    }

    public class AesManaged : Aes { }

    public class CryptoStream : Crestron.SimplSharp.CrestronIO.Stream
    {
        public CryptoStream(Crestron.SimplSharp.CrestronIO.Stream stream, ICryptoTransform transform, CryptoStreamMode mode)
            : base(new System.Security.Cryptography.CryptoStream(stream.Inner, ((Transform)transform).Inner,
                mode == CryptoStreamMode.Write ? System.Security.Cryptography.CryptoStreamMode.Write : System.Security.Cryptography.CryptoStreamMode.Read, true)) { }
        public void FlushFinalBlock() => ((System.Security.Cryptography.CryptoStream)Inner).FlushFinalBlock();
    }

    public class PBKDF2SHA256
    {
        private readonly byte[] _password, _salt;
        private readonly int _iterations;
        public PBKDF2SHA256(byte[] password, byte[] salt, int iterations) { _password = password; _salt = salt; _iterations = iterations; }
        public byte[] GetBytes(int count) => System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(_password, _salt, _iterations, System.Security.Cryptography.HashAlgorithmName.SHA256, count);
    }
}
