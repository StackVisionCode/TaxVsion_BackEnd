using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.PublicBookings;
using Xunit;

namespace TaxVision.Calendar.Tests.Security;

/// <summary>La llave del visitante en los índices no se puede revertir con un diccionario de emails.</summary>
public sealed class HmacRequesterKeyHasherTests
{
    private static HmacRequesterKeyHasher Hasher(string? masterKey) =>
        new(Options.Create(new RotatingSecretProtectionOptions { MasterKey = masterKey ?? string.Empty }));

    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void The_same_email_gives_the_same_key_and_it_is_not_a_plain_hash()
    {
        var hasher = Hasher(Key);
        var email = WorkEmail.Create(" Jane@Acme-Tax.com ").Value;

        var key = hasher.KeyOf(email);

        Assert.Matches("^[0-9a-f]{64}$", key);
        Assert.Equal(key, hasher.KeyOf(WorkEmail.Create("jane@acme-tax.com").Value));
        Assert.NotEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email.Value))), key);
    }

    [Fact]
    public void Another_server_key_gives_another_requester_key()
    {
        var email = WorkEmail.Create("jane@acme-tax.com").Value;

        Assert.NotEqual(
            Hasher(Key).KeyOf(email),
            Hasher(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).KeyOf(email)
        );
    }

    [Fact]
    public void Without_a_server_key_it_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => Hasher(null).KeyOf(WorkEmail.Create("jane@acme-tax.com").Value));
    }
}
