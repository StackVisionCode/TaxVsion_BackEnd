using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Política de contraseñas (03 §10): longitud por MFA (D-13), lista de bloqueo, contexto y NFKC.</summary>
public sealed class PasswordPolicyTests
{
    private static readonly PasswordContext NoMfa = new("ana.perez@example.com");

    [Theory]
    [InlineData("Password2024!!!")]
    [InlineData("!!Welcome123456")]
    [InlineData("qwertyuiop12345")]
    [InlineData("111111111111111")]
    [InlineData("123456789012345")]
    [InlineData("abcdefghijklmnop")]
    public void Common_or_trivial_passwords_are_rejected(string password)
    {
        Assert.Equal("User.Password", PasswordPolicy.Validate(password, NoMfa).Error.Code);
    }

    [Theory]
    [InlineData("acme-offices-rocks-2026")]
    [InlineData("MyAcmeTaxPassword")]
    [InlineData("taxoffix-is-great-99")]
    [InlineData("perez-family-garden")]
    public void Passwords_built_from_the_context_are_rejected(string password)
    {
        var context = NoMfa with { ContextWords = ["Acme Tax", "acme", "Ana", "Perez"] };

        Assert.Equal("User.Password", PasswordPolicy.Validate(password, context).Error.Code);
    }

    // D-13: sin MFA el mínimo es 15; con MFA sigue en 12.
    [Fact]
    public void Without_mfa_a_new_password_needs_15_characters()
    {
        const string fourteen = "violet-harbor7";

        Assert.Equal("User.Password", PasswordPolicy.Validate(fourteen, NoMfa).Error.Code);
        Assert.True(PasswordPolicy.Validate(fourteen, NoMfa with { MfaEnabled = true }).IsSuccess);
        Assert.True(PasswordPolicy.Validate("violet-harbor77", NoMfa).IsSuccess);
    }

    [Fact]
    public void Validation_uses_the_nfkc_form()
    {
        // Ancho completo: en NFKC queda "password1234567", que está bloqueada.
        const string fullWidth = "ｐａｓｓｗｏｒｄ1234567";

        Assert.Equal("User.Password", PasswordPolicy.Validate(fullWidth, NoMfa).Error.Code);
    }

    [Fact]
    public void The_hasher_normalizes_new_hashes_and_still_verifies_old_unnormalized_ones()
    {
        var hasher = new Pbkdf2PasswordHasher();
        const string composed = "café-violet-harbor";
        const string decomposed = "café-violet-harbor";

        var hash = hasher.Hash(decomposed);

        Assert.True(hasher.Verify(composed, hash));
        Assert.True(hasher.Verify(decomposed, hash));
        Assert.False(hasher.Verify("cafe-violet-harbor", hash));
    }

    // Transición: un hash viejo hecho sin normalizar sigue verificando con la misma entrada.
    [Fact]
    public void A_legacy_hash_of_the_unnormalized_text_still_verifies()
    {
        const string decomposed = "cafe\u0301-violet-harbor";
        var salt = new byte[16];
        var key = Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivation.Pbkdf2(
            decomposed,
            salt,
            Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivationPrf.HMACSHA256,
            100_000,
            32
        );
        var legacy = $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";

        Assert.True(new Pbkdf2PasswordHasher().Verify(decomposed, legacy));
    }

    [Fact]
    public void A_short_email_name_does_not_block_every_password_that_contains_its_letters()
    {
        // Como las palabras del contexto: solo cuenta una parte local de 4 o más letras.
        var result = PasswordPolicy.Validate("mango orchard by the river", new PasswordContext("m@x.com"));

        Assert.True(result.IsSuccess);
    }
}
