using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Queries.SearchCustomerImports;
using TaxVision.Customer.Domain.Catalogs;
using TaxVision.Customer.Infrastructure.Imports;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// El import usa el mismo catálogo que ve la pantalla (sin ocupaciones desactivadas) y el historial no se rompe
/// con una página o un tamaño fuera de rango.
/// </summary>
public sealed class CustomerImportCatalogAndHistoryTests
{
    [Fact]
    public async Task A_deactivated_occupation_is_not_accepted_by_the_import()
    {
        await using var db = CreateContext();
        var active = Occupation.Create(Guid.NewGuid(), "Engineer").Value;
        var inactive = Occupation.Create(Guid.NewGuid(), "Archtect").Value;
        inactive.Deactivate();
        db.Occupations.AddRange(active, inactive);
        await db.SaveChangesAsync();
        var resolver = new SqlServerCatalogResolver(db);

        Assert.Equal(active.Id, await resolver.ResolveOccupationIdAsync(" engineer ", default));
        Assert.Null(await resolver.ResolveOccupationIdAsync("Archtect", default));
    }

    [Theory]
    [InlineData(0, 20, 1, 20)]
    [InlineData(-3, 20, 1, 20)]
    [InlineData(2, 0, 2, 1)]
    [InlineData(1, 100_000, 1, 100)]
    public async Task The_import_history_keeps_page_and_size_in_range(
        int page,
        int size,
        int expectedPage,
        int expectedSize
    )
    {
        var reader = new CapturingReader();

        await SearchCustomerImportsHandler.Handle(
            new SearchCustomerImportsQuery(Guid.NewGuid(), page, size),
            reader,
            default
        );

        Assert.Equal((expectedPage, expectedSize), (reader.Page, reader.Size));
    }

    private sealed class CapturingReader : ICustomerImportReadService
    {
        public int Page { get; private set; }
        public int Size { get; private set; }

        public Task<CustomerImportAttemptResponse?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<CustomerImportAttemptResponse?>(null);

        public Task<IReadOnlyList<CustomerImportAttemptResponse>> SearchAsync(
            Guid tenantId,
            int page,
            int size,
            CancellationToken ct
        )
        {
            (Page, Size) = (page, size);
            return Task.FromResult<IReadOnlyList<CustomerImportAttemptResponse>>([]);
        }

        public IAsyncEnumerable<CustomerImportRowResponse> StreamRowsAsync(Guid importId, CancellationToken ct) =>
            AsyncEnumerable.Empty<CustomerImportRowResponse>();
    }

    private static CustomerDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<CustomerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeTenantContext()
        );

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
