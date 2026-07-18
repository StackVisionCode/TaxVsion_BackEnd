using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

public sealed class IdempotencyKeyTests
{
    [Fact]
    public void Create_with_an_empty_value_fails()
    {
        var result = IdempotencyKey.Create("   ");

        Assert.True(result.IsFailure);
        Assert.Equal("IdempotencyKey.Empty", result.Error.Code);
    }

    [Fact]
    public void Create_with_a_value_longer_than_200_characters_fails()
    {
        var result = IdempotencyKey.Create(new string('a', 201));

        Assert.True(result.IsFailure);
        Assert.Equal("IdempotencyKey.TooLong", result.Error.Code);
    }

    [Fact]
    public void Two_keys_with_the_same_value_are_equal()
    {
        var a = IdempotencyKey.Create("renewal-123").Value;
        var b = IdempotencyKey.Create("renewal-123").Value;

        Assert.Equal(a, b);
    }
}
