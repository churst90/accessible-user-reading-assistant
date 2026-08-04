using System.Buffers;

namespace Aura.Abstractions.Output;

/// <summary>
/// What counts as blank text.
/// </summary>
/// <remarks>
/// <para>
/// Taken from NVDA's <c>BLANK_CHUNK_CHARS</c>, including the non-breaking
/// space: a line made of U+00A0 looks empty, is not empty, and web pages are
/// full of them. A reader that treats it as ordinary text says nothing audible
/// and the user cannot tell whether the line was blank or the reader failed.
/// </para>
/// <para>
/// The rule this exists for is <b>not</b> "is this string empty". It is
/// <see cref="Presentation.IsBlank"/>: an announcement is blank only when
/// <em>nothing in the whole composed presentation</em> is non-blank. That is
/// why an empty line inside a list item says "list item" rather than "blank",
/// and it is why comparing one announcement's text against the last one's could
/// never work — that approach cannot tell "nothing moved" from "the next thing
/// happens to read the same", which is how arrowing through consecutive blank
/// lines went silent.
/// </para>
/// </remarks>
public static class Blank
{
    private static readonly SearchValues<char> BlankChars =
        SearchValues.Create(" \n\r\t\0 ​﻿");

    /// <summary>True when <paramref name="text"/> has nothing audible in it.</summary>
    public static bool Is(string? text) =>
        string.IsNullOrEmpty(text) || !text.AsSpan().ContainsAnyExcept(BlankChars);
}
