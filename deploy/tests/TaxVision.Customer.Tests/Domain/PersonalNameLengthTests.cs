using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Relations;
using TaxVision.Customer.Infrastructure.Persistence;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Domain;

/// <summary>
/// B-14: segundo nombre, prefijo y sufijo se limitan en el dominio con los mismos topes que la base, para
/// que el exceso sea un 400 tipado y no un error de SQL al guardar.
/// </summary>
public sealed class PersonalNameLengthTests
{
    [Theory]
    [InlineData("middle", 80)]
    [InlineData("prefix", 20)]
    [InlineData("suffix", 20)]
    public void A_name_part_at_its_limit_is_accepted(string part, int limit)
    {
        var result = Create(part, new string('a', limit));

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("middle", 80)]
    [InlineData("prefix", 20)]
    [InlineData("suffix", 20)]
    public void A_name_part_over_its_limit_is_rejected_with_a_typed_error(string part, int limit)
    {
        var result = Create(part, new string('a', limit + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("PersonalName.Length", result.Error.Code);
        Assert.Contains(limit.ToString(), result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("middle", 80)]
    [InlineData("prefix", 20)]
    [InlineData("suffix", 20)]
    public void Surrounding_whitespace_does_not_count_against_the_limit(string part, int limit)
    {
        var result = Create(part, $"  {new string('a', limit)}  ");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void The_length_error_maps_to_http_400()
    {
        var error = Create("suffix", new string('a', 21)).Error;

        Assert.Equal(400, error.ToHttpStatusCode());
    }

    [Fact]
    public void The_domain_limits_match_the_database_columns_of_customers_and_relations()
    {
        using var db = new CustomerDbContext(
            new DbContextOptionsBuilder<CustomerDbContext>().UseSqlServer("Server=unused").Options,
            new NoTenant()
        );

        foreach (
            var (owner, navigation) in new[]
            {
                (typeof(DomainCustomer), "PersonalName"),
                (typeof(CustomerRelation), "Name"),
            }
        )
        {
            var name = db.Model.FindEntityType(owner)!.FindNavigation(navigation)!.TargetEntityType;
            Assert.Equal(
                PersonalName.MiddleNameMaxLength,
                name.FindProperty(nameof(PersonalName.MiddleName))!.GetMaxLength()
            );
            Assert.Equal(PersonalName.PrefixMaxLength, name.FindProperty(nameof(PersonalName.Prefix))!.GetMaxLength());
            Assert.Equal(PersonalName.SuffixMaxLength, name.FindProperty(nameof(PersonalName.Suffix))!.GetMaxLength());
        }
    }

    private static Result<PersonalName> Create(string part, string value) =>
        part switch
        {
            "middle" => PersonalName.Create("Jane", "Doe", middleName: value),
            "prefix" => PersonalName.Create("Jane", "Doe", prefix: value),
            "suffix" => PersonalName.Create("Jane", "Doe", suffix: value),
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        };

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid value) { }
    }
}
