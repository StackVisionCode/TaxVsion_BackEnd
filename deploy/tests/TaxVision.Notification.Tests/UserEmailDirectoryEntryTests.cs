using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Tests;

/// <summary>Idioma, zona y nombre en el directorio: la foto más reciente gana.</summary>
public sealed class UserEmailDirectoryEntryTests
{
    [Fact]
    public void ApplyProfile_ignores_older_events()
    {
        var entry = UserEmailDirectoryEntry.Create(Guid.NewGuid(), Guid.NewGuid(), "user@example.com");
        var now = DateTime.UtcNow;

        Assert.True(entry.ApplyProfile("es", "America/Chicago", "Ana López", now));
        Assert.False(entry.ApplyProfile("en", null, "Old Name", now.AddMinutes(-5)));

        Assert.Equal("es", entry.Language);
        Assert.Equal("America/Chicago", entry.TimeZoneId);
        Assert.Equal("Ana López", entry.DisplayName);
    }

    [Fact]
    public void ApplyProfile_keeps_only_supported_languages()
    {
        var entry = UserEmailDirectoryEntry.Create(Guid.NewGuid(), Guid.NewGuid(), "user@example.com");

        entry.ApplyProfile("ES", " ", null, DateTime.UtcNow);
        Assert.Equal("es", entry.Language);
        Assert.Null(entry.TimeZoneId);

        entry.ApplyProfile("pt", null, null, DateTime.UtcNow.AddMinutes(1));
        Assert.Null(entry.Language);
    }
}
