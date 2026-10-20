using Microsoft.Extensions.Options;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Grupos de consumers migrados cuyo canal in-app puede quedar servido por Communication
/// (handler Node legacy) o por Notification (puente nuevo). El flag espeja al de Communication
/// (<c>COMMUNICATION_INAPP_SOURCE_*</c>) para apagar la publicación de este lado cuando el
/// rollback deja al grupo en Communication.
/// </summary>
public enum InAppSourceGroup
{
    CustomerImport,
    Connectors,
    CloudStorageLegal,
    CloudStoragePolicy,
    Support,
    Signature,
}

public sealed class InAppRoutingOptions
{
    public const string SectionName = "Notifications:InApp";

    /// <summary>
    /// Default <c>Communication</c> para que el rollback (quitar el flag) deje al legacy Node
    /// como fuente única. Para encender el pipeline nuevo en producción se setea
    /// <c>Notifications__InApp__&lt;Grupo&gt;__Source=Notification</c>.
    /// </summary>
    public SourceSettings CustomerImport { get; set; } = new();
    public SourceSettings Connectors { get; set; } = new();
    public SourceSettings CloudStorageLegal { get; set; } = new();
    public SourceSettings CloudStoragePolicy { get; set; } = new();
    public SourceSettings Support { get; set; } = new();
    public SourceSettings Signature { get; set; } = new();

    public sealed class SourceSettings
    {
        public string Source { get; set; } = "Communication";
    }
}

/// <summary>
/// Decide si este servicio debe publicar in-app para un grupo o si Communication es la fuente.
/// </summary>
public interface IInAppSourceGate
{
    bool PublishesInApp(InAppSourceGroup group);
}

public sealed class InAppSourceGate(IOptions<InAppRoutingOptions> options) : IInAppSourceGate
{
    private readonly InAppRoutingOptions _options = options.Value;

    public bool PublishesInApp(InAppSourceGroup group) =>
        string.Equals(SettingsFor(group).Source, "Notification", StringComparison.OrdinalIgnoreCase);

    private InAppRoutingOptions.SourceSettings SettingsFor(InAppSourceGroup group) =>
        group switch
        {
            InAppSourceGroup.CustomerImport => _options.CustomerImport,
            InAppSourceGroup.Connectors => _options.Connectors,
            InAppSourceGroup.CloudStorageLegal => _options.CloudStorageLegal,
            InAppSourceGroup.CloudStoragePolicy => _options.CloudStoragePolicy,
            InAppSourceGroup.Support => _options.Support,
            InAppSourceGroup.Signature => _options.Signature,
            _ => new InAppRoutingOptions.SourceSettings(),
        };
}
