using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Tests.Messages;

internal sealed class FakeConnectorsClient : IConnectorsClient
{
    public Result<MessageBodyResponse> Response { get; set; } =
        Result.Success(new MessageBodyResponse("<p>hi</p>", "hi", new Dictionary<string, string>()));

    public Result<ConnectorsAttachmentBytes> AttachmentResponse { get; set; } =
        Result.Success(new ConnectorsAttachmentBytes([1, 2, 3, 4]));

    public List<(Guid TenantId, Guid AccountId, string ProviderMessageId)> Calls { get; } = [];

    public List<(
        Guid TenantId,
        Guid AccountId,
        string ProviderMessageId,
        string ProviderAttachmentId
    )> AttachmentCalls { get; } = [];

    public Task<Result<MessageBodyResponse>> FetchMessageBodyAsync(
        Guid tenantId,
        Guid accountId,
        string providerMessageId,
        CancellationToken ct = default
    )
    {
        Calls.Add((tenantId, accountId, providerMessageId));
        return Task.FromResult(Response);
    }

    public Task<Result<ConnectorsAttachmentBytes>> FetchAttachmentAsync(
        Guid tenantId,
        Guid accountId,
        string providerMessageId,
        string providerAttachmentId,
        CancellationToken ct = default
    )
    {
        AttachmentCalls.Add((tenantId, accountId, providerMessageId, providerAttachmentId));
        return Task.FromResult(AttachmentResponse);
    }
}
