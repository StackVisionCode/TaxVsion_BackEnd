using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("Contacts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(Contact.MaxNameLength);
        builder.Property(c => c.Email).HasMaxLength(Contact.MaxEmailLength);
        builder.Property(c => c.PhoneE164).HasMaxLength(Contact.MaxPhoneLength);
        builder.Property(c => c.Source).HasConversion<int>().IsRequired();
        builder.Property(c => c.CustomerRef);
        builder.Property(c => c.OptedOutChannels).HasConversion<int>().IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();

        // Dedupe por destino: único por tenant sobre email / teléfono, pero solo cuando existen (índices
        // filtrados — muchos contactos tienen solo uno de los dos). El destino se guarda normalizado.
        builder
            .HasIndex(c => new { c.TenantId, c.Email })
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL")
            .HasDatabaseName("UX_Contacts_TenantId_Email");
        builder
            .HasIndex(c => new { c.TenantId, c.PhoneE164 })
            .IsUnique()
            .HasFilter("[PhoneE164] IS NOT NULL")
            .HasDatabaseName("UX_Contacts_TenantId_PhoneE164");
        builder.HasIndex(c => new { c.TenantId, c.CreatedAtUtc }).HasDatabaseName("IX_Contacts_TenantId_CreatedAtUtc");
    }
}

public sealed class ContactListConfiguration : IEntityTypeConfiguration<ContactList>
{
    public void Configure(EntityTypeBuilder<ContactList> builder)
    {
        builder.ToTable("ContactLists");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(ContactList.MaxNameLength).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(ContactList.MaxDescriptionLength);
        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.UpdatedAtUtc).IsRequired();

        builder
            .HasMany(l => l.Members)
            .WithOne()
            .HasForeignKey(m => m.ContactListId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(ContactList.Members))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(l => new { l.TenantId, l.CreatedAtUtc }).HasDatabaseName("IX_ContactLists_TenantId_CreatedAtUtc");
    }
}

public sealed class ContactListMemberConfiguration : IEntityTypeConfiguration<ContactListMember>
{
    public void Configure(EntityTypeBuilder<ContactListMember> builder)
    {
        builder.ToTable("ContactListMembers");
        builder.HasKey(m => m.Id);
        // Id generado en dominio (guardrail 10): sin esto EF haría UPDATE en vez de INSERT al agregar hijos.
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.ContactListId).IsRequired();
        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.ContactId).IsRequired();
        builder.Property(m => m.AddedAtUtc).IsRequired();

        builder
            .HasIndex(m => new { m.ContactListId, m.ContactId })
            .IsUnique()
            .HasDatabaseName("UX_ContactListMembers_ContactListId_ContactId");
        builder.HasIndex(m => new { m.TenantId, m.ContactId }).HasDatabaseName("IX_ContactListMembers_TenantId_ContactId");
    }
}
