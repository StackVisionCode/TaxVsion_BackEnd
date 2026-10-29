namespace TaxVision.Postmaster.Infrastructure.DevMailbox;

// Buzón local de desarrollo: copia cada correo saliente a disco; solo se honra en Development.
public sealed class DevMailboxOptions
{
    public const string SectionName = "Postmaster:DevMailbox";

    public bool Enabled { get; set; }

    // Relativa al content root (la carpeta del proyecto Api): apunta a TaxVsion_BackEnd/logs/dev-mailbox.
    public string Directory { get; set; } = "../../../../logs/dev-mailbox";
}
