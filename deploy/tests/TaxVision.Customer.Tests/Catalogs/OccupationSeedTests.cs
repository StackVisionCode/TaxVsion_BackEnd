using System.Text.RegularExpressions;
using TaxVision.Customer.Infrastructure.Persistence.Seeds;

namespace TaxVision.Customer.Tests.Catalogs;

/// <summary>
/// F6: el catálogo de ocupaciones no ofrece las mal escritas (se desactivan, no se borran: hay clientes que las
/// usan) y cubre las ocupaciones de EE. UU. (BLS SOC 2018), militares y situaciones comunes, para que nadie se
/// quede sin la suya al crear un cliente. Sin nombres activos repetidos ni Ids repetidos.
/// </summary>
public sealed partial class OccupationSeedTests
{
    private static readonly string[] Misspelled =
    [
        "Archtect",
        "Busness Analyst",
        "Busness Owner",
        "Cl Engneer",
        "Constructon Manager",
        "Database Admnstrator",
        "Dentst",
        "Electrcan",
        "Flght Attendant",
        "Fnancal Analyst",
        "Frefghter",
        "Graphc Desgner",
        "Hookie Player",
        "Loan Offcer",
        "Marketng Drector",
        "Mechancal Engneer",
        "Network Engneer",
        "Nsurance Agent",
        "Nvestment Banker",
        "Operatons Manager",
        "Paramedc",
        "Pharmacst",
        "Physcal Therapst",
        "Physcan",
        "Plot",
        "Polce Offcer",
        "Psychologst",
        "Research Scentst",
        "Socal Worker",
        "Software Engneer",
        "T Manager",
        "Truck Er",
        "Veternaran",
    ];

    private static readonly IReadOnlyList<(Guid Id, string Name, bool IsActive)> Seed = OccupationSeed
        .All.Select(o =>
            (
                (Guid)o.GetType().GetProperty("Id")!.GetValue(o)!,
                (string)o.GetType().GetProperty("Name")!.GetValue(o)!,
                (bool)o.GetType().GetProperty("IsActive")!.GetValue(o)!
            )
        )
        .ToList();

    [Fact]
    public void Misspelled_occupations_are_kept_but_inactive()
    {
        foreach (var name in Misspelled)
            Assert.False(Seed.Single(o => o.Name == name).IsActive, name);
    }

    [Theory]
    [InlineData("Retired")]
    [InlineData("Student")]
    [InlineData("Unemployed")]
    [InlineData("Homemaker")]
    [InlineData("Disabled")]
    [InlineData("Self-Employed")]
    [InlineData("Other")]
    [InlineData("Business Owner")]
    [InlineData("IT Manager")]
    [InlineData("Dentist")]
    [InlineData("Truck Driver")]
    [InlineData("Military Service Member")]
    [InlineData("Licensed Practical Nurse")]
    [InlineData("Construction Laborer")]
    public void Common_occupations_are_offered(string name)
    {
        Assert.Contains(Seed, o => o.Name == name && o.IsActive);
    }

    [Fact]
    public void It_covers_the_us_occupations()
    {
        Assert.True(Seed.Count(o => o.IsActive) >= 1_100, $"Only {Seed.Count(o => o.IsActive)} active occupations.");
    }

    [Fact]
    public void Active_names_are_not_repeated()
    {
        var repeated = Seed.Where(o => o.IsActive)
            .GroupBy(o => Letters().Replace(o.Name.ToLowerInvariant(), string.Empty))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(repeated);
    }

    [Fact]
    public void Ids_are_not_repeated()
    {
        Assert.Equal(Seed.Count, Seed.Select(o => o.Id).Distinct().Count());
    }

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex Letters();
}
