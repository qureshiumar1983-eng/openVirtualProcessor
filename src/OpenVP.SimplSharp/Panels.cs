using Crestron.SimplSharpPro.DeviceSupport;
using OpenVP.Core;
using OpenVP.SimplSharp;

namespace Crestron.SimplSharpPro.UI
{
    /// <summary>Common base for touch panel classes. Every model works the same way here: joins over IP.</summary>
    public abstract class TswFt5Button : BasicTriListWithSmartObject
    {
        protected TswFt5Button(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { }
    }

    public abstract class TswX70Base : TswFt5Button { protected TswX70Base(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public abstract class TswX60Base : TswFt5Button { protected TswX60Base(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }

    public class Tsw1070 : TswX70Base { public Tsw1070(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public class Tsw770 : TswX70Base { public Tsw770(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public class Tsw570 : TswX70Base { public Tsw570(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public class Tsw570P : TswX70Base { public Tsw570P(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public class Tsw1060 : TswX60Base { public Tsw1060(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public class Tsw760 : TswX60Base { public Tsw760(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public class Tsw560 : TswX60Base { public Tsw560(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public class Ts1070 : TswX70Base { public Ts1070(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public class Ts770 : TswX70Base { public Ts770(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public class Tsw1080 : TswX70Base { public Tsw1080(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }
    public class Tsw780 : TswX70Base { public Tsw780(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { } }

    public class XpanelForSmartGraphics : BasicTriListWithSmartObject
    {
        public XpanelForSmartGraphics(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { }
    }

    public class XpanelForHtml5 : BasicTriListWithSmartObject
    {
        public XpanelForHtml5(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { }
    }

    public class CrestronApp : BasicTriListWithSmartObject
    {
        public CrestronApp(uint ipId, CrestronControlSystem cs) : base(ipId, cs) { }
        public string ParameterProjectName { get; set; } = string.Empty;
    }
}

namespace Crestron.SimplSharpPro.EthernetCommunication
{
    /// <summary>
    /// EISC. When the address is local (127.0.0.2, 127.0.0.1, localhost) it links to any other slot's
    /// EISC with the same IP ID, including SIMPL Windows slots. Remote EISCs are not supported yet.
    /// </summary>
    public class ThreeSeriesTcpIpEthernetIntersystemCommunications : BasicTriList
    {
        public ThreeSeriesTcpIpEthernetIntersystemCommunications(uint ipId, string ipAddress, CrestronControlSystem cs)
            : base(ipId, cs, "EISC to " + ipAddress)
        {
            IPAddress = ipAddress ?? string.Empty;
        }

        public string IPAddress { get; }

        protected override string DeviceTypeName => "EISC";

        protected override void AfterRegister(ProgramSlot slot)
        {
            if (IntersystemBus.IsLocalAddress(IPAddress))
                slot.Processor.Bus.Attach(Device);
            else
                OpenVPBridge.Log("Warning", $"EISC {ID:X2} to {IPAddress}: only local (127.0.0.2) EISCs are supported so far.");
        }

        protected override void BeforeUnregister(ProgramSlot slot)
        {
            if (slot != null && Device != null) slot.Processor.Bus.Detach(Device);
        }
    }

    /// <summary>4-Series name for the same EISC class.</summary>
    public class EthernetIntersystemCommunications : ThreeSeriesTcpIpEthernetIntersystemCommunications
    {
        public EthernetIntersystemCommunications(uint ipId, string ipAddress, CrestronControlSystem cs)
            : base(ipId, ipAddress, cs) { }
    }
}
