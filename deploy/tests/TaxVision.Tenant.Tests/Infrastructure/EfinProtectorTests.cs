using System.Security.Cryptography;
using TaxVision.Tenant.Domain.ValueObjects;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Infrastructure;

public sealed class EfinProtectorTests
{
    [Fact]
    public void Round_trips_and_keeps_only_the_last_two_digits_in_clear()
    {
        var protector = TestEfinProtectors.Create(out _);

        var encrypted = protector.Protect(Efin.Create("123456").Value);

        Assert.Equal("56", encrypted.LastDigits);
        Assert.Equal(1, encrypted.KeyVersion);
        Assert.Equal("123456", protector.Reveal(encrypted));
    }

    [Fact]
    public void Encrypting_the_same_efin_twice_gives_different_bytes()
    {
        var protector = TestEfinProtectors.Create(out _);
        var efin = Efin.Create("123456").Value;

        Assert.NotEqual(protector.Protect(efin).Ciphertext, protector.Protect(efin).Ciphertext);
    }

    [Fact]
    public void After_rotating_the_key_an_efin_saved_with_the_previous_one_still_opens()
    {
        var oldProtector = TestEfinProtectors.Create(out var oldKey);
        var saved = oldProtector.Protect(Efin.Create("123456").Value);
        var newKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var rotated = TestEfinProtectors.Create(newKey, keyVersion: 2, previousKey: oldKey, previousVersion: 1);

        Assert.Equal("123456", rotated.Reveal(saved));
        Assert.Equal(2, rotated.Protect(Efin.Create("123456").Value).KeyVersion);
    }

    [Fact]
    public void A_different_key_cannot_open_it()
    {
        var saved = TestEfinProtectors.Create(out _).Protect(Efin.Create("123456").Value);
        var stranger = TestEfinProtectors.Create(out _);

        Assert.ThrowsAny<CryptographicException>(() => stranger.Reveal(saved));
    }
}
