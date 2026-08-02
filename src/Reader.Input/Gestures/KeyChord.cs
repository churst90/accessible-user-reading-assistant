using Aura.Abstractions.Input;

namespace Aura.Input.Gestures;

/// <summary>
/// Immutable description of a keyboard chord — a virtual-key code plus the
/// modifier set that must be active when it fires.
/// </summary>
/// <remarks>
/// Modifier-state matching is exact: if a chord requires <c>Reader</c>, then
/// <c>Reader|Shift</c> will not match. This keeps <c>Reader+Down</c> from
/// firing when the user is shift-selecting with <c>Reader+Shift+Down</c>.
/// </remarks>
public readonly record struct KeyChord(int KeyCode, InputModifiers Modifiers)
{
    public bool Matches(RawInput input) =>
        input.Kind == InputEventKind.KeyDown
        && input.KeyCode == KeyCode
        && (input.Modifiers & ~InputModifiers.None) == Modifiers;
}
