using Xunit;

namespace Aura.Platform.Windows.Tests;

public class PlaceholderTests
{
    [Fact(Skip = "Phase 1 — UIA provider implementation not yet present")]
    public void UiaProvider_translates_focus_event_to_AccessibilityEvent() { }
}
