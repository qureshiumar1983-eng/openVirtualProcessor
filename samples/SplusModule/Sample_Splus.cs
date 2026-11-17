// Equivalent SIMPL+ source:
//
//   DIGITAL_INPUT  PRESS, START;
//   ANALOG_INPUT   LEVEL;
//   STRING_INPUT   NAME$[50];
//   DIGITAL_OUTPUT BUSY;
//   ANALOG_OUTPUT  DOUBLED;
//   STRING_OUTPUT  GREETING$, TICKS$;
//   INTEGER_PARAMETER STEP;
//   INTEGER count;
//
//   PUSH PRESS    { BUSY = 1; DELAY(5); BUSY = 0; }
//   CHANGE LEVEL  { DOUBLED = LEVEL * 2; }
//   CHANGE NAME$  { MAKESTRING(GREETING$, "Hello, %s!", NAME$); }
//   PUSH START    { WHILE (START) { WAIT(5, tick) { count = count + STEP; MAKESTRING(TICKS$, "%d", count); } } }
//   FUNCTION MAIN() { WaitForInitializationComplete(); GREETING$ = "ready"; }

using System;
using Crestron.Logos.SplusLibrary;
using Crestron.Logos.SplusObjects;
using Amib.Threading.Internal;

namespace UserModule_SAMPLE_SPLUS
{
    public class UserModuleClass_SAMPLE_SPLUS : SplusObject
    {
        const uint PRESS__DigitalInput__ = 0;
        const uint START__DigitalInput__ = 1;
        const uint LEVEL__AnalogSerialInput__ = 0;
        const uint NAME__DOLLAR____AnalogSerialInput__ = 1;
        const uint BUSY__DigitalOutput__ = 0;
        const uint DOUBLED__AnalogSerialOutput__ = 0;
        const uint GREETING__DOLLAR____AnalogSerialOutput__ = 1;
        const uint TICKS__DOLLAR____AnalogSerialOutput__ = 2;
        const uint STEP__Parameter__ = 2;

        DigitalInput PRESS, START;
        AnalogInput LEVEL;
        StringInput NAME__DOLLAR__;
        DigitalOutput BUSY;
        AnalogOutput DOUBLED;
        StringOutput GREETING__DOLLAR__, TICKS__DOLLAR__;
        UShortParameter STEP;
        ushort COUNT;

        public UserModuleClass_SAMPLE_SPLUS(string InstanceName, string ReferenceID, CrestronStringEncoding nEncodingType)
            : base(InstanceName, ReferenceID, nEncodingType) { }

        object PRESS_OnPush_0(object __EventInfo__)
        {
            SignalEventArgs args = (SignalEventArgs)__EventInfo__;
            try
            {
                SplusExecutionContext ctx = SplusThreadStartCode(args);
                BUSY.Value = 1;
                Functions.Delay(5);
                BUSY.Value = 0;
            }
            catch (Exception e) { ObjectCatchHandler(e); }
            finally { ObjectFinallyHandler(args); }
            return this;
        }

        object LEVEL_OnChange_1(object __EventInfo__)
        {
            SignalEventArgs args = (SignalEventArgs)__EventInfo__;
            try
            {
                SplusExecutionContext ctx = SplusThreadStartCode(args);
                DOUBLED.Value = (ushort)(LEVEL.UshortValue * 2);
            }
            catch (Exception e) { ObjectCatchHandler(e); }
            finally { ObjectFinallyHandler(args); }
            return this;
        }

        object NAME_OnChange_2(object __EventInfo__)
        {
            SignalEventArgs args = (SignalEventArgs)__EventInfo__;
            try
            {
                SplusExecutionContext ctx = SplusThreadStartCode(args);
                MakeString(GREETING__DOLLAR__, "Hello, {0}!", NAME__DOLLAR__);
            }
            catch (Exception e) { ObjectCatchHandler(e); }
            finally { ObjectFinallyHandler(args); }
            return this;
        }

        object START_OnPush_3(object __EventInfo__)
        {
            SignalEventArgs args = (SignalEventArgs)__EventInfo__;
            try
            {
                SplusExecutionContext ctx = SplusThreadStartCode(args);
                while (Functions.TestForTrue(START.Value))
                    CreateWait("TICK", 5, TICK_Callback);
            }
            catch (Exception e) { ObjectCatchHandler(e); }
            finally { ObjectFinallyHandler(args); }
            return this;
        }

        void TICK_Callback(object __waitObj__)
        {
            Wait w = (Wait)__waitObj__;
            try
            {
                SplusExecutionContext ctx = SplusThreadStartCode(w);
                COUNT = (ushort)(COUNT + STEP.Value);
                MakeString(TICKS__DOLLAR__, "{0:d}", COUNT);
            }
            catch (Exception e) { ObjectCatchHandler(e); }
            finally { w.RemoveFromList(); ObjectFinallyHandler(); }
        }

        public override object FunctionMain(object __obj__)
        {
            try
            {
                SplusExecutionContext ctx = SplusFunctionMainStartCode();
                WaitForInitializationComplete();
                GREETING__DOLLAR__.UpdateValue("ready");
            }
            catch (Exception e) { ObjectCatchHandler(e); }
            finally { ObjectFinallyHandler(); }
            return __obj__;
        }

        public override void LogosSplusInitialize()
        {
            PRESS = new DigitalInput(PRESS__DigitalInput__, this);
            m_DigitalInputList.Add(PRESS__DigitalInput__, PRESS);
            START = new DigitalInput(START__DigitalInput__, this);
            m_DigitalInputList.Add(START__DigitalInput__, START);
            LEVEL = new AnalogInput(LEVEL__AnalogSerialInput__, this);
            m_AnalogInputList.Add(LEVEL__AnalogSerialInput__, LEVEL);
            NAME__DOLLAR__ = new StringInput(NAME__DOLLAR____AnalogSerialInput__, 50, this);
            m_StringInputList.Add(NAME__DOLLAR____AnalogSerialInput__, NAME__DOLLAR__);
            BUSY = new DigitalOutput(BUSY__DigitalOutput__, this);
            m_DigitalOutputList.Add(BUSY__DigitalOutput__, BUSY);
            DOUBLED = new AnalogOutput(DOUBLED__AnalogSerialOutput__, this);
            m_AnalogOutputList.Add(DOUBLED__AnalogSerialOutput__, DOUBLED);
            GREETING__DOLLAR__ = new StringOutput(GREETING__DOLLAR____AnalogSerialOutput__, this);
            m_StringOutputList.Add(GREETING__DOLLAR____AnalogSerialOutput__, GREETING__DOLLAR__);
            TICKS__DOLLAR__ = new StringOutput(TICKS__DOLLAR____AnalogSerialOutput__, this);
            m_StringOutputList.Add(TICKS__DOLLAR____AnalogSerialOutput__, TICKS__DOLLAR__);
            STEP = new UShortParameter(STEP__Parameter__, this);
            m_ParameterList.Add(STEP__Parameter__, STEP);

            PRESS.OnDigitalPush.Add(new InputChangeHandlerWrapper(PRESS_OnPush_0, false));
            LEVEL.OnAnalogChange.Add(new InputChangeHandlerWrapper(LEVEL_OnChange_1, false));
            NAME__DOLLAR__.OnSerialChange.Add(new InputChangeHandlerWrapper(NAME_OnChange_2, false));
            START.OnDigitalPush.Add(new InputChangeHandlerWrapper(START_OnPush_3, false));
        }
    }
}
