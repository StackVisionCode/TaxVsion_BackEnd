using TaxVision.Wallet.Domain.Pricing;

namespace TaxVision.Wallet.Domain.Reservations;

/// <summary>Unidades por canal de una reserva, tal como llegaron al reservar.</summary>
public sealed record ReservationChannelUnits(int Email, int Sms, int Push, int WhatsApp)
{
    public static ReservationChannelUnits None { get; } = new(0, 0, 0, 0);

    /// <summary>Recorta a int (las unidades de la reserva son int) y descarta negativos.</summary>
    public static ReservationChannelUnits From(long email, long sms, long push, long whatsApp) =>
        new(ToUnits(email), ToUnits(sms), ToUnits(push), ToUnits(whatsApp));

    /// <summary>Canales con unidades, en orden fijo.</summary>
    public IReadOnlyList<(PriceChannel Channel, int Units)> NonZero()
    {
        var channels = new List<(PriceChannel, int)>(4);
        if (Email > 0)
            channels.Add((PriceChannel.Email, Email));
        if (Sms > 0)
            channels.Add((PriceChannel.Sms, Sms));
        if (Push > 0)
            channels.Add((PriceChannel.Push, Push));
        if (WhatsApp > 0)
            channels.Add((PriceChannel.WhatsApp, WhatsApp));
        return channels;
    }

    private static int ToUnits(long units) => (int)Math.Clamp(units, 0, int.MaxValue);
}

/// <summary>Foto legible de quién y para qué se reservó; no cambia aunque el origen se renombre o se borre.</summary>
public sealed record ReservationTrace(string? Description, ReservationChannelUnits Units, Guid? RequestedByUserId)
{
    public static ReservationTrace Empty { get; } = new(null, ReservationChannelUnits.None, null);
}
