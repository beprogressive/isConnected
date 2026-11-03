namespace IsConnected;

internal enum HighlightArea
{
    FullScreen,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

internal enum HighlightEdge
{
    Left,
    Right,
    Top,
    Bottom,
}

internal enum HighlightCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

internal sealed record HighlightAreaDefinition(
    HighlightArea Area,
    string DisplayName,
    IReadOnlyList<HighlightEdge> Edges,
    HighlightCorner? Corner)
{
    public bool IsCorner => Corner.HasValue;
}

internal static class HighlightAreaCatalog
{
    private static readonly HighlightAreaDefinition[] Definitions =
    [
        new(
            HighlightArea.FullScreen,
            "Full screen",
            [HighlightEdge.Top, HighlightEdge.Bottom, HighlightEdge.Left, HighlightEdge.Right],
            null),
        new(HighlightArea.Left, "Left", [HighlightEdge.Left], null),
        new(HighlightArea.Right, "Right", [HighlightEdge.Right], null),
        new(HighlightArea.Top, "Top", [HighlightEdge.Top], null),
        new(HighlightArea.Bottom, "Bottom", [HighlightEdge.Bottom], null),
        new(HighlightArea.TopLeft, "Top-left corner", [], HighlightCorner.TopLeft),
        new(HighlightArea.TopRight, "Top-right corner", [], HighlightCorner.TopRight),
        new(HighlightArea.BottomLeft, "Bottom-left corner", [], HighlightCorner.BottomLeft),
        new(HighlightArea.BottomRight, "Bottom-right corner", [], HighlightCorner.BottomRight),
    ];

    private static readonly Dictionary<HighlightArea, HighlightAreaDefinition> DefinitionByArea =
        Definitions.ToDictionary(definition => definition.Area);

    public static IReadOnlyList<HighlightAreaDefinition> All => Definitions;

    public static bool IsSupported(HighlightArea area) => DefinitionByArea.ContainsKey(area);

    public static HighlightAreaDefinition Get(HighlightArea area) =>
        DefinitionByArea.TryGetValue(area, out var definition)
            ? definition
            : DefinitionByArea[HighlightArea.FullScreen];
}

internal readonly record struct HighlightOptions(HighlightArea Area, Color Color);
