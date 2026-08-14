using BuildingBlocks.Results;
using TaxVision.Tasks.Application.ClientRequests;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Application.ClientRequests.Commands;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.ClientRequests;

public sealed class SubmitClientDocumentHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid OtherCustomerId = Guid.NewGuid();
    private static readonly Guid PreparerId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// La regla que sostiene todo el portal: el pedido de otro cliente responde «no existe». El
    /// <c>customerId</c> viene del token, así que llegar hasta aquí con uno ajeno significa que
    /// alguien cambió el id del pedido en la URL.
    /// </summary>
    [Fact]
    public async Task A_request_that_belongs_to_another_customer_is_not_found()
    {
        var request = NewRequest(CustomerId);
        var repository = new InMemoryClientRequestRepository(request);

        var result = await SubmitAsync(repository, OtherCustomerId, request.Id);

        Assert.Equal(ClientRequestErrors.NotYours, result.Error);
    }

    /// <summary>Y el 404 no distingue: el cliente no puede deducir que ese pedido existe.</summary>
    [Fact]
    public async Task The_error_code_does_not_reveal_that_the_request_exists()
    {
        var request = NewRequest(CustomerId);
        var repository = new InMemoryClientRequestRepository(request);

        var foreign = await SubmitAsync(repository, OtherCustomerId, request.Id);
        var missing = await SubmitAsync(repository, OtherCustomerId, Guid.NewGuid());

        Assert.Equal(missing.Error.Code, foreign.Error.Code);
    }

    [Fact]
    public async Task Its_own_request_accepts_the_document_and_moves_to_submitted()
    {
        var request = NewRequest(CustomerId);
        var repository = new InMemoryClientRequestRepository(request);

        var result = await SubmitAsync(repository, CustomerId, request.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClientRequestStatus.Submitted, request.Status);
    }

    /// <summary>La respuesta del portal no lleva quién lo pidió: el id de un empleado no es del cliente.</summary>
    [Fact]
    public async Task The_portal_response_hides_who_asked()
    {
        var request = NewRequest(CustomerId);
        var repository = new InMemoryClientRequestRepository(request);

        var result = await SubmitAsync(repository, CustomerId, request.Id);

        var fields = result.Value.GetType().GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("RequestedByUserId", fields);
        Assert.DoesNotContain("ResolvedByUserId", fields);
    }

    private static async Task<Result<PortalClientRequestResponse>> SubmitAsync(
        IClientRequestRepository repository,
        Guid customerId,
        Guid requestId
    ) =>
        await SubmitClientDocumentHandler.Handle(
            new SubmitClientDocumentCommand(TenantId, customerId, requestId, Guid.NewGuid(), "w2.pdf", "application/pdf", 2048),
            repository,
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

    private static ClientRequest NewRequest(Guid customerId) =>
        ClientRequest.Create(TenantId, customerId, PreparerId, null, "W-2 2025", null, null, Now).Value;
}

internal sealed class InMemoryClientRequestRepository(params ClientRequest[] seed) : IClientRequestRepository
{
    private readonly List<ClientRequest> _requests = [.. seed];

    public void Add(ClientRequest request) => _requests.Add(request);

    public Task<Result<ClientRequest>> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default)
    {
        var found = _requests.FirstOrDefault(r => r.TenantId == tenantId && r.Id == requestId);

        return Task.FromResult(
            found is null ? Result.Failure<ClientRequest>(ClientRequestErrors.NotFound) : Result.Success(found)
        );
    }

    public Task<IReadOnlyList<ClientRequest>> ListForCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool onlyOpen,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<ClientRequest>>(
            [.. _requests.Where(r => r.TenantId == tenantId && r.CustomerId == customerId && (!onlyOpen || r.IsOpen))]
        );

    public Task<IReadOnlyList<ClientRequest>> ListForTaskAsync(
        Guid tenantId,
        Guid taskId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<ClientRequest>>([.. _requests.Where(r => r.TaskId == taskId)]);

    public Task<ClientRequest?> GetByDocumentFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(_requests.FirstOrDefault(r => r.Documents.Any(d => d.FileId == fileId)));
}
