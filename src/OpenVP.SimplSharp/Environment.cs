using System;
using System.IO;
using OpenVP.SimplSharp;

namespace Crestron.SimplSharp
{
    public enum eProgramStatusEventType { Stopping = 0, Paused = 1, Resumed = 2 }

    public enum eSystemEventType { DiskInserted = 0, DiskRemoved = 1, Rebooting = 2 }

    public enum eEthernetEventType { LinkDown = 0, LinkUp = 1 }

    public enum EthernetAdapterType
    {
        EthernetUnknownAdapter = -1,
        EthernetLANAdapter = 0,
        EthernetLAN2Adapter = 1,
        EthernetCSAdapter = 2,
        EthernetAdapterWifi = 3
    }

    public enum eDevicePlatform { Appliance = 0, Server = 1 }

    public enum eRuntimeEnvironment { SimplSharpPro = 0, SIMPL = 1, SimplSharp = 2 }

    public class EthernetEventArgs : EventArgs
    {
        public EthernetEventArgs(eEthernetEventType type, EthernetAdapterType adapter)
        {
            EthernetEventType = type;
            EthernetAdapter = adapter;
        }

        public eEthernetEventType EthernetEventType { get; }
        public EthernetAdapterType EthernetAdapter { get; }
    }

    public delegate void ProgramStatusEventHandler(eProgramStatusEventType programEventType);
    public delegate void SystemEventHandler(eSystemEventType systemEventType);
    public delegate void EthernetEventHandler(EthernetEventArgs ethernetEventArgs);

    public static class CrestronEnvironment
    {
        public static eCrestronSeries ProgramCompatibility => eCrestronSeries.Series4;
        public static string NewLine => "\r\n";

        /// <summary>Scales value from [inLow, inHigh] to [outLow, outHigh], clamped.</summary>
        public static int ScaleWithLimits(int value, int inHigh, int inLow, int outHigh, int outLow)
        {
            if (inHigh == inLow) return outLow;
            var v = Math.Clamp(value, Math.Min(inLow, inHigh), Math.Max(inLow, inHigh));
            return (int)Math.Round(outLow + (v - inLow) * (double)(outHigh - outLow) / (inHigh - inLow));
        }

        public static class OSVersion
        {
            public static string Firmware => "OpenVP " + typeof(CrestronEnvironment).Assembly.GetName().Version;
            public static string Version => Firmware;
        }

        public static class SystemInfo
        {
            public static string SerialNumber => "OPENVP" + System.Environment.MachineName.ToUpperInvariant();
            public static string MACAddress => "00:00:00:00:00:00";
        }

        public static event ProgramStatusEventHandler ProgramStatusEventHandler;
        public static event SystemEventHandler SystemEventHandler;
        public static event EthernetEventHandler EthernetEventHandler;

        /// <summary>OpenVP behaves like VC-4: a server, not an appliance.</summary>
        public static eDevicePlatform DevicePlatform => eDevicePlatform.Server;

        public static eRuntimeEnvironment RuntimeEnvironment => eRuntimeEnvironment.SimplSharpPro;

        public static string SystemInfoDomainName => System.Net.Dns.GetHostName();

        public static void Sleep(int milliseconds) => System.Threading.Thread.Sleep(milliseconds);

        public static long GetTickCount() => Environment.TickCount64;

        public static int TickCount => Environment.TickCount;

        public static DateTime GetLocalTime() => DateTime.Now;

        public static string GetTimeZone() => TimeZoneInfo.Local.Id;

        public static void AllowOtherAppsToRun() => System.Threading.Thread.Yield();

        internal static void Raise(eProgramStatusEventType type) => ProgramStatusEventHandler?.Invoke(type);

        internal static void Raise(eSystemEventType type) => SystemEventHandler?.Invoke(type);

        internal static void Raise(EthernetEventArgs args) => EthernetEventHandler?.Invoke(args);
    }

    /// <summary>Program-level facts (slot number, room ID, program folder).</summary>
    public static class InitialParametersClass
    {
        public delegate string ResolveHostNameFunctionDelegate(string hostName);

        public static uint NumberOfExternalEthernetInterfaces => 1;

        /// <summary>Custom host-name resolver some libraries install. Kept for compatibility.</summary>
        public static ResolveHostNameFunctionDelegate ResolveHostName { get; set; }

        public static uint ApplicationNumber => (uint)(OpenVPBridge.Slot?.Number ?? 1);

        public static string ProgramIDTag => OpenVPBridge.Slot?.Runtime?.ProgramIdTag ?? string.Empty;

        public static string RoomId => OpenVPBridge.Slot?.RoomId ?? $"SLOT{ApplicationNumber:00}";

        public static string RoomName => RoomId;

        public static string ProgramDirectory
        {
            get
            {
                var dir = OpenVPBridge.Slot?.Directory ?? Directory.GetCurrentDirectory();
                return dir.EndsWith(Path.DirectorySeparatorChar.ToString()) ? dir : dir + Path.DirectorySeparatorChar;
            }
        }

        public static string FirmwareVersion => "OpenVP 0.1";

        public static string ControllerPromptName => "OPENVP";
    }

    public delegate void CrestronSharpHelperDelegate(object userObject);

    public static class CrestronInvoke
    {
        /// <summary>Run work in the background. Runs on the thread pool, not the slot thread, so it may block.</summary>
        public static object BeginInvoke(CrestronSharpHelperDelegate callbackFunction) => BeginInvoke(callbackFunction, null);

        public static object BeginInvoke(CrestronSharpHelperDelegate callbackFunction, object userObject)
        {
            var slot = OpenVPBridge.Slot;
            var label = (callbackFunction.Method.DeclaringType?.Name ?? "?") + "." + callbackFunction.Method.Name;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                using var work = slot?.Metrics.BeginWork("CrestronInvoke", label);
                try { callbackFunction(userObject); }
                catch (Exception e) { slot?.Log("Error", "Exception in CrestronInvoke: " + e); }
            });
            return null;
        }
    }
}
