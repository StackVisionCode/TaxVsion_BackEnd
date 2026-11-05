using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files;

/// <summary>Bucket donde vive el blob según el estado efectivo del archivo (CST-5).</summary>
public static class StoredObjectLocation
{
    public static string BucketFor(FileObject file, CloudStorageOptions options)
    {
        if (file.QuotaFootprint == QuotaFootprint.Used)
            return options.MainBucket;
        return file.EffectiveStatus is FileStatus.Infected or FileStatus.BlockedByPolicy or FileStatus.PendingReview
            ? options.QuarantineBucket
            : options.TempBucket;
    }
}
