namespace BuildingBlocks.Authorization.SlotGating;

public sealed class SlotGatingOptions
{
    public const string SectionName = "Seats:SlotGating";

    public SlotGatingMode Mode { get; init; } = SlotGatingMode.Disabled;
    public bool EmitNotifications { get; init; }
}

public enum SlotGatingMode
{
    Disabled,
    Shadow,
    Enforce,
}
