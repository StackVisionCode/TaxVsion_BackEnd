using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Lo que el cliente compra de una edición: precio base por ciclo, asientos, almacenamiento e invitaciones. Dos
/// ediciones con los mismos términos son intercambiables para quien ya paga (re-anclaje R1); los módulos y las
/// claves de plataforma no cuentan acá.
/// </summary>
public sealed class CommercialTerms : IEquatable<CommercialTerms>
{
    public const string SeatsMaxKey = "seats.max";
    public const string StorageMaxBytesKey = "storage.max_bytes";
    public const string InvitationsMaxPendingKey = "invitations.max_pending";

    private CommercialTerms(
        IReadOnlyDictionary<BillingCycle, Money> prices,
        int? seatsMax,
        long? storageMaxBytes,
        int? invitationsMaxPending
    )
    {
        Prices = prices;
        SeatsMax = seatsMax;
        StorageMaxBytes = storageMaxBytes;
        InvitationsMaxPending = invitationsMaxPending;
    }

    public IReadOnlyDictionary<BillingCycle, Money> Prices { get; }
    public int? SeatsMax { get; }
    public long? StorageMaxBytes { get; }
    public int? InvitationsMaxPending { get; }

    public static CommercialTerms Of(SubscriptionPlanVersion version)
    {
        var prices = version
            .PriceTiers.Where(tier => tier.MinQuantity <= 1 && (tier.MaxQuantity is null || tier.MaxQuantity >= 1))
            .GroupBy(tier => tier.BillingCycle)
            .ToDictionary(group => group.Key, group => group.First().UnitAmount);

        return new CommercialTerms(
            prices,
            ReadInt(version, SeatsMaxKey),
            ReadLong(version, StorageMaxBytesKey),
            ReadInt(version, InvitationsMaxPendingKey)
        );
    }

    public CommercialTerms WithPrices(Money monthly, Money yearly) =>
        new(
            new Dictionary<BillingCycle, Money> { [BillingCycle.Monthly] = monthly, [BillingCycle.Yearly] = yearly },
            SeatsMax,
            StorageMaxBytes,
            InvitationsMaxPending
        );

    public CommercialTerms WithSeatsMax(int seatsMax) => new(Prices, seatsMax, StorageMaxBytes, InvitationsMaxPending);

    public CommercialTerms WithStorageMaxBytes(long storageMaxBytes) =>
        new(Prices, SeatsMax, storageMaxBytes, InvitationsMaxPending);

    public bool Equals(CommercialTerms? other) =>
        other is not null
        && SeatsMax == other.SeatsMax
        && StorageMaxBytes == other.StorageMaxBytes
        && InvitationsMaxPending == other.InvitationsMaxPending
        && Prices.Count == other.Prices.Count
        && Prices.All(price => other.Prices.TryGetValue(price.Key, out var same) && same == price.Value);

    public override bool Equals(object? obj) => Equals(obj as CommercialTerms);

    public override int GetHashCode() =>
        HashCode.Combine(SeatsMax, StorageMaxBytes, InvitationsMaxPending, Prices.Count);

    public static bool operator ==(CommercialTerms? left, CommercialTerms? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(CommercialTerms? left, CommercialTerms? right) => !(left == right);

    private static int? ReadInt(SubscriptionPlanVersion version, string key) =>
        int.TryParse(Find(version, key), out var value) ? value : null;

    private static long? ReadLong(SubscriptionPlanVersion version, string key) =>
        long.TryParse(Find(version, key), out var value) ? value : null;

    private static string? Find(SubscriptionPlanVersion version, string key) =>
        version.Entitlements.FirstOrDefault(entitlement => entitlement.Key.Value == key)?.DefaultValue;

    internal static EntitlementValueType TypeOf(string key) =>
        key == StorageMaxBytesKey ? EntitlementValueType.Long : EntitlementValueType.Int;
}
