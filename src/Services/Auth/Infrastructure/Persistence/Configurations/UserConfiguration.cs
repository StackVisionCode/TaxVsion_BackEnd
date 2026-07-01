using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.TenantId).IsRequired();
        builder.Property(user => user.Name).HasMaxLength(100).IsRequired();
        builder.Property(user => user.LastName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(320).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(user => user.ActorType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(user => user.IsActive).IsRequired();
        builder.HasIndex(user => new { user.TenantId, user.Email })
            .IsUnique();
        builder.HasIndex(user => new { user.TenantId, user.ActorType });
        builder.HasIndex(user => new { user.TenantId, user.CustomerId })
            .HasFilter("[CustomerId] IS NOT NULL");
        builder.Ignore(user => user.Roles);

        var converter = new ValueConverter<List<string>, string>(
            roles => JsonSerializer.Serialize(roles, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>());

        var comparer = new ValueComparer<List<string>>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            roles => roles.Aggregate(0, (hash, role) => HashCode.Combine(hash, role.GetHashCode())),
            roles => roles.ToList());

        var rolesProperty = builder.Property<List<string>>("_roles")
            .HasColumnName("Roles")
            .HasColumnType("nvarchar(max)")
            .HasConversion(converter);

        rolesProperty.Metadata.SetValueComparer(comparer);
    }
}
