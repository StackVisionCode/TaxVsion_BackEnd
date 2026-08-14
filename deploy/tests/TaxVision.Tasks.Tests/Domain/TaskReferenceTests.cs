using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class TaskReferenceTests
{
    [Fact]
    public void Both_parts_are_optional_and_independent()
    {
        var customerOnly = TaskReference.Create(Guid.NewGuid(), null);
        var yearOnly = TaskReference.Create(null, 2025);

        Assert.True(customerOnly.IsSuccess);
        Assert.Null(customerOnly.Value.TaxYear);

        Assert.True(yearOnly.IsSuccess);
        Assert.Null(yearOnly.Value.CustomerId);
    }

    [Fact]
    public void None_carries_neither_customer_nor_tax_year()
    {
        Assert.Null(TaskReference.None.CustomerId);
        Assert.Null(TaskReference.None.TaxYear);
    }

    /// <summary><c>Guid.Empty</c> no es «sin cliente»: produce tareas que parecen referenciadas y no lo están.</summary>
    [Fact]
    public void An_empty_customer_guid_is_rejected_instead_of_treated_as_absent()
    {
        var result = TaskReference.Create(Guid.Empty, 2025);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Reference.CustomerInvalid", result.Error.Code);
    }

    [Theory]
    [InlineData(1989)]
    [InlineData(2101)]
    [InlineData(202)]
    [InlineData(20255)]
    public void A_tax_year_outside_the_range_is_a_typo_and_is_rejected(int taxYear)
    {
        var result = TaskReference.Create(null, taxYear);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Reference.TaxYearOutOfRange", result.Error.Code);
    }

    [Theory]
    [InlineData(TaskReference.MinTaxYear)]
    [InlineData(2025)]
    [InlineData(TaskReference.MaxTaxYear)]
    public void The_range_bounds_themselves_are_accepted(int taxYear)
    {
        Assert.True(TaskReference.Create(null, taxYear).IsSuccess);
    }
}
