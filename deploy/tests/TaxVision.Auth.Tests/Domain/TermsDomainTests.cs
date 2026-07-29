using TaxVision.Auth.Domain.Terms;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>Fase L1.4 / PayFlow Fase 6 — TenantTermsAcceptance.Accept.</summary>
public sealed class TermsDomainTests
{
    [Fact]
    public void Accept_records_the_tenant_user_version_and_request_metadata()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var termsVersionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var acceptance = TenantTermsAcceptance.Accept(
            tenantId,
            userId,
            "2026-07-14",
            termsVersionId,
            new string('a', 64),
            "Onboarding",
            "203.0.113.5",
            "xunit",
            now
        );

        Assert.Equal(tenantId, acceptance.TenantId);
        Assert.Equal(userId, acceptance.AcceptedByUserId);
        Assert.Equal("2026-07-14", acceptance.TermsVersion);
        Assert.Equal(termsVersionId, acceptance.TermsVersionId);
        Assert.Equal(new string('a', 64), acceptance.ContentHash);
        Assert.Equal("Onboarding", acceptance.AcceptedInContext);
        Assert.Equal("203.0.113.5", acceptance.AcceptedFromIp);
        Assert.Equal("xunit", acceptance.UserAgent);
        Assert.Equal(now, acceptance.AcceptedAtUtc);
    }

    [Fact]
    public void Accept_allows_ip_user_agent_and_content_hash_to_be_missing()
    {
        var acceptance = TenantTermsAcceptance.Accept(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "2026-07-14",
            Guid.NewGuid(),
            null,
            "LegacyPreV2",
            null,
            null,
            DateTime.UtcNow
        );

        Assert.Null(acceptance.ContentHash);
        Assert.Null(acceptance.AcceptedFromIp);
        Assert.Null(acceptance.UserAgent);
    }
}
