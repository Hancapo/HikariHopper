namespace HikariHopper.Core;

/// <summary>One titled group of facts in an item's properties: "SIZE" with "In the archive" and "Unpacked".</summary>
public sealed record PropertySection(string Heading, IReadOnlyList<(string Label, string Value)> Rows);

/// <summary>What the Properties sheet shows about one file or folder.</summary>
/// <param name="Glyph">The explorer row's glyph kind: <c>folder</c>, or <c>file</c> refined by <paramref name="Kind"/>.</param>
public sealed record EntryProperties(string Name, string Kind, string Glyph, IReadOnlyList<PropertySection> Sections);
