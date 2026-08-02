namespace OpenReader.Config;

/// <summary>
/// Merges layered <see cref="ReaderConfig"/> instances. Later layers override
/// earlier ones; <c>null</c> at any layer means "inherit from below".
/// </summary>
/// <remarks>
/// <para>
/// Scalar properties: last non-null wins.
/// </para>
/// <para>
/// Records (Speech / Input): merged property-by-property, not replaced
/// wholesale, so a user setting only <c>RatePercent</c> doesn't blow away
/// the default voice id.
/// </para>
/// <para>
/// Dictionaries (key bindings): merged with last-write-wins per key. There is
/// no "remove" syntax yet — to revert a binding, restate the default.
/// </para>
/// </remarks>
public static class ConfigMerger
{
    public static ReaderConfig Merge(params ReaderConfig?[] layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var result = new ReaderConfig();
        foreach (var layer in layers)
        {
            if (layer is null)
            {
                continue;
            }
            result = MergePair(result, layer);
        }
        return result;
    }

    private static ReaderConfig MergePair(ReaderConfig lower, ReaderConfig upper)
    {
        return new ReaderConfig
        {
            Version = upper.Version ?? lower.Version,
            General = MergeGeneral(lower.General, upper.General),
            Speech = MergeSpeech(lower.Speech, upper.Speech),
            Input = MergeInput(lower.Input, upper.Input),
            Keyboard = MergeKeyboard(lower.Keyboard, upper.Keyboard),
        };
    }

    private static GeneralConfig? MergeGeneral(GeneralConfig? lower, GeneralConfig? upper)
    {
        if (lower is null)
        {
            return upper;
        }
        if (upper is null)
        {
            return lower;
        }
        return new GeneralConfig
        {
            Profile = upper.Profile ?? lower.Profile,
            StartWithWindows = upper.StartWithWindows ?? lower.StartWithWindows,
        };
    }

    private static KeyboardConfig? MergeKeyboard(KeyboardConfig? lower, KeyboardConfig? upper)
    {
        if (lower is null)
        {
            return upper;
        }
        if (upper is null)
        {
            return lower;
        }
        return new KeyboardConfig
        {
            Layout = upper.Layout ?? lower.Layout,
            SpeakCommandKeys = upper.SpeakCommandKeys ?? lower.SpeakCommandKeys,
            SpeakCharacters = upper.SpeakCharacters ?? lower.SpeakCharacters,
            SpeakWords = upper.SpeakWords ?? lower.SpeakWords,
        };
    }

    private static SpeechConfig? MergeSpeech(SpeechConfig? lower, SpeechConfig? upper)
    {
        if (lower is null)
        {
            return upper;
        }
        if (upper is null)
        {
            return lower;
        }
        return new SpeechConfig
        {
            Engine = upper.Engine ?? lower.Engine,
            VoiceId = upper.VoiceId ?? lower.VoiceId,
            RatePercent = upper.RatePercent ?? lower.RatePercent,
            VolumeDelta = upper.VolumeDelta ?? lower.VolumeDelta,
            PitchDelta = upper.PitchDelta ?? lower.PitchDelta,
        };
    }

    private static InputConfig? MergeInput(InputConfig? lower, InputConfig? upper)
    {
        if (lower is null)
        {
            return upper;
        }
        if (upper is null)
        {
            return lower;
        }

        Dictionary<string, string>? bindings = null;
        if (lower.KeyBindings is not null || upper.KeyBindings is not null)
        {
            bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (lower.KeyBindings is not null)
            {
                foreach (var kv in lower.KeyBindings)
                {
                    bindings[kv.Key] = kv.Value;
                }
            }
            if (upper.KeyBindings is not null)
            {
                foreach (var kv in upper.KeyBindings)
                {
                    bindings[kv.Key] = kv.Value;
                }
            }
        }

        return new InputConfig
        {
            ReaderModifier = upper.ReaderModifier ?? lower.ReaderModifier,
            KeyBindings = bindings,
        };
    }
}
