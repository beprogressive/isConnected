namespace IsConnected;

internal enum SpeedOverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

internal sealed record SpeedOverlayCornerDefinition(
    SpeedOverlayCorner Corner,
    string DisplayName);

internal static class SpeedOverlayCornerCatalog
{
    private static readonly SpeedOverlayCornerDefinition[] Definitions =
    [
        new(SpeedOverlayCorner.TopLeft, "Top-left corner"),
        new(SpeedOverlayCorner.TopRight, "Top-right corner"),
        new(SpeedOverlayCorner.BottomLeft, "Bottom-left corner"),
        new(SpeedOverlayCorner.BottomRight, "Bottom-right corner"),
    ];

    private static readonly Dictionary<SpeedOverlayCorner, SpeedOverlayCornerDefinition> DefinitionByCorner =
        Definitions.ToDictionary(definition => definition.Corner);

    public static IReadOnlyList<SpeedOverlayCornerDefinition> All => Definitions;

    public static bool IsSupported(SpeedOverlayCorner corner) => DefinitionByCorner.ContainsKey(corner);
}
