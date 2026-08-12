using BuildingBlocks.Results;
using TaxVision.Sms.Application.Providers;

namespace TaxVision.Sms.Tests.Fakes;

/// <summary>Test double de <see cref="ISmsProvider"/> — capabilities y comportamiento configurables.
/// No hace nada real; cuenta invocaciones y devuelve lo que el test le fije.</summary>
internal sealed class FakeSmsProvider : ISmsProvider
{
    public string Code { get; init; } = "fake";
    public SmsProviderCapabilities Capabilities { get; set; } = FullCapabilities();

    public int SendAsyncCallCount { get; private set; }

    /// <summary>Comportamiento del envío. Por defecto acepta con un providerMessageId sintético.</summary>
    public Func<SmsSendRequest, SmsSendResult> SendImpl { get; set; } =
        _ => new SmsSendResult(true, "prov-" + Guid.NewGuid().ToString("N"), null, null);

    public bool SignatureValid { get; set; } = true;
    public SmsDeliveryUpdate? DeliveryUpdate { get; set; }
    public SmsInboundMessage? InboundMessage { get; set; }

    public Task<Result<SmsSendResult>> SendAsync(SmsSendRequest request, CancellationToken ct = default)
    {
        SendAsyncCallCount++;
        return Task.FromResult(Result.Success(SendImpl(request)));
    }

    public Task<Result<IReadOnlyList<SmsSendResult>>> SendBatchAsync(
        IReadOnlyList<SmsSendRequest> requests,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Result<SmsSignatureCheck> VerifySignature(
        string rawPayload,
        string signatureHeader,
        string secret,
        string requestUrl = ""
    ) => Result.Success(new SmsSignatureCheck(SignatureValid, SignatureValid ? null : "invalid"));

    public Result<SmsDeliveryUpdate> ParseDeliveryReceipt(string rawPayload) =>
        DeliveryUpdate is null
            ? Result.Failure<SmsDeliveryUpdate>(new Error("fake.noDlr", "No DLR configured."))
            : Result.Success(DeliveryUpdate);

    public Result<SmsInboundMessage> ParseInbound(string rawPayload) =>
        InboundMessage is null
            ? Result.Failure<SmsInboundMessage>(new Error("fake.noInbound", "No inbound configured."))
            : Result.Success(InboundMessage);

    public static SmsProviderCapabilities FullCapabilities() =>
        new()
        {
            SupportsDeliveryReceipts = true,
            SupportsInbound = true,
            SupportsBulkSend = false,
            MaxBatchSize = 100,
            SupportsMedia = true,
            SupportsMultipleMedia = true,
            MaxMediaItems = 10,
            MaxMediaSizeBytes = 5_000_000,
            AllowedMediaTypes = new HashSet<string>(StringComparer.Ordinal),
        };
}

internal sealed class FakeSmsAdapterFactory(ISmsProvider provider) : ISmsAdapterFactory
{
    public ISmsProvider Resolve(string code) => provider;
}
