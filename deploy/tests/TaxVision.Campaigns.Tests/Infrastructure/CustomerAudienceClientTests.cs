using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Campaigns.Infrastructure.Customers;

namespace TaxVision.Campaigns.Tests.Infrastructure;

public sealed class CustomerAudienceClientTests
{
    private sealed class FixedToken(string? token) : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    /// <summary>Páginas 1-2 con 200 clientes cada una (de 1000 en total); la página 3 responde 500.</summary>
    private sealed class PagedCustomers : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var page = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["page"]!);
            if (page >= 3)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var items = Enumerable
                .Range(0, 200)
                .Select(i => new
                {
                    Id = Guid.NewGuid(),
                    DisplayName = $"C{page}-{i}",
                    PrimaryEmail = $"c{page}-{i}@x.com",
                    PrimaryPhone = (string?)null,
                })
                .ToList();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(
                        new
                        {
                            Items = items,
                            Page = page,
                            Size = 200,
                            TotalCount = 1000,
                        }
                    ),
                }
            );
        }
    }

    private static CustomerAudienceClient Client(string? token = "token") =>
        new(
            new HttpClient(new PagedCustomers()) { BaseAddress = new Uri("http://customer/") },
            new FixedToken(token),
            NullLogger<CustomerAudienceClient>.Instance
        );

    [Fact]
    public async Task GetActiveCustomers_Page3Returns500_ReportsIncomplete()
    {
        var result = await Client().GetActiveCustomersAsync(Guid.NewGuid());

        Assert.False(result.IsComplete);
        Assert.Equal(400, result.Members.Count);
        Assert.NotNull(result.IncompleteReason);
    }

    [Fact]
    public async Task GetActiveCustomers_NoServiceToken_ReportsIncomplete()
    {
        var result = await Client(token: null).GetActiveCustomersAsync(Guid.NewGuid());

        Assert.False(result.IsComplete);
        Assert.Empty(result.Members);
    }
}
