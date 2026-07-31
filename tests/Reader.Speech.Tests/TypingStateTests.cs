using FluentAssertions;
using OpenReader.Speech;
using Xunit;

namespace OpenReader.Speech.Tests;

public class TypingStateTests
{
    [Fact]
    public void New_state_is_not_typing()
    {
        var state = new TypingState();
        state.IsTyping.Should().BeFalse();
    }

    [Fact]
    public void NotifyTyping_sets_IsTyping_true_within_window()
    {
        var state = new TypingState { Window = TimeSpan.FromMilliseconds(200) };
        state.NotifyTyping();
        state.IsTyping.Should().BeTrue();
    }

    [Fact]
    public void IsTyping_returns_false_after_window_expires()
    {
        var state = new TypingState { Window = TimeSpan.FromMilliseconds(20) };
        state.NotifyTyping();
        state.IsTyping.Should().BeTrue();
        Thread.Sleep(60);
        state.IsTyping.Should().BeFalse();
    }
}
