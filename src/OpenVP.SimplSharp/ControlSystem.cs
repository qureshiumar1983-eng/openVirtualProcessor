using System;
using OpenVP.SimplSharp;

namespace Crestron.SimplSharpPro
{
    /// <summary>
    /// Base class of every SIMPL# Pro program. The program's ControlSystem class derives from this;
    /// OpenVP constructs it, then calls InitializeSystem().
    /// </summary>
    public abstract class CrestronControlSystem : GenericBase
    {
        protected CrestronControlSystem() : base(0, "OpenVP control system")
        {
            OpenVPBridge.ControlSystem = this;
        }

        /// <summary>Called once after the constructor. Put device registration and setup here.</summary>
        public virtual void InitializeSystem() { }

        // A virtual processor, like VC-4, has no physical ports; its COM ports are virtual (see ComPort).
        public bool SupportsComPort => true;
        public bool SupportsIROut => false;
        public bool SupportsRelay => false;
        public bool SupportsVersiport => false;
        public bool SupportsDigitalInput => false;
        public bool SupportsEthernet => true;
        public bool SupportsCresnet => false;
        public int NumberOfComPorts => 8;

        private CrestronCollection<ComPort> _comPorts;

        /// <summary>The virtual processor's COM ports 1-8. They carry no hardware: see <see cref="ComPort"/>.</summary>
        public CrestronCollection<ComPort> ComPorts
        {
            get
            {
                if (_comPorts == null)
                {
                    var c = new CrestronCollection<ComPort>();
                    for (uint i = 1; i <= 8; i++) c.Add(i, new ComPort(i));
                    _comPorts = c;
                }
                return _comPorts;
            }
        }
        public int NumberOfIROutputPorts => 0;
        public int NumberOfRelayPorts => 0;
        public int NumberOfVersiPorts => 0;
        public int NumberOfDigitalInputPorts => 0;

        public string ControllerPrompt => "OPENVP";

        public override bool IsOnline => true;
    }

    public static class ProgramInfo
    {
        public static uint ProgramNumber => (uint)(OpenVPBridge.Slot?.Number ?? 1);
    }
}

// Namespaces referenced by `using` lines in the default Crestron program template.
namespace Crestron.SimplSharpPro.Diagnostics
{
    public static class SystemMonitor
    {
        public static double CpuUtilization => 0;
    }
}
