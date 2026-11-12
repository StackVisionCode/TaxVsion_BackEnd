using Microsoft.Extensions.Options;
using TaxVision.Growth.Infrastructure.Security;

namespace TaxVision.Growth.Tests.Security;

/// <summary>
/// Los códigos que genera el CRM (clave "referral-regenerate:…") son cortos y fáciles de dictar; los emitidos antes
/// siguen derivándose en el formato largo para que su hash guardado coincida con el texto mostrado.
/// </summary>
public sealed class ReferralCodeTokenGeneratorFormatTests
{
    private static HmacSha256ReferralCodeTokenGenerator CreateGenerator() =>
        new(Options.Create(new ReferralCodeTokenHashingOptions { Pepper = new string('p', 48) }));

    private static readonly Guid Program = Guid.Parse("a1a2a3a4-0000-0000-0000-000000000001");
    private static readonly Guid Owner = Guid.Parse("5093c0f4-b412-4a97-aff1-03a202859230");

    [Fact]
    public void A_regenerated_code_is_short_readable_and_deterministic()
    {
        var generator = CreateGenerator();
        var key = $"{HmacSha256ReferralCodeTokenGenerator.ShortFormatKeyPrefix}{Guid.NewGuid()}";

        var first = generator.Generate(Program, Owner, key).Value.Reveal();
        var second = generator.Generate(Program, Owner, key).Value.Reveal();

        Assert.Equal(first, second);
        Assert.Matches("^TVR-[0-9A-HJKMNP-TV-Z]{10}$", first);
        Assert.Equal(14, first.Length);
    }

    [Fact]
    public void Different_operations_give_different_short_codes()
    {
        var generator = CreateGenerator();
        var codes = Enumerable
            .Range(0, 200)
            .Select(_ =>
                generator
                    .Generate(Program, Owner, $"{HmacSha256ReferralCodeTokenGenerator.ShortFormatKeyPrefix}{Guid.NewGuid()}")
                    .Value.Reveal()
            )
            .ToHashSet();

        Assert.Equal(200, codes.Count);
    }

    [Fact]
    public void A_previously_issued_key_keeps_the_original_long_format()
    {
        var generator = CreateGenerator();

        var code = generator.Generate(Program, Owner, $"referral-code:v1:{Owner}:{Program}:{Owner}").Value.Reveal();

        Assert.Matches("^TVR-[0-9A-F]{32}$", code);
    }
}
