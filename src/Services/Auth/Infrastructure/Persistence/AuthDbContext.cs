using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(
                "Persistence.UniqueConstraint",
                "A record with the same unique values already exists.",
                ex
            );
        }
    }
}
