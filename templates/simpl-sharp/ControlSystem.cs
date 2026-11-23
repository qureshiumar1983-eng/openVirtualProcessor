using System;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using Crestron.SimplSharpPro.EthernetCommunication;
using Crestron.SimplSharpPro.UI;

namespace StarterProgram
{
    /// <summary>
    /// A starter program: one touch panel at IP ID 03 and one EISC at IP ID 10.
    ///
    /// Panel (IP ID 03)
    ///   press digital 1   toggles digital feedback 1 (a lamp)
    ///   analog 1          is echoed back on analog feedback 1, and as text on serial feedback 1
    ///   serial 1          comes back in upper case on serial feedback 2
    ///   press digital 10  is sent over the EISC (join 1), so another slot's program can react
    /// EISC (IP ID 10, address 127.0.0.2: a link to another slot)
    ///   digital 1 in      lights digital feedback 10 on the panel
    ///
    /// Open the panel from the IP table in the OpenVP console ("open test panel") and press the buttons.
    /// </summary>
    public class ControlSystem : CrestronControlSystem
    {
        private XpanelForHtml5 _panel;
        private ThreeSeriesTcpIpEthernetIntersystemCommunications _eisc;

        public ControlSystem() : base()
        {
            try
            {
                CrestronEnvironment.ProgramStatusEventHandler += OnProgramStatus;
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in the constructor: {0}", e.Message);
            }
        }

        public override void InitializeSystem()
        {
            // The panel. Its IP ID must match the IP table of the program (here 0x03).
            _panel = new XpanelForHtml5(0x03, this);
            _panel.SigChange += OnPanelSig;
            if (_panel.Register() != eDeviceRegistrationUnRegistrationResponse.Success)
                ErrorLog.Error("Panel failed to register: {0}", _panel.RegistrationFailureReason);

            // The EISC. 127.0.0.2 is how a program talks to another program on the same processor (another OpenVP slot).
            _eisc = new ThreeSeriesTcpIpEthernetIntersystemCommunications(0x10, "127.0.0.2", this);
            _eisc.SigChange += OnEiscSig;
            _eisc.Register();

            _panel.StringInput[10].StringValue = "Starter program running";
            CrestronConsole.AddNewConsoleCommand(args => CrestronConsole.ConsoleCommandResponse("starter ok"),
                "starter", "Show that the starter program is running", ConsoleAccessLevelEnum.AccessOperator);
            ErrorLog.Notice("StarterProgram initialized");
        }

        private void OnPanelSig(BasicTriList device, SigEventArgs args)
        {
            switch (args.Event)
            {
                case eSigEvent.BoolChange when args.Sig.Number == 1 && args.Sig.BoolValue:
                    device.BooleanInput[1].BoolValue = !device.BooleanInput[1].BoolValue;
                    break;
                case eSigEvent.BoolChange when args.Sig.Number == 10:
                    _eisc.BooleanInput[1].BoolValue = args.Sig.BoolValue;
                    break;
                case eSigEvent.UShortChange when args.Sig.Number == 1:
                    device.UShortInput[1].UShortValue = args.Sig.UShortValue;
                    device.StringInput[1].StringValue = "Level " + args.Sig.UShortValue;
                    break;
                case eSigEvent.StringChange when args.Sig.Number == 1:
                    device.StringInput[2].StringValue = args.Sig.StringValue.ToUpperInvariant();
                    break;
            }
        }

        private void OnEiscSig(BasicTriList device, SigEventArgs args)
        {
            if (args.Event == eSigEvent.BoolChange && args.Sig.Number == 1)
                _panel.BooleanInput[10].BoolValue = args.Sig.BoolValue;
        }

        private void OnProgramStatus(eProgramStatusEventType type)
        {
            if (type == eProgramStatusEventType.Stopping)
                ErrorLog.Notice("StarterProgram stopping");
        }
    }
}
