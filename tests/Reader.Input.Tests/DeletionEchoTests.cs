using Aura.Abstractions.Input;
using Aura.Input.Echo;
using FluentAssertions;
using Xunit;

namespace Aura.Input.Tests;

/// <summary>
/// What Backspace and Delete say.
/// </summary>
/// <remarks>
/// <para>
/// These used to assert that a <c>SpeakDeletedCharacters</c> setting defaulted
/// to true. That tested a property, not a behaviour — and once the behaviour
/// stopped consulting the property the tests went on passing, which is the
/// worst thing a test can do. The setting is gone: a reader that silently
/// discards what you just removed has failed at the one job that keystroke has,
/// and that is not a preference.
/// </para>
/// <para>
/// A sighted user glances at the line to see what disappeared. Without this the
/// only way to find out is to navigate back over the text and re-read it, so a
/// user who finds per-character echo too chatty while typing still needs to
/// know what they just destroyed.
/// </para>
/// </remarks>
public class DeletionEchoTests
{
    private sealed class FakeInput : IInputSource
    {
        public event EventHandler<RawInput>? RawInputReceived;

        public void Press(int vk) => RawInputReceived?.Invoke(this, new RawInput(
            InputSource.Keyboard, InputEventKind.KeyDown, vk, InputModifiers.None, DateTimeOffset.UnixEpoch));

        public ValueTask StartAsync(CancellationToken cancellationToken) => default;

        public ValueTask DisposeAsync() => default;
    }

    private const int VkBack = 0x08;
    private const int VkDelete = 0x2E;

    private static (FakeInput Input, List<string> Spoken) Harness(
        string? charBefore = null,
        string? charAfter = null,
        KeyEchoSettings? settings = null)
    {
        var input = new FakeInput();
        var spoken = new List<string>();
        var service = new KeyEchoService(
            input,
            spoken.Add,
            settings ?? KeyEchoSettings.Defaults,
            charBeforeCaret: () => charBefore,
            charAfterCaret: () => charAfter);
        service.Start();
        return (input, spoken);
    }

    // The word-buffer path — Backspace right after typing, where the buffer
    // holds exactly what this user typed — is not covered here. Filling the
    // buffer means going through KeyTranslator, which reads the live keyboard
    // layout through Win32 and therefore cannot run in this suite. It belongs
    // in the Windows integration suite (F5b), and until that exists it is
    // covered by ear.

    [Fact]
    public void Backspace_in_existing_text_announces_the_character_behind_the_caret()
    {
        // The case that never worked. Arrow into existing text and delete, and
        // the word buffer knows nothing — which is most deleting. The caret
        // tracker keeps the neighbouring characters as it goes, precisely
        // because this moment cannot read them for itself.
        var (input, spoken) = Harness(charBefore: "z");

        input.Press(VkBack);

        spoken.Should().ContainSingle().Which.Should().Be("z");
    }

    [Fact]
    public void Delete_announces_the_character_ahead_of_the_caret()
    {
        // Delete removes what is AHEAD, so the word buffer never had anything
        // to say about it. It said nothing at all.
        var (input, spoken) = Harness(charAfter: "q");

        input.Press(VkDelete);

        spoken.Should().ContainSingle().Which.Should().Be("q");
    }

    [Fact]
    public void Deletion_is_announced_even_with_every_echo_setting_off()
    {
        var silent = new KeyEchoSettings
        {
            SpeakCharacters = false,
            SpeakWords = false,
            SpeakCommandKeys = false,
        };
        var (input, spoken) = Harness(charBefore: "z", charAfter: "q", settings: silent);

        input.Press(VkBack);
        input.Press(VkDelete);

        spoken.Should().Equal("z", "q");
    }

    [Fact]
    public void With_nothing_knowable_the_key_is_named_only_for_users_who_asked_for_key_names()
    {
        var (quiet, quietSpoken) = Harness(settings: KeyEchoSettings.Defaults with { SpeakCommandKeys = false });
        quiet.Press(VkDelete);
        quietSpoken.Should().BeEmpty("silence beats saying a word the user switched off");

        var (loud, loudSpoken) = Harness(settings: KeyEchoSettings.Defaults with { SpeakCommandKeys = true });
        loud.Press(VkDelete);
        loudSpoken.Should().ContainSingle().Which.Should().Be("delete");
    }
}
