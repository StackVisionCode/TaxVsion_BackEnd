namespace BuildingBlocks.PurchaseIntentAmount;

/// <summary>SEC-M1.C — produce un token de firma opaco con el formato
/// <c>v1.{base64hmac}.{signedAtUnix}</c>. El caller M2M (Subscription/Wallet) lo incluye en el body
/// de su request a PaymentApp; PaymentApp lo verifica con <see cref="IPurchaseIntentAmountVerifier"/>
/// antes de abrir el checkout. Fail-closed verificable con la misma secret, sin round-trip.</summary>
public interface IPurchaseIntentAmountSigner
{
    string Sign(PurchaseIntentAmountInput input);
}
