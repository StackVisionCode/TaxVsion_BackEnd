using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Application.Common;
using TaxVision.Inventory.Domain;
using TaxVision.Inventory.Domain.Suppliers;
using TaxVision.Inventory.Domain.ValueObjects;

namespace TaxVision.Inventory.Application.Suppliers;

// ───────────────────────── Supplier commands ─────────────────────────

public sealed record CreateSupplierCommand(Guid TenantId, Guid UserId, string Name, string? ContactName, string? Email, string? Phone, string? Address, string? TaxId);

public sealed record UpdateSupplierCommand(Guid TenantId, Guid Id, string Name, string? ContactName, string? Email, string? Phone, string? Address, string? TaxId);

public sealed record SetSupplierActiveCommand(Guid TenantId, Guid Id, bool IsActive);

public sealed record DeleteSupplierCommand(Guid TenantId, Guid Id);

public static class CreateSupplierHandler
{
    public static async Task<Result<SupplierDto>> Handle(CreateSupplierCommand c, ISupplierRepository suppliers, IUnitOfWork uow, CancellationToken ct)
    {
        var created = Supplier.Create(c.TenantId, c.UserId, c.Name, c.ContactName, c.Email, c.Phone, c.Address, c.TaxId, DateTime.UtcNow);
        if (created.IsFailure)
            return Result.Failure<SupplierDto>(created.Error);
        await suppliers.AddAsync(created.Value, ct);
        await uow.SaveChangesAsync(ct);
        return Result.Success(created.Value.ToDto());
    }
}

public static class UpdateSupplierHandler
{
    public static async Task<Result<SupplierDto>> Handle(UpdateSupplierCommand c, ISupplierRepository suppliers, IUnitOfWork uow, CancellationToken ct)
    {
        var supplier = await suppliers.GetByIdAsync(c.TenantId, c.Id, ct);
        if (supplier is null)
            return Result.Failure<SupplierDto>(InventoryErrors.SupplierNotFound);
        var r = supplier.Update(c.Name, c.ContactName, c.Email, c.Phone, c.Address, c.TaxId, DateTime.UtcNow);
        if (r.IsFailure)
            return Result.Failure<SupplierDto>(r.Error);
        await uow.SaveChangesAsync(ct);
        return Result.Success(supplier.ToDto());
    }
}

public static class SetSupplierActiveHandler
{
    public static async Task<Result> Handle(SetSupplierActiveCommand c, ISupplierRepository suppliers, IUnitOfWork uow, CancellationToken ct)
    {
        var supplier = await suppliers.GetByIdAsync(c.TenantId, c.Id, ct);
        if (supplier is null)
            return Result.Failure(InventoryErrors.SupplierNotFound);
        supplier.SetActive(c.IsActive, DateTime.UtcNow);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class DeleteSupplierHandler
{
    public static async Task<Result> Handle(DeleteSupplierCommand c, ISupplierRepository suppliers, IUnitOfWork uow, CancellationToken ct)
    {
        var supplier = await suppliers.GetByIdAsync(c.TenantId, c.Id, ct);
        if (supplier is null)
            return Result.Failure(InventoryErrors.SupplierNotFound);
        supplier.SoftDelete(DateTime.UtcNow);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record GetSupplierQuery(Guid TenantId, Guid Id);

public sealed record ListSuppliersQuery(Guid TenantId, bool ActiveOnly);

public static class GetSupplierHandler
{
    public static async Task<Result<SupplierDto>> Handle(GetSupplierQuery q, ISupplierRepository suppliers, CancellationToken ct)
    {
        var supplier = await suppliers.GetByIdAsync(q.TenantId, q.Id, ct);
        return supplier is null ? Result.Failure<SupplierDto>(InventoryErrors.SupplierNotFound) : Result.Success(supplier.ToDto());
    }
}

public static class ListSuppliersHandler
{
    public static async Task<Result<IReadOnlyList<SupplierDto>>> Handle(ListSuppliersQuery q, ISupplierRepository suppliers, CancellationToken ct)
    {
        var rows = await suppliers.ListAsync(q.TenantId, q.ActiveOnly, ct);
        IReadOnlyList<SupplierDto> dtos = rows.Select(s => s.ToDto()).ToList();
        return Result.Success(dtos);
    }
}

// ───────────────────────── Item-Supplier link ─────────────────────────

public sealed record UpsertItemSupplierCommand(
    Guid TenantId,
    Guid CatalogItemId,
    Guid SupplierId,
    string? SupplierSku,
    decimal? PriceAmount,
    string? PriceCurrency,
    int? LeadTimeDays,
    bool IsPreferred
);

public sealed record DeleteItemSupplierCommand(Guid TenantId, Guid Id);

public sealed record ListItemSuppliersQuery(Guid TenantId, Guid CatalogItemId);

public static class UpsertItemSupplierHandler
{
    public static async Task<Result<ItemSupplierDto>> Handle(
        UpsertItemSupplierCommand c,
        IItemSupplierRepository links,
        ISupplierRepository suppliers,
        IUnitOfWork uow,
        CancellationToken ct
    )
    {
        if (await suppliers.GetByIdAsync(c.TenantId, c.SupplierId, ct) is null)
            return Result.Failure<ItemSupplierDto>(InventoryErrors.SupplierNotFound);

        Money? price = null;
        if (c.PriceAmount is { } amount)
        {
            var money = Money.Create(amount, c.PriceCurrency);
            if (money.IsFailure)
                return Result.Failure<ItemSupplierDto>(money.Error);
            price = money.Value;
        }

        var nowUtc = DateTime.UtcNow;
        var existing = await links.GetAsync(c.TenantId, c.CatalogItemId, c.SupplierId, ct);
        if (existing is not null)
        {
            existing.Update(c.SupplierSku, price, c.LeadTimeDays, c.IsPreferred, nowUtc);
            await uow.SaveChangesAsync(ct);
            return Result.Success(existing.ToDto());
        }

        var created = ItemSupplier.Create(c.TenantId, c.CatalogItemId, c.SupplierId, c.SupplierSku, price, c.LeadTimeDays, c.IsPreferred, nowUtc);
        if (created.IsFailure)
            return Result.Failure<ItemSupplierDto>(created.Error);
        await links.AddAsync(created.Value, ct);
        await uow.SaveChangesAsync(ct);
        return Result.Success(created.Value.ToDto());
    }
}

public static class DeleteItemSupplierHandler
{
    public static async Task<Result> Handle(DeleteItemSupplierCommand c, IItemSupplierRepository links, IUnitOfWork uow, CancellationToken ct)
    {
        var link = await links.GetByIdAsync(c.TenantId, c.Id, ct);
        if (link is null)
            return Result.Failure(InventoryErrors.ItemSupplierNotFound);
        links.Remove(link);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class ListItemSuppliersHandler
{
    public static async Task<Result<IReadOnlyList<ItemSupplierDto>>> Handle(ListItemSuppliersQuery q, IItemSupplierRepository links, CancellationToken ct)
    {
        var rows = await links.ListByItemAsync(q.TenantId, q.CatalogItemId, ct);
        IReadOnlyList<ItemSupplierDto> dtos = rows.Select(x => x.ToDto()).ToList();
        return Result.Success(dtos);
    }
}
