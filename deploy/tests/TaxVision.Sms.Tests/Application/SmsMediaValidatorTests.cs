using TaxVision.Sms.Application.Messages;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;

namespace TaxVision.Sms.Tests.Application;

public sealed class SmsMediaValidatorTests
{
    private static SmsProviderCapabilities Caps(
        bool supportsMedia = true,
        bool supportsMultiple = true,
        int maxItems = 10,
        long maxSize = 1_000_000,
        params string[] allowedTypes
    ) =>
        new()
        {
            SupportsDeliveryReceipts = true,
            SupportsInbound = true,
            SupportsBulkSend = false,
            MaxBatchSize = 100,
            SupportsMedia = supportsMedia,
            SupportsMultipleMedia = supportsMultiple,
            MaxMediaItems = maxItems,
            MaxMediaSizeBytes = maxSize,
            AllowedMediaTypes = new HashSet<string>(allowedTypes, StringComparer.Ordinal),
        };

    private static SmsMediaPayload Media(string type = "application/pdf", long? size = 100) =>
        new("https://x/y", type, "y", size);

    [Fact]
    public void No_media_is_always_valid()
    {
        var error = SmsMediaValidator.Validate(Caps(supportsMedia: false), []);
        Assert.Null(error);
    }

    [Fact]
    public void Media_against_provider_without_media_support_fails()
    {
        var error = SmsMediaValidator.Validate(Caps(supportsMedia: false), [Media()]);

        Assert.NotNull(error);
        Assert.Equal(SmsErrors.MediaNotSupported.Code, error!.Code);
    }

    [Fact]
    public void Multiple_media_without_multi_support_fails()
    {
        var error = SmsMediaValidator.Validate(Caps(supportsMultiple: false), [Media(), Media()]);

        Assert.NotNull(error);
        Assert.Equal(SmsErrors.MultipleMediaNotSupported.Code, error!.Code);
    }

    [Fact]
    public void Exceeding_max_media_items_fails()
    {
        var caps = Caps(maxItems: 1);
        var error = SmsMediaValidator.Validate(caps, [Media(), Media()]);

        Assert.NotNull(error);
        Assert.Equal(SmsErrors.MediaCountExceeded.Code, error!.Code);
    }

    [Fact]
    public void Media_over_size_limit_fails()
    {
        var error = SmsMediaValidator.Validate(Caps(maxSize: 50), [Media(size: 100)]);

        Assert.NotNull(error);
        Assert.Equal(SmsErrors.MediaTooLarge.Code, error!.Code);
    }

    [Fact]
    public void Media_with_disallowed_content_type_fails()
    {
        var caps = Caps(allowedTypes: "image/png");
        var error = SmsMediaValidator.Validate(caps, [Media(type: "application/pdf")]);

        Assert.NotNull(error);
        Assert.Equal(SmsErrors.MediaTypeNotSupported.Code, error!.Code);
    }

    [Fact]
    public void Valid_media_within_all_limits_passes()
    {
        var caps = Caps(allowedTypes: "application/pdf");
        var error = SmsMediaValidator.Validate(caps, [Media(type: "application/pdf", size: 500)]);

        Assert.Null(error);
    }
}
