using BuildingBlocks.Domain;

namespace TaxVision.CloudStorage.Domain.FileTypes;

/// <summary>Quién cambió el catálogo de tipos o la política de una carpeta, y cómo quedó (antes/después).</summary>
public sealed class FilePolicyAudit : BaseEntity
{
    public const int MaxSnapshotLength = 2000;

    private FilePolicyAudit() { }

    /// <summary><c>file_type</c> o <c>folder_policy</c>.</summary>
    public string Kind { get; private set; } = default!;

    /// <summary>Extensión o FolderType afectado.</summary>
    public string Key { get; private set; } = default!;

    public string? Before { get; private set; }
    public string After { get; private set; } = default!;
    public Guid ActorUserId { get; private set; }
    public DateTime AtUtc { get; private set; }

    public static FilePolicyAudit Record(string kind, string key, string? before, string after, Guid actorUserId, DateTime nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Key = key,
            Before = Truncate(before),
            After = Truncate(after)!,
            ActorUserId = actorUserId,
            AtUtc = nowUtc,
        };

    private static string? Truncate(string? value) =>
        value is { Length: > MaxSnapshotLength } ? value[..MaxSnapshotLength] : value;
}
