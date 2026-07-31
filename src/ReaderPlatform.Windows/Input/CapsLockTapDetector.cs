using System.Runtime.Versioning;

namespace OpenReader.Platform.Windows.Input;

/// <summary>
/// Detects "solo taps" of CapsLock — a quick down/up with no other key
/// pressed in between — used by the laptop layout to convert two CapsLock
/// taps in close succession into a screen-reader on/off toggle.
/// </summary>
/// <remarks>
/// State is fed from the keyboard hook. The detector itself does not call any
/// Win32 APIs — it just observes events and invokes a handler when a solo
/// tap completes within the configured time window.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class CapsLockTapDetector
{
    private const int VK_CAPITAL = 0x14;
    private static readonly TimeSpan SoloTapWindow = TimeSpan.FromMilliseconds(500);

    private readonly Action _onSoloTap;
    private long _capsLockDownTicks;
    private bool _capsLockUsedAsModifier;

    public CapsLockTapDetector(Action onSoloTap)
    {
        _onSoloTap = onSoloTap ?? throw new ArgumentNullException(nameof(onSoloTap));
    }

    /// <summary>Feed a key event in. <paramref name="capsIsActive"/> indicates whether CapsLock is currently the active Reader key.</summary>
    public void Observe(int vk, bool down, bool capsIsActive)
    {
        if (vk == VK_CAPITAL && capsIsActive)
        {
            ObserveCapsLock(down);
            return;
        }
        // Any non-CapsLock key pressed while CapsLock is held means CapsLock
        // is being used as a modifier, not a solo tap.
        if (down && _capsLockDownTicks != 0)
        {
            _capsLockUsedAsModifier = true;
        }
    }

    private void ObserveCapsLock(bool down)
    {
        if (down)
        {
            if (_capsLockDownTicks == 0)
            {
                _capsLockDownTicks = DateTime.UtcNow.Ticks;
                _capsLockUsedAsModifier = false;
            }
            return;
        }

        // Up.
        var heldFor = DateTime.UtcNow.Ticks - _capsLockDownTicks;
        var wasSoloTap = !_capsLockUsedAsModifier
            && _capsLockDownTicks != 0
            && heldFor < SoloTapWindow.Ticks;
        _capsLockDownTicks = 0;
        if (wasSoloTap)
        {
            _onSoloTap();
        }
    }
}
