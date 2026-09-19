using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Senders;

/// <summary>Errores de dominio centralizados del aggregate <see cref="SenderProfile"/>.</summary>
public static class SenderProfileErrors
{
    public static readonly Error TenantRequired = new("SenderProfile.Tenant", "TenantId is required.");
    public static readonly Error ChannelInvalid = new(
        "SenderProfile.Channel",
        "A sender profile must target exactly one channel."
    );
    public static readonly Error NameRequired = new("SenderProfile.Name", "Name is required.");
    public static readonly Error NameTooLong = new("SenderProfile.NameTooLong", "Name exceeds the maximum length.");
    public static readonly Error SenderRefRequired = new("SenderProfile.SenderRef", "SenderRef is required.");
    public static readonly Error SenderRefTooLong = new(
        "SenderProfile.SenderRefTooLong",
        "SenderRef exceeds the maximum length."
    );
    public static readonly Error NotFound = new("SenderProfile.NotFound", "Sender profile not found.");
    public static readonly Error NotActive = new("SenderProfile.NotActive", "The sender profile is disabled.");
    public static readonly Error ChannelMismatch = new(
        "SenderProfile.ChannelMismatch",
        "The sender profile's channel is not one of the campaign's channels."
    );
}
