using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Domain.Quotas;

/// <summary>Libera lo que el archivo tenía contado al borrarse físicamente (CST-5).</summary>
public static class QuotaRelease
{
    public static void Apply(TenantStorageLimit limit, FileObject file)
    {
        switch (file.QuotaFootprint)
        {
            case QuotaFootprint.Used:
                limit.ReleaseUsed(file.SizeBytes);
                break;
            case QuotaFootprint.Reserved:
                limit.Release(file.SizeBytes);
                break;
        }
    }
}
