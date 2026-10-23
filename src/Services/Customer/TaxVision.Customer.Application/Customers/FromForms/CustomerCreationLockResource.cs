using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Customer.Application.Customers.FromForms;

// Los nombres de lock se ven en las vistas del motor: el email va siempre como hash.
public static class CustomerCreationLockResource
{
    public static string For(Guid tenantId, string normalizedEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return $"customer-create:{tenantId:N}:{Convert.ToHexStringLower(hash)}";
    }
}
