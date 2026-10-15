namespace BuildingBlocks.PurchaseIntentAmount;

/// <summary>SEC-M1.C — config compartida por los 3 servicios que firman o verifican amounts de
/// purchase intents (Subscription, Wallet, PaymentApp). La <see cref="Secret"/> debe ser el MISMO
/// valor en los 3; se rota introduciendo <see cref="PreviousSecret"/> con el valor anterior durante
/// una ventana (típicamente mayor que <see cref="ClockSkew"/>) y luego despejándolo.</summary>
public sealed class PurchaseIntentAmountSignatureOptions
{
    public const string SectionName = "PurchaseIntentAmountSignature";

    /// <summary>Secret HMAC-SHA256; mínimo 32 bytes recomendado (base64 de 32 bytes aleatorios).
    /// Las impls no imponen longitud mínima para no romper configs legadas, pero logean warning si
    /// es menos de 32 chars.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Opcional — secret anterior durante la ventana de rotación. El verifier lo prueba
    /// como fallback cuando la firma no matchea con <see cref="Secret"/>. El signer SIEMPRE firma
    /// con <see cref="Secret"/>, nunca con el previous (eso rompería la rotación).</summary>
    public string? PreviousSecret { get; set; }

    /// <summary>Ventana de tolerancia reloj entre firmante y verificador — un atacante que capture
    /// una firma vieja no puede reusarla pasado este tiempo.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Fase final del rollout: cuando TODOS los callers firman, se activa para fail-closed
    /// contra requests sin firma. Default <c>false</c> para que el deploy entre versiones no bloquee
    /// callers en-vuelo.</summary>
    public bool RejectUnsigned { get; set; } = false;
}
