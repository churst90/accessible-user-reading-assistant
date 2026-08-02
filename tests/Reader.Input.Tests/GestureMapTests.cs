using FluentAssertions;
using Aura.Abstractions.Input;
using Aura.Input.Commands;
using Aura.Input.Gestures;
using Xunit;

namespace Aura.Input.Tests;

public class GestureMapTests
{
    private static RawInput Down(int vk, InputModifiers mods = InputModifiers.None)
        => new(InputSource.Keyboard, InputEventKind.KeyDown, vk, mods, DateTimeOffset.UtcNow);

    [Fact]
    public void Bind_then_resolve_returns_command()
    {
        var map = new GestureMap();
        map.Bind(new KeyChord(0x46, InputModifiers.Reader), ReaderCommand.ReportFocus);

        map.Resolve(Down(0x46, InputModifiers.Reader)).Should().Be(ReaderCommand.ReportFocus);
    }

    [Fact]
    public void Resolve_returns_None_when_modifiers_differ()
    {
        var map = new GestureMap();
        map.Bind(new KeyChord(0x46, InputModifiers.Reader), ReaderCommand.ReportFocus);

        map.Resolve(Down(0x46, InputModifiers.None)).Should().Be(ReaderCommand.None);
        map.Resolve(Down(0x46, InputModifiers.Reader | InputModifiers.Shift)).Should().Be(ReaderCommand.None);
    }

    [Fact]
    public void Resolve_only_fires_on_KeyDown()
    {
        var map = new GestureMap();
        map.Bind(new KeyChord(0x46, InputModifiers.Reader), ReaderCommand.ReportFocus);

        var up = new RawInput(InputSource.Keyboard, InputEventKind.KeyUp, 0x46, InputModifiers.Reader, DateTimeOffset.UtcNow);
        map.Resolve(up).Should().Be(ReaderCommand.None);
    }

    [Fact]
    public void Bind_replaces_existing_binding()
    {
        var map = new GestureMap();
        map.Bind(new KeyChord(0x46, InputModifiers.Reader), ReaderCommand.ReportFocus);
        map.Bind(new KeyChord(0x46, InputModifiers.Reader), ReaderCommand.ReportTitle);

        map.Resolve(Down(0x46, InputModifiers.Reader)).Should().Be(ReaderCommand.ReportTitle);
    }

    [Fact]
    public void Desktop_defaults_bind_nvda_numpad_review()
    {
        // NVDA desktop numpad cluster (NumLock ON):
        //   1 prev char   2 cur char   3 next char
        //   4 prev word   5 cur word   6 next word
        //   7 prev line   8 cur line   9 next line
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);

        map.Resolve(Down(0x61 /* NUMPAD1 */)).Should().Be(ReaderCommand.ReadPreviousCharacter);
        map.Resolve(Down(0x62 /* NUMPAD2 */)).Should().Be(ReaderCommand.ReadCharacter);
        map.Resolve(Down(0x63 /* NUMPAD3 */)).Should().Be(ReaderCommand.ReadNextCharacter);
        map.Resolve(Down(0x64 /* NUMPAD4 */)).Should().Be(ReaderCommand.ReadPreviousWord);
        map.Resolve(Down(0x65 /* NUMPAD5 */)).Should().Be(ReaderCommand.ReadWord);
        map.Resolve(Down(0x66 /* NUMPAD6 */)).Should().Be(ReaderCommand.ReadNextWord);
        map.Resolve(Down(0x67 /* NUMPAD7 */)).Should().Be(ReaderCommand.ReadPreviousLine);
        map.Resolve(Down(0x68 /* NUMPAD8 */)).Should().Be(ReaderCommand.ReadLine);
        map.Resolve(Down(0x69 /* NUMPAD9 */)).Should().Be(ReaderCommand.ReadNextLine);
    }

    [Fact]
    public void Desktop_review_jumps_use_shifted_numpad()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);

        map.Resolve(Down(0x67 /* NUMPAD7 */, InputModifiers.Shift)).Should().Be(ReaderCommand.ReviewMoveToTop);
        map.Resolve(Down(0x61 /* NUMPAD1 */, InputModifiers.Shift)).Should().Be(ReaderCommand.ReviewMoveToBottom);
    }

    [Fact]
    public void Desktop_say_all_bound_to_numpad_plus_and_reader_down()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);

        map.Resolve(Down(0x6B /* VK_ADD */, InputModifiers.Reader)).Should().Be(ReaderCommand.SayAllFromCursor);
        map.Resolve(Down(0x28 /* VK_DOWN */, InputModifiers.Reader)).Should().Be(ReaderCommand.SayAllFromCursor);
        map.Resolve(Down(0x28 /* VK_DOWN */, InputModifiers.Reader | InputModifiers.Shift)).Should().Be(ReaderCommand.SayAll);
    }

    [Fact]
    public void Desktop_reader_up_reads_current_line()
    {
        // NVDA desktop: Insert+Up Arrow = report current line.
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);

        map.Resolve(Down(0x26 /* VK_UP */, InputModifiers.Reader)).Should().Be(ReaderCommand.ReadLine);
    }

    [Fact]
    public void Laptop_defaults_bind_reader_arrows()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Laptop);

        map.Resolve(Down(0x25 /* VK_LEFT */, InputModifiers.Reader))
            .Should().Be(ReaderCommand.ReadPreviousCharacter);
        map.Resolve(Down(0x27 /* VK_RIGHT */, InputModifiers.Reader))
            .Should().Be(ReaderCommand.ReadNextCharacter);
        map.Resolve(Down(0x26 /* VK_UP */, InputModifiers.Reader))
            .Should().Be(ReaderCommand.ReadPreviousLine);
        map.Resolve(Down(0x28 /* VK_DOWN */, InputModifiers.Reader))
            .Should().Be(ReaderCommand.ReadNextLine);
    }

    [Fact]
    public void Laptop_period_chords_read_current_unit()
    {
        // NVDA laptop: NVDA+. = char, NVDA+Ctrl+. = word, NVDA+Shift+. = line.
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Laptop);

        map.Resolve(Down(0xBE /* OEM_PERIOD */, InputModifiers.Reader)).Should().Be(ReaderCommand.ReadCharacter);
        map.Resolve(Down(0xBE, InputModifiers.Reader | InputModifiers.Control)).Should().Be(ReaderCommand.ReadWord);
        map.Resolve(Down(0xBE, InputModifiers.Reader | InputModifiers.Shift)).Should().Be(ReaderCommand.ReadLine);
    }

    [Fact]
    public void Laptop_say_all_uses_reader_shift_a()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Laptop);

        map.Resolve(Down(0x41 /* A */, InputModifiers.Reader | InputModifiers.Shift))
            .Should().Be(ReaderCommand.SayAllFromCursor);
    }

    [Fact]
    public void OpenSettings_bound_in_both_layouts()
    {
        var desktop = new GestureMap();
        GestureBindings.ApplyDefaults(desktop, KeyboardLayout.Desktop);
        desktop.Resolve(Down(0x4F /* O */, InputModifiers.Reader)).Should().Be(ReaderCommand.OpenSettings);
        desktop.Resolve(Down(0x4E /* N */, InputModifiers.Reader)).Should().Be(ReaderCommand.OpenSettings);

        var laptop = new GestureMap();
        GestureBindings.ApplyDefaults(laptop, KeyboardLayout.Laptop);
        laptop.Resolve(Down(0x4F /* O */, InputModifiers.Reader)).Should().Be(ReaderCommand.OpenSettings);
    }

    [Fact]
    public void Shared_meta_commands_bound()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);

        map.Resolve(Down(0x51 /* Q */, InputModifiers.Reader)).Should().Be(ReaderCommand.OpenExitDialog);
        map.Resolve(Down(0x50 /* P */, InputModifiers.Reader)).Should().Be(ReaderCommand.CyclePunctuationLevel);
        map.Resolve(Down(0x31 /* 1 */, InputModifiers.Reader)).Should().Be(ReaderCommand.ToggleKeyboardHelp);
        map.Resolve(Down(0x70 /* F1 */, InputModifiers.Reader)).Should().Be(ReaderCommand.OpenDocumentation);
        map.Resolve(Down(0x7B /* F12 */, InputModifiers.Reader)).Should().Be(ReaderCommand.ReportTime);
        map.Resolve(Down(0x54 /* T */, InputModifiers.Reader)).Should().Be(ReaderCommand.ReportTitle);
        map.Resolve(Down(0x09 /* TAB */, InputModifiers.Reader)).Should().Be(ReaderCommand.ReportFocus);
    }

    [Fact]
    public void Reader_A_says_all_from_cursor_in_both_layouts()
    {
        // NVDA convention shared across desktop and laptop: NVDA+a starts
        // continuous reading from the caret. Insert+A on desktop, CapsLock+A
        // on laptop both produce the same chord (Reader modifier).
        foreach (var layout in new[] { KeyboardLayout.Desktop, KeyboardLayout.Laptop })
        {
            var map = new GestureMap();
            GestureBindings.ApplyDefaults(map, layout);
            map.Resolve(Down(0x41 /* A */, InputModifiers.Reader))
                .Should().Be(ReaderCommand.SayAllFromCursor, $"layout={layout}");
        }
    }

    [Fact]
    public void Reader_L_reads_current_line_in_both_layouts()
    {
        // NVDA laptop: NVDA+l reads the current line. We bind it in both
        // layouts so the letter-row chord works on either keyboard.
        foreach (var layout in new[] { KeyboardLayout.Desktop, KeyboardLayout.Laptop })
        {
            var map = new GestureMap();
            GestureBindings.ApplyDefaults(map, layout);
            map.Resolve(Down(0x4C /* L */, InputModifiers.Reader))
                .Should().Be(ReaderCommand.ReadLine, $"layout={layout}");
        }
    }

    [Fact]
    public void Reader_Ctrl_S_opens_synthesizer_dialog()
    {
        // Reader+Ctrl+S so plain Ctrl+S still saves in the focused app —
        // chord matching is exact on modifiers.
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);
        map.Resolve(Down(0x53 /* S */, InputModifiers.Reader | InputModifiers.Control))
            .Should().Be(ReaderCommand.OpenSynthesizerDialog);

        // Plain Ctrl+S must NOT match the synthesizer chord.
        map.Resolve(Down(0x53, InputModifiers.Control)).Should().Be(ReaderCommand.None);
    }

    [Fact]
    public void Bare_Ctrl_resolves_to_StopSpeech()
    {
        // Regression: the Win32 hook normalizes VK_LCONTROL/VK_RCONTROL to
        // VK_CONTROL (0x11) so this binding actually fires when the user
        // presses either physical Control key alone.
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);

        map.Resolve(Down(0x11 /* VK_CONTROL */, InputModifiers.Control))
            .Should().Be(ReaderCommand.StopSpeech);
    }

    [Fact]
    public void Reset_replaces_existing_bindings()
    {
        var map = new GestureMap();
        map.Bind(new KeyChord(0x70 /* F1 */, InputModifiers.None), ReaderCommand.SayAll);
        GestureBindings.Reset(map, KeyboardLayout.Desktop);

        map.Resolve(Down(0x70)).Should().Be(ReaderCommand.None);
        map.Resolve(Down(0x68 /* VK_NUMPAD8 */)).Should().Be(ReaderCommand.ReadLine);
    }
}
