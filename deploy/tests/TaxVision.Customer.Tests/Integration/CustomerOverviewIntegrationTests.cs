using System.Data.Common;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>Overview del dashboard en SQL Server: conteos por estado en un GROUP BY, con y sin recorte.</summary>
public sealed class CustomerOverviewIntegrationTests(CustomerTemporaryDatabase database)
    : IClassFixture<CustomerTemporaryDatabase>
{
    private static readonly Guid Admin = Guid.NewGuid();

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();

    [Fact]
    public async Task Overview_counts_by_status_in_one_grouped_query_for_both_scopes()
    {
        await SeedAsync(_tenant, _ => Result.Success());
        await SeedAsync(_tenant, c => c.Deactivate(Admin));
        await SeedAsync(_tenant, c => c.Archive(Admin));
        await SeedAsync(_tenant, c => c.AssignPreparer(_employee, Admin));
        await SeedAsync(Guid.NewGuid(), c => c.AssignPreparer(_employee, Admin));
        var commands = new CommandCapture();

        var office = await Reader(commands).GetOverviewAsync(_tenant, 12);
        var mine = await Reader(commands).GetOverviewAsync(_tenant, 12, assignedToUserId: _employee);

        Assert.Equal(new CustomerStatusCounts(2, 1, 1), office.StatusCounts);
        Assert.Equal(4, office.TotalCount);
        Assert.Equal(new CustomerStatusCounts(1, 0, 0), mine.StatusCounts);
        Assert.Equal(1, mine.TotalCount);
        // Un solo GROUP BY por llamada y ningún COUNT aparte.
        Assert.Equal(2, commands.Sql.Count(s => s.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(commands.Sql, s => s.Contains("GROUP BY") && s.Contains("CustomerAssignments"));
    }

    private CustomerReadService Reader(CommandCapture commands) =>
        new(database.NewContext(Guid.Empty, commands), new PlainProtector());

    private async Task SeedAsync(Guid tenantId, Func<DomainCustomer, Result> arrange)
    {
        var customer = DomainCustomer
            .Register(
                tenantId,
                CustomerKind.Individual,
                PersonalName.Create("Overview", "Client").Value,
                null,
                EmailAddress.Create($"overview-{Guid.NewGuid():N}@example.test").Value,
                null,
                Language.En,
                Admin
            )
            .Value;
        Assert.True(arrange(customer).IsSuccess);
        await using var db = database.NewContext(tenantId);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
    }

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PlainProtector : ISensitiveDataProtector
    {
        public byte[] Protect(string plainText) => System.Text.Encoding.UTF8.GetBytes(plainText);

        public string Unprotect(byte[] cipher) => System.Text.Encoding.UTF8.GetString(cipher);

        public string ComputeBlindIndex(string plainText, Guid tenantId) => plainText;

        public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) => [plainText];

        public short CurrentKeyVersion => 1;
    }
}
