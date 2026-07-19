namespace TaxVision.Tenant.Tests.Domain;

public sealed class TenantLogoTests
{
    private static TaxVision.Tenant.Domain.Tenant NewTenant() =>
        TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York").Value;

    [Fact]
    public void ConfirmLogo_with_valid_png_succeeds()
    {
        var tenant = NewTenant();
        var fileId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var result = tenant.ConfirmLogo(fileId, "image/png", 100_000, 200, 60, now);

        Assert.True(result.IsSuccess);
        Assert.Equal(fileId, tenant.LogoFileId);
        Assert.Equal("image/png", tenant.LogoContentType);
        Assert.Equal(100_000, tenant.LogoSizeBytes);
        Assert.Equal(200, tenant.LogoWidth);
        Assert.Equal(60, tenant.LogoHeight);
        Assert.Equal(now, tenant.LogoUpdatedAtUtc);
    }

    [Fact]
    public void SetLogoPending_with_valid_png_succeeds_but_leaves_it_unconfirmed()
    {
        var tenant = NewTenant();
        var fileId = Guid.NewGuid();

        var result = tenant.SetLogoPending(fileId, "image/png", 100_000, 200, 60);

        Assert.True(result.IsSuccess);
        Assert.Equal(fileId, tenant.LogoFileId);
        Assert.Equal("image/png", tenant.LogoContentType);
        Assert.Equal(100_000, tenant.LogoSizeBytes);
        Assert.Equal(200, tenant.LogoWidth);
        Assert.Equal(60, tenant.LogoHeight);
        // LogoUpdatedAtUtc null es EL marcador de "todavia no confirmado" que usan
        // DiscardPendingLogo y GetTenantLogoQuery — no debe setearse acá.
        Assert.Null(tenant.LogoUpdatedAtUtc);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("image/bmp")]
    [InlineData("application/octet-stream")]
    public void SetLogoPending_rejects_disallowed_content_types(string contentType)
    {
        var tenant = NewTenant();

        var result = tenant.SetLogoPending(Guid.NewGuid(), contentType, 100_000, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Logo.ContentType", result.Error.Code);
    }

    [Fact]
    public void SetLogoPending_rejects_size_over_500kb()
    {
        var tenant = NewTenant();

        var result = tenant.SetLogoPending(
            Guid.NewGuid(),
            "image/png",
            TaxVision.Tenant.Domain.Tenant.MaxLogoSizeBytes + 1,
            null,
            null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Logo.SizeBytes", result.Error.Code);
    }

    [Fact]
    public void SetLogoPending_rejects_zero_or_negative_size()
    {
        var tenant = NewTenant();

        var result = tenant.SetLogoPending(Guid.NewGuid(), "image/png", 0, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Logo.SizeBytes", result.Error.Code);
    }

    [Fact]
    public void SetLogoPending_rejects_empty_file_id()
    {
        var tenant = NewTenant();

        var result = tenant.SetLogoPending(Guid.Empty, "image/png", 100_000, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Logo.FileId", result.Error.Code);
    }

    [Fact]
    public void RemoveLogo_clears_all_fields()
    {
        var tenant = NewTenant();
        tenant.ConfirmLogo(Guid.NewGuid(), "image/png", 100_000, 200, 60, DateTime.UtcNow);

        tenant.RemoveLogo();

        Assert.Null(tenant.LogoFileId);
        Assert.Null(tenant.LogoContentType);
        Assert.Null(tenant.LogoSizeBytes);
        Assert.Null(tenant.LogoWidth);
        Assert.Null(tenant.LogoHeight);
        Assert.Null(tenant.LogoUpdatedAtUtc);
    }

    [Fact]
    public void RemoveLogo_is_idempotent_when_no_logo_exists()
    {
        var tenant = NewTenant();

        tenant.RemoveLogo();

        Assert.Null(tenant.LogoFileId);
    }

    [Fact]
    public void DiscardPendingLogo_clears_matching_unconfirmed_pending_upload()
    {
        var tenant = NewTenant();
        var pendingFileId = Guid.NewGuid();
        // SetLogoPending deja LogoUpdatedAtUtc null — mismo estado que deja
        // UploadTenantLogoHandler antes de que llegue el resultado del escaneo.
        tenant.SetLogoPending(pendingFileId, "image/png", 100_000, null, null);

        tenant.DiscardPendingLogo(pendingFileId);

        Assert.Null(tenant.LogoFileId);
    }

    [Fact]
    public void DiscardPendingLogo_ignores_mismatched_file_id()
    {
        var tenant = NewTenant();
        var currentFileId = Guid.NewGuid();
        tenant.ConfirmLogo(currentFileId, "image/png", 100_000, null, null, DateTime.UtcNow);

        // Un rechazo tardío de un upload viejo/reemplazado no debe pisar el logo actual.
        tenant.DiscardPendingLogo(Guid.NewGuid());

        Assert.Equal(currentFileId, tenant.LogoFileId);
    }

    [Fact]
    public void DiscardPendingLogo_never_removes_an_already_confirmed_logo()
    {
        var tenant = NewTenant();
        var fileId = Guid.NewGuid();
        // ConfirmLogo deja LogoUpdatedAtUtc no-null: simula un logo YA confirmado por FileAvailable.
        tenant.ConfirmLogo(fileId, "image/png", 100_000, null, null, DateTime.UtcNow);

        // Simula un evento de rechazo tardío/duplicado para el mismo fileId que ya se confirmó —
        // no debería poder pasar en el flujo real (Infected/BlockedByPolicy llegan antes que
        // FileAvailable, nunca después), pero el aggregate no debe confiar en el orden de entrega:
        // DiscardPendingLogo solo actúa sobre uploads TODAVÍA no confirmados (LogoUpdatedAtUtc null).
        tenant.DiscardPendingLogo(fileId);

        Assert.Equal(fileId, tenant.LogoFileId);
    }
}
