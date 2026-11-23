using System.Text;
using OpenVP.Core;
using OpenVP.Core.Ops;

namespace OpenVP.Tests;

/// <summary>The file editor: validation with line numbers, saving with the old content kept for Undo, and the "changed on disk" check.</summary>
public static class TextEditorTests
{
    static (string Dir, Trash Trash) Setup() { var d = Paths.TempData(); return (d, new Trash(d)); }

    [Test]
    public static void Json_and_xml_errors_say_which_line()
    {
        var j = TextFileEditor.Validate("rooms.json", "{\n  \"a\": 1,\n  \"b\": ,\n  \"c\": 3\n}");
        Assert.True(j != null && j.Line == 3, $"JSON error on line 3, got {j?.Line}: {j?.Message}");
        Assert.True(!j!.Message.Contains("Path:") && !j.Message.Contains("LineNumber"), "message is plain: " + j.Message);
        Assert.True(TextFileEditor.Validate("a.json", "{\"a\":[1,2,3]}") == null, "valid json");
        Assert.True(TextFileEditor.Validate("a.json", "") != null, "empty json is invalid");
        Assert.Equal(1, TextFileEditor.Validate("a.json", "{\"a\":1,}")!.Line, "trailing comma, line 1");
        var x = TextFileEditor.Validate("cfg.xml", "<root>\n  <a>1</a>\n  <b>2\n</root>");
        Assert.True(x != null && x.Line >= 3, $"XML error near line 4, got {x?.Line}: {x?.Message}");
        Assert.True(TextFileEditor.Validate("cfg.xml", "<root><a>1</a></root>") == null, "valid xml");
        Assert.True(TextFileEditor.Validate("cfg.xml", "<!DOCTYPE x [<!ENTITY a SYSTEM \"file:///etc/passwd\">]><x>&a;</x>") != null, "DTDs are refused, so no entity can read a file");
        Assert.True(TextFileEditor.Validate("notes.txt", "{{{") == null, "plain text is never checked");
    }

    [Test]
    public static void Save_keeps_the_previous_content_in_the_trash_for_undo()
    {
        var (dir, trash) = Setup();
        var f = Path.Combine(dir, "program01", "nvram", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, "{\"volume\":10}");
        var opened = TextFileEditor.Read(f);
        Assert.Equal("{\"volume\":10}", opened.Text, "read");
        var (state, undo) = TextFileEditor.Save(f, "{\"volume\":55}", opened.Tag, trash, 1);
        Assert.Equal("{\"volume\":55}", File.ReadAllText(f), "saved");
        Assert.True(undo != null && state.Tag != opened.Tag, "undo id, new tag");
        var e = trash.Get(undo)!;
        Assert.True(e.Kind == "edit" && e.Slot == 1 && e.Label.Contains("settings.json"), "trash entry");
        trash.Restore(undo);
        Assert.Equal("{\"volume\":10}", File.ReadAllText(f), "undo puts the earlier content back over the new");
        Assert.True(trash.Get(undo) == null, "the entry is used up");
    }

    [Test]
    public static void Save_refuses_a_file_that_changed_on_disk_unless_forced()
    {
        var (dir, trash) = Setup();
        var f = Path.Combine(dir, "a.txt");
        File.WriteAllText(f, "first");
        var opened = TextFileEditor.Read(f);
        File.WriteAllText(f, "changed by the program");
        var ex = Assert.Throws<EditConflictException>(() => TextFileEditor.Save(f, "my edit", opened.Tag, trash, 1));
        Assert.True(ex.Message.Contains("changed on disk"), ex.Message);
        Assert.Equal("changed by the program", File.ReadAllText(f), "nothing was overwritten");
        var (_, undo) = TextFileEditor.Save(f, "my edit", opened.Tag, trash, 1, force: true);
        Assert.Equal("my edit", File.ReadAllText(f), "forced save");
        trash.Restore(undo);
        Assert.Equal("changed by the program", File.ReadAllText(f), "and the content that was overwritten can be brought back");
        // A file that was deleted after it was opened.
        File.Delete(f);
        Assert.True(Assert.Throws<EditConflictException>(() => TextFileEditor.Save(f, "x", opened.Tag, trash, 1)).Message.Contains("deleted"), "deleted");
        // Saving the same content someone else saved is not a conflict.
        File.WriteAllText(f, "same");
        var t = TextFileEditor.Read(f).Tag;
        File.WriteAllText(f, "same");
        TextFileEditor.Save(f, "next", t, trash, 1);
    }

    [Test]
    public static void Save_checks_json_and_size_and_keeps_the_files_own_line_endings()
    {
        var (dir, trash) = Setup();
        var f = Path.Combine(dir, "c.json");
        File.WriteAllBytes(f, Encoding.UTF8.GetBytes("{\r\n  \"a\": 1\r\n}\r\n"));
        var o = TextFileEditor.Read(f);
        Assert.True(o.Newline == "crlf" && !o.Text.Contains('\r'), "CRLF detected, text shown with \\n");
        var bad = Assert.Throws<EditInvalidException>(() => TextFileEditor.Save(f, "{\n  \"a\": \n}", o.Tag, trash, 1));
        Assert.Equal(3, bad.Problem.Line, "the line of the error is reported");
        Assert.True(File.ReadAllText(f).Contains("\"a\": 1"), "invalid text wasn't saved");
        TextFileEditor.Save(f, "{\n  \"a\": 2\n}\n", o.Tag, trash, 1);
        Assert.Equal("{\r\n  \"a\": 2\r\n}\r\n", File.ReadAllText(f), "saved with CRLF as before");
        var o2 = TextFileEditor.Read(f);
        TextFileEditor.Save(f, "not json at all", o2.Tag, trash, 1, allowInvalid: true);
        Assert.Equal("not json at all", File.ReadAllText(f), "saving anyway works when asked");

        var big = Path.Combine(dir, "big.txt");
        File.WriteAllText(big, new string('a', (int)TextFileEditor.MaxBytes + 10));
        Assert.True(Assert.Throws<EditTooLargeException>(() => TextFileEditor.Read(big)).Message.Contains("2 MB"), "too large to open");
        File.WriteAllText(Path.Combine(dir, "ok.txt"), "x");
        Assert.True(Assert.Throws<EditTooLargeException>(() => TextFileEditor.Save(Path.Combine(dir, "ok.txt"), new string('a', (int)TextFileEditor.MaxBytes + 1), null, trash, 1)).Message.Contains("limit"), "too large to save");
    }

    [Test]
    public static void The_editor_only_opens_text()
    {
        var (dir, _) = Setup();
        File.WriteAllBytes(Path.Combine(dir, "bin.txt"), [1, 2, 0, 3]);
        Assert.True(Assert.Throws<ArgumentException>(() => TextFileEditor.Read(Path.Combine(dir, "bin.txt"))).Message.Contains("binary"), "binary content");
        File.WriteAllBytes(Path.Combine(dir, "prog.dll"), [1, 2, 3]);
        Assert.True(Assert.Throws<ArgumentException>(() => TextFileEditor.Read(Path.Combine(dir, "prog.dll"))).Message.Contains("text files"), "wrong type");
        foreach (var ext in new[] { "json", "xml", "txt", "csv", "ini", "cfg", "usp", "smw", "JSON" }) Assert.True(TextFileEditor.IsEditableName("x." + ext), "." + ext);
        Assert.True(!TextFileEditor.IsEditableName("x.dll") && !TextFileEditor.IsEditableName("x.cpz") && !TextFileEditor.IsEditableName("noext"), "programs and unknown types are not editable");
        // Latin-1 text keeps its encoding.
        var f = Path.Combine(dir, "latin.txt");
        File.WriteAllBytes(f, [0x63, 0x61, 0x66, 0xE9]);
        var t = TextFileEditor.Read(f);
        Assert.True(t.Encoding == "latin1" && t.Text == "café", "latin1 read");
        TextFileEditor.Save(f, "café!", t.Tag, new Trash(dir), 1);
        Assert.True(File.ReadAllBytes(f).SequenceEqual(new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x21 }), "latin1 written back as it was");
    }
}
