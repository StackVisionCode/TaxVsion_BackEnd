using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Domain.Settings;

/// <summary>
/// Techos efectivos de un tenant: lo que vende su plan (entitlements), con la excepción que PlatformAdmin
/// le haya puesto, dentro de los techos técnicos del código. El dominio aplica la regla; el número llega de
/// afuera. Los valores de la oficina (tamaño, páginas, horas por defecto) se eligen dentro de estos techos.
/// </summary>
public sealed record SignatureLimits
{
    // Techos técnicos: ni un plan ni una excepción pueden pasarlos.
    public const int AbsoluteMaxDocumentsPerRequest = 100;
    public const int AbsoluteMaxSignersPerRequest = 200;
    public const int MinTokenHours = 1;
    public const int AbsoluteMaxTokenHours = 8760; // 1 año

    public int MaxDocumentsPerRequest { get; private init; }
    public int MaxSignersPerRequest { get; private init; }
    public long MaxPdfBytes { get; private init; }
    public int MaxPagesPerDocument { get; private init; }
    public int MaxTokenHours { get; private init; }
    public int MinRetentionYears { get; private init; }
    public bool PurgeAllowed { get; private init; }

    /// <summary>Canales de envío del código (Email, SMS, WhatsApp, KBA) que el tenant puede usar.</summary>
    public VerificationChannel AllowedChannels { get; private init; }

    private SignatureLimits() { }

    /// <summary>Acota cada valor a los techos técnicos (un entitlement mal cargado no rompe el servicio).</summary>
    public static SignatureLimits Create(
        int maxDocumentsPerRequest,
        int maxSignersPerRequest,
        long maxPdfBytes,
        int maxPagesPerDocument,
        int maxTokenHours,
        int minRetentionYears,
        bool purgeAllowed,
        VerificationChannel allowedChannels
    ) =>
        new()
        {
            MaxDocumentsPerRequest = Math.Clamp(maxDocumentsPerRequest, 1, AbsoluteMaxDocumentsPerRequest),
            MaxSignersPerRequest = Math.Clamp(maxSignersPerRequest, 1, AbsoluteMaxSignersPerRequest),
            MaxPdfBytes = Math.Clamp(maxPdfBytes, 1024, DocumentLimits.AbsoluteMaxPdfBytes),
            MaxPagesPerDocument = Math.Clamp(maxPagesPerDocument, 1, DocumentLimits.AbsoluteMaxPages),
            MaxTokenHours = Math.Clamp(maxTokenHours, MinTokenHours, AbsoluteMaxTokenHours),
            MinRetentionYears = Math.Clamp(
                minRetentionYears,
                RetentionPolicy.MinRetentionYears,
                RetentionPolicy.MaxRetentionYears
            ),
            PurgeAllowed = purgeAllowed,
            AllowedChannels = allowedChannels == VerificationChannel.None ? VerificationChannel.Email : allowedChannels,
        };

    public Result CheckTokenHours(int hours) =>
        hours >= MinTokenHours && hours <= MaxTokenHours
            ? Result.Success()
            : Result.Failure(
                new Error(
                    "Signature.Request.TokenExpiration",
                    $"Token expiration must be between {MinTokenHours} and {MaxTokenHours} hours."
                )
            );

    public Result CheckDocumentCount(int count) =>
        count >= 1 && count <= MaxDocumentsPerRequest
            ? Result.Success()
            : Result.Failure(
                new Error(
                    "Signature.Request.TooManyDocuments",
                    $"Document count must be between 1 and {MaxDocumentsPerRequest}."
                )
            );

    public Result CheckSignerCount(int count) =>
        count <= MaxSignersPerRequest
            ? Result.Success()
            : Result.Failure(
                new Error("Signature.Request.TooManySigners", $"Signer count cannot exceed {MaxSignersPerRequest}.")
            );

    /// <summary>
    /// El método que pide la oficina para un firmante debe salir por un canal permitido. El PIN del preparador
    /// no es un canal de envío (lo pone el personal), así que no se filtra.
    /// </summary>
    public Result CheckVerificationMethod(SignerVerificationMethod? method) =>
        ChannelOf(method) is { } channel && !AllowedChannels.HasFlag(channel)
            ? Result.Failure(
                new Error(
                    "Signature.Signer.ChannelNotAllowed",
                    $"The {channel} verification channel is not enabled for this office."
                )
            )
            : Result.Success();

    public static VerificationChannel? ChannelOf(SignerVerificationMethod? method) =>
        method switch
        {
            SignerVerificationMethod.EmailOtp => VerificationChannel.Email,
            SignerVerificationMethod.SmsOtp => VerificationChannel.Sms,
            SignerVerificationMethod.WhatsAppOtp => VerificationChannel.WhatsApp,
            SignerVerificationMethod.KbaQuiz => VerificationChannel.KnowledgeBased,
            _ => null,
        };
}
