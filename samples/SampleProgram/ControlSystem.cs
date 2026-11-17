using System;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.CrestronThread;
using Crestron.SimplSharpPro.DeviceSupport;
using Crestron.SimplSharpPro.EthernetCommunication;
using Crestron.SimplSharpPro.UI;

namespace SampleProgram
{
    /// <summary>
    /// Written the way the Crestron program template is written.
    /// Panel at IPID 03:
    ///   press d1        toggles feedback d1
    ///   analog a1       echoes to a1 feedback and to serial s1 as text
    ///   serial s1 in    echoes back upper-case on s2
    ///   press d10       is passed to EISC IPID 10 (127.0.0.2) join d1, so another slot can see it
    /// EISC d1 from the other side sets panel feedback d10.
    /// </summary>
    public class ControlSystem : CrestronControlSystem
    {
        private XpanelForHtml5 _panel;
        private ThreeSeriesTcpIpEthernetIntersystemCommunications _eisc;
        private CTimer _heartbeat;
        private ushort _beats;

        public ControlSystem() : base()
        {
            try
            {
                Thread.MaxNumberOfUserThreads = 20;
                CrestronEnvironment.ProgramStatusEventHandler += OnProgramStatus;
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in the constructor: {0}", e.Message);
            }
        }

        public override void InitializeSystem()
        {
            _panel = new XpanelForHtml5(0x03, this);
            _panel.SigChange += OnPanelSig;
            if (_panel.Register() != eDeviceRegistrationUnRegistrationResponse.Success)
                ErrorLog.Error("Panel failed to register: {0}", _panel.RegistrationFailureReason);

            _eisc = new ThreeSeriesTcpIpEthernetIntersystemCommunications(0x10, "127.0.0.2", this);
            _eisc.SigChange += OnEiscSig;
            _eisc.Register();

            _panel.StringInput[10].StringValue = "SampleProgram running";
            _heartbeat = new CTimer(_ => _panel.UShortInput[10].UShortValue = ++_beats, null, 1000, 1000);

            CrestronConsole.AddNewConsoleCommand(args => CrestronConsole.ConsoleCommandResponse("sample ok, beats={0}, args={1}", _beats, args),
                "sample", "Show SampleProgram status", ConsoleAccessLevelEnum.AccessOperator);

            ErrorLog.Notice("SampleProgram initialized");
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
            {
                _heartbeat?.Stop();
                ErrorLog.Notice("SampleProgram stopping");
            }
        }
    }
}
