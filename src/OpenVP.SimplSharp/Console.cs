using System;
using OpenVP.Core;
using OpenVP.SimplSharp;

namespace Crestron.SimplSharp
{
    public enum ConsoleAccessLevelEnum
    {
        AccessAdministrator = 0,
        AccessOperator = 1,
        AccessProgrammer = 2
    }

    public delegate void SimplSharpProConsoleCmdFunction(string cmdParameters);

    public static class CrestronConsole
    {
        public static void PrintLine(string message) => Write(message);

        public static void PrintLine(string format, params object[] args) => Write(SafeFormat(format, args));

        public static void Print(string message) => Write(message);

        public static void Print(string format, params object[] args) => Write(SafeFormat(format, args));

        /// <summary>Write the response to the console command currently running.</summary>
        public static void ConsoleCommandResponse(string message)
        {
            var sink = OpenVPBridge.ConsoleSink;
            if (sink != null) sink(message);
            else Write(message);
        }

        public static void ConsoleCommandResponse(string format, params object[] args) =>
            ConsoleCommandResponse(SafeFormat(format, args));

        public static bool AddNewConsoleCommand(SimplSharpProConsoleCmdFunction userFunction, string userCmdName,
            string userCmdHelp, ConsoleAccessLevelEnum userCmdAccess)
        {
            if (userFunction == null || string.IsNullOrWhiteSpace(userCmdName)) return false;
            return OpenVPBridge.RequireSlot.AddConsoleCommand(new ConsoleCommand(userCmdName, userCmdHelp ?? string.Empty,
                (args, respond) =>
                {
                    var previous = OpenVPBridge.ConsoleSink;
                    OpenVPBridge.ConsoleSink = respond;
                    try { userFunction(args); }
                    finally { OpenVPBridge.ConsoleSink = previous; }
                }));
        }

        public static bool AddNewConsoleCommand(SimplSharpProConsoleCmdFunction userFunction, string userCmdName,
            string userCmdHelp, ConsoleAccessLevelEnum userCmdAccess, bool allowDuplicates) =>
            AddNewConsoleCommand(userFunction, userCmdName, userCmdHelp, userCmdAccess);

        /// <summary>Send a command to the processor's own console. Returns false: a virtual processor has no firmware console.</summary>
        public static bool SendControlSystemCommand(string cmd, ref string response)
        {
            response = OpenVPBridge.RequireSlot.RunConsoleCommand(cmd);
            return true;
        }

        private static void Write(string message) => OpenVPBridge.Log("Console", message ?? string.Empty);

        internal static string SafeFormat(string format, object[] args)
        {
            if (args == null || args.Length == 0) return format ?? string.Empty;
            try { return string.Format(format, args); }
            catch (FormatException) { return format + " " + string.Join(" ", args); }
        }
    }

    public static class ErrorLog
    {
        public static void Error(string message) => OpenVPBridge.Log("Error", message);
        public static void Error(string format, string arg) => OpenVPBridge.Log("Error", CrestronConsole.SafeFormat(format, new object[] { arg }));
        public static void Warn(string format, string arg) => OpenVPBridge.Log("Warning", CrestronConsole.SafeFormat(format, new object[] { arg }));
        public static void Notice(string format, string arg) => OpenVPBridge.Log("Notice", CrestronConsole.SafeFormat(format, new object[] { arg }));
        public static void Info(string format, string arg) => OpenVPBridge.Log("Info", CrestronConsole.SafeFormat(format, new object[] { arg }));
        public static void Error(string format, params object[] args) => OpenVPBridge.Log("Error", CrestronConsole.SafeFormat(format, args));
        public static void Warn(string message) => OpenVPBridge.Log("Warning", message);
        public static void Warn(string format, params object[] args) => OpenVPBridge.Log("Warning", CrestronConsole.SafeFormat(format, args));
        public static void Notice(string message) => OpenVPBridge.Log("Notice", message);
        public static void Notice(string format, params object[] args) => OpenVPBridge.Log("Notice", CrestronConsole.SafeFormat(format, args));
        public static void Ok(string message) => OpenVPBridge.Log("Notice", message);
        public static void Ok(string format, params object[] args) => OpenVPBridge.Log("Notice", CrestronConsole.SafeFormat(format, args));
        public static void Info(string message) => OpenVPBridge.Log("Info", message);
        public static void Info(string format, params object[] args) => OpenVPBridge.Log("Info", CrestronConsole.SafeFormat(format, args));
        public static void Exception(string message, Exception ex) => OpenVPBridge.Log("Error", message + ": " + ex);
    }
}
