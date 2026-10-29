namespace TaxVision.DataCollection.Engine.Mapping;

public sealed record PublisherCapabilities(IReadOnlySet<string> Permissions, bool IsTenantAdmin)
{
    public static PublisherCapabilities None { get; } = new(new HashSet<string>(StringComparer.Ordinal), false);

    public bool Allows(IReadOnlyList<string> requiredPermissions, bool requiresTenantAdmin) =>
        (!requiresTenantAdmin || IsTenantAdmin) && requiredPermissions.All(Permissions.Contains);
}
