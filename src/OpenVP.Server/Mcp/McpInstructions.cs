namespace OpenVP.Server.Mcp;

/// <summary>What the MCP client is told about this server when it connects (shown to the AI assistant, not the user).</summary>
public static class McpInstructions
{
    public const string Text = """
        OpenVP is a virtual Crestron-style control processor. It runs SIMPL# Pro programs (.cpz/.dll) and SIMPL Windows programs (.lpz/.smw)
        in up to 10 slots. These tools let you test a program the way a person at a touch panel would.

        Typical test loop:
        1. list_slots to see what is loaded. load_program to load a freshly built program (path on the OpenVP computer), or control_slot restart for a clean start.
        2. list_controls to see every button, slider and text field on the program's panel (the same controls as the web console's generated panel).
        3. press_button / set_value to use them. Each returns the control's feedback before and after, every signal that changed, log lines and exceptions.
        4. wait_for_signal to assert a value that arrives later (ramps, delays). read_signals, read_log and get_exceptions to look closer.
        5. test_all_buttons for a quick smoke test of every button; replay_recording to run a recording made in the console as a regression test.

        Presses are real: whatever the program drives (serial ports, relays, other slots over EISC) reacts. Don't press buttons on a slot that runs a live system
        unless the user asked you to. A slot locked in the console ("live event") refuses changes.
        """;
}
