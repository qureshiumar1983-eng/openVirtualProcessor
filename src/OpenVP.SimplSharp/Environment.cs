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
        public static event ProgramStatusEventHandler ProgramStatusEventHandler;
        public static event SystemEventHandler SystemEventHandler;
        public static event EthernetEventHandler EthernetEventHandler;

        /// <summary>OpenVP behaves like VC-4: a server, not an appliance.</summary>
        public static eDevicePlatform DevicePlatform => eDevicePlatform.Server;

        public static eRuntimeEnvironment RuntimeEnvironment => eRuntimeEnvironment.SimplSharpPro;

        public static string SystemInfoDomainName => System.Net.Dns.GetHostName();

        public static int ProgramCompatibility => 4;

        public static void Sleep(int milliseconds) => System.Threading.Thread.Sleep(milliseconds);

        public static long GetTickCount() => Environment.TickCount64;

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
        public static uint ApplicationNumber => (uint)(OpenVPBridge.Slot?.Number ?? 1);

        public static string ProgramIDTag => OpenVPBridge.Slot?.Runtime?.ProgramName ?? string.Empty;

        public static string RoomId => $"SLOT{ApplicationNumber:00}";

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
        public static void BeginInvoke(CrestronSharpHelperDelegate callbackFunction) => BeginInvoke(callbackFunction, null);

        public static void BeginInvoke(CrestronSharpHelperDelegate callbackFunction, object userObject)
        {
            var slot = OpenVPBridge.Slot;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { callbackFunction(userObject); }
                catch (Exception e) { slot?.Log("Error", "Exception in CrestronInvoke: " + e); }
            });
        }
    }
}
