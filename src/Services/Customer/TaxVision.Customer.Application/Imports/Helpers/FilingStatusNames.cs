using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Application.Imports.Helpers;

/// <summary>
/// Filing status por su nombre exacto o por los nombres comunes del IRS y en español (MFJ, "Married Filing
/// Jointly", HOH, QSS, Soltero…). Se comparan solo las letras, sin mayúsculas. Misma tabla que la corrección del CRM.
/// </summary>
internal static class FilingStatusNames
{
    private static readonly Dictionary<string, FilingStatus> Synonyms = new(StringComparer.Ordinal)
    {
        ["s"] = FilingStatus.Single,
        ["single"] = FilingStatus.Single,
        ["soltero"] = FilingStatus.Single,
        ["soltera"] = FilingStatus.Single,
        ["marriedfilingjointly"] = FilingStatus.MarriedJoint,
        ["marriedfilingjoint"] = FilingStatus.MarriedJoint,
        ["marriedjointly"] = FilingStatus.MarriedJoint,
        ["marriedjoint"] = FilingStatus.MarriedJoint,
        ["mfj"] = FilingStatus.MarriedJoint,
        ["marriedfilingseparately"] = FilingStatus.MarriedSeparate,
        ["marriedfilingseparate"] = FilingStatus.MarriedSeparate,
        ["marriedseparately"] = FilingStatus.MarriedSeparate,
        ["marriedseparate"] = FilingStatus.MarriedSeparate,
        ["mfs"] = FilingStatus.MarriedSeparate,
        ["headofhousehold"] = FilingStatus.HeadOfHousehold,
        ["headofhouse"] = FilingStatus.HeadOfHousehold,
        ["hoh"] = FilingStatus.HeadOfHousehold,
        ["qualifyingwidow"] = FilingStatus.QualifyingSurvivingSpouse,
        ["qualifyingwidower"] = FilingStatus.QualifyingSurvivingSpouse,
        ["qualifyingwidowwer"] = FilingStatus.QualifyingSurvivingSpouse,
        ["qualifyingwidowerwithdependentchild"] = FilingStatus.QualifyingSurvivingSpouse,
        ["qualifyingsurvivingspouse"] = FilingStatus.QualifyingSurvivingSpouse,
        ["qss"] = FilingStatus.QualifyingSurvivingSpouse,
        ["qw"] = FilingStatus.QualifyingSurvivingSpouse,
        ["qww"] = FilingStatus.QualifyingSurvivingSpouse,
    };

    public static bool TryParse(string? raw, out FilingStatus status)
    {
        if (ImportEnum.TryParseName(raw, out status))
            return true;
        var key = new string((raw ?? string.Empty).Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
        return Synonyms.TryGetValue(key, out status);
    }
}
