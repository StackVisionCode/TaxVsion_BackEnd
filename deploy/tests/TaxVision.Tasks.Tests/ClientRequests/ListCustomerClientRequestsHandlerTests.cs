using TaxVision.Tasks.Application.ClientRequests;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Application.ClientRequests.Queries;
using TaxVision.Tasks.Domain.ClientRequests;

namespace TaxVision.Tasks.Tests.ClientRequests;

/// <summary>
/// La contraparte de staff del listado del portal: en el perfil del CRM el preparador ve TODO lo que
/// se le pidió a un cliente, con el cliente explícito en la query (el portal lo deriva del token).
/// </summary>
public sealed class ListCustomerClientRequestsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid OtherCustomerId = Guid.NewGuid();
    private static readonly Guid PreparerId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Only_this_customers_requests_are_returned()
    {
        var repository = new InMemoryClientRequestRepository(
            NewRequest(CustomerId, "W-2 2025"),
            NewRequest(CustomerId, "1099 2025"),
            NewRequest(OtherCustomerId, "Not yours")
        );

        var result = await ListAsync(repository, CustomerId, onlyOpen: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, r => Assert.Equal(CustomerId, r.CustomerId));
    }

    [Fact]
    public async Task OnlyOpen_excludes_closed_requests()
    {
        var open = NewRequest(CustomerId, "Still pending");
        var cancelled = NewRequest(CustomerId, "Withdrawn");
        cancelled.Cancel(PreparerId, "No longer needed", Now);
        var repository = new InMemoryClientRequestRepository(open, cancelled);

        var openOnly = await ListAsync(repository, CustomerId, onlyOpen: true);
        var all = await ListAsync(repository, CustomerId, onlyOpen: false);

        Assert.Single(openOnly.Value);
        Assert.Equal(open.Id, openOnly.Value[0].Id);
        Assert.Equal(2, all.Value.Count);
    }

    /// <summary>Al revés que el portal: el staff SÍ ve quién lo pidió (es la vista del preparador).</summary>
    [Fact]
    public async Task The_staff_response_includes_who_asked()
    {
        var repository = new InMemoryClientRequestRepository(NewRequest(CustomerId, "W-2 2025"));

        var result = await ListAsync(repository, CustomerId, onlyOpen: false);

        var fields = result.Value[0].GetType().GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("RequestedByUserId", fields);
    }

    [Fact]
    public async Task A_customer_with_no_requests_returns_an_empty_list()
    {
        var repository = new InMemoryClientRequestRepository(NewRequest(OtherCustomerId, "Someone else's"));

        var result = await ListAsync(repository, CustomerId, onlyOpen: false);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    private static Task<BuildingBlocks.Results.Result<IReadOnlyList<ClientRequestResponse>>> ListAsync(
        IClientRequestRepository repository,
        Guid customerId,
        bool onlyOpen
    ) =>
        ListCustomerClientRequestsHandler.Handle(
            new ListCustomerClientRequestsQuery(TenantId, customerId, onlyOpen),
            repository,
            CancellationToken.None
        );

    private static ClientRequest NewRequest(Guid customerId, string title) =>
        ClientRequest.Create(TenantId, customerId, PreparerId, null, title, null, null, Now).Value;
}
