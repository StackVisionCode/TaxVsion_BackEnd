using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Customer.Application.Customers.FromForms;

// Los nombres de lock se ven en las vistas del motor: la llave (email, teléfono, nombre) va siempre como hash.
public static class CustomerCreationLockResource
{
    public static string For(Guid tenantId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return $"customer-create:{tenantId:N}:{Convert.ToHexStringLower(hash)}";
    }
}
