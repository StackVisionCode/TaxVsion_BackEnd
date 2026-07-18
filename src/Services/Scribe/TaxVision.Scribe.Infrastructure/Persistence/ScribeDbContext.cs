using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Projections;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Infrastructure.Persistence;

/// <summary>Contexto de Entity Framework Core responsable de la persistencia del dominio Scribe.</summary>
public sealed class ScribeDbContext(DbContextOptions<ScribeDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<EmailTemplateVersion> EmailTemplateVersions => Set<EmailTemplateVersion>();
    public DbSet<TemplateVariableDefinition> TemplateVariableDefinitions => Set<TemplateVariableDefinition>();
    public DbSet<EmailLayout> EmailLayouts => Set<EmailLayout>();
    public DbSet<EmailLayoutVersion> EmailLayoutVersions => Set<EmailLayoutVersion>();
    public DbSet<EventTemplateMapping> EventTemplateMappings => Set<EventTemplateMapping>();
    public DbSet<TenantLogoRef> TenantLogoRefs => Set<TenantLogoRef>();
    public DbSet<TenantLogoMissingNotification> TenantLogoMissingNotifications => Set<TenantLogoMissingNotification>();

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
