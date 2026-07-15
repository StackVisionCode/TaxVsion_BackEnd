using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Consents;
using TaxVision.Signature.Domain.Validation;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// Tests de dominio para los aggregates de los 6 gaps: DocumentValidationRecord,
/// ConsentEvent y SignatureAuditEvent. Solo tocan reglas del dominio — la crypto vive
/// en Infrastructure y no se testea aquí.
/// </summary>
public sealed class GapAggregatesTests
{
    // -------------------- DocumentValidationRecord --------------------

    [Fact]
    public void DocumentValidationRecord_accepted_captures_metadata()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sha = new string('a', 64);

        var result = DocumentValidationRecord.RecordAccepted(
            tenantId,
            userId,
            sha,
            "form-8879.pdf",
            "application/pdf",
            sizeBytes: 12345,
            pageCount: 4,
            hasExistingSignatures: false
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentValidationVerdict.Accepted, result.Value.Verdict);
        Assert.Equal(sha, result.Value.ContentSha256);
        Assert.Equal(4, result.Value.PageCount);
        Assert.Null(result.Value.RejectionCode);
    }

    [Fact]
    public void DocumentValidationRecord_rejected_requires_code()
    {
        var result = DocumentValidationRecord.RecordRejected(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('a', 64),
            "x.pdf",
            "application/pdf",
            sizeBytes: 1,
            pageCount: 1,
            hasExistingSignatures: true,
            rejectionCode: "",
            rejectionReason: "already signed"
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.DocumentValidation.RejectionCode", result.Error.Code);
    }

    [Fact]
    public void DocumentValidationRecord_hash_validated()
    {
        var result = DocumentValidationRecord.RecordAccepted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "not-a-sha",
            "x.pdf",
            "application/pdf",
            1,
            1,
            false
        );
        Assert.True(result.IsFailure);
        Assert.Equal("Signature.DocumentValidation.Hash", result.Error.Code);
    }

    // -------------------- ConsentEvent --------------------

    [Fact]
    public void ConsentEvent_records_full_snapshot()
    {
        var tenant = Guid.NewGuid();
        var reqId = Guid.NewGuid();
        var signerId = Guid.NewGuid();

        var result = ConsentEvent.RecordAcceptance(
            tenant,
            reqId,
            signerId,
            textVersion: "consent.disclose.v1.en",
            textLanguage: "En",
            textSnapshot: "I authorize disclosure under §7216.",
            textHash: new string('b', 64),
            clientIp: "1.2.3.4",
            userAgent: "Mozilla/5.0"
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(tenant, result.Value.TenantId);
        Assert.Equal(signerId, result.Value.SignerId);
        Assert.Equal("En", result.Value.TextLanguage);
        Assert.Equal("consent.disclose.v1.en", result.Value.TextVersion);
        Assert.NotNull(result.Value.ClientIp);
    }

    [Theory]
    [InlineData("EN")] // 2 chars but with uppercase, still fine
    [InlineData("En")]
    [InlineData("Es")]
    public void ConsentEvent_language_short_codes_valid(string language)
    {
        var result = ConsentEvent.RecordAcceptance(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "v1",
            language,
            "text",
            new string('b', 64),
            null,
            null
        );
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ConsentEvent_rejects_long_language()
    {
        var result = ConsentEvent.RecordAcceptance(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "v1",
            "English",
            "text",
            new string('b', 64),
            null,
            null
        );
        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Consent.TextLanguage", result.Error.Code);
    }

    // -------------------- SignatureAuditEvent --------------------

    [Fact]
    public void SignatureAuditEvent_first_event_uses_genesis()
    {
        var result = SignatureAuditEvent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            sequence: 1,
            kind: SignatureAuditEventKind.RequestCreated,
            occurredAtUtc: DateTime.UtcNow,
            payloadJson: "{\"foo\":1}",
            previousChainHash: SignatureAuditEvent.GenesisMarker,
            chainHash: new string('c', 64)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureAuditEvent.GenesisMarker, result.Value.PreviousChainHash);
    }

    [Fact]
    public void SignatureAuditEvent_rejects_short_hash()
    {
        var result = SignatureAuditEvent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            SignatureAuditEventKind.RequestCreated,
            DateTime.UtcNow,
            "{}",
            SignatureAuditEvent.GenesisMarker,
            "short"
        );
        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Audit.ChainHash", result.Error.Code);
    }

    [Fact]
    public void SignatureAuditEvent_rejects_zero_sequence()
    {
        var result = SignatureAuditEvent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            SignatureAuditEventKind.RequestCreated,
            DateTime.UtcNow,
            "{}",
            SignatureAuditEvent.GenesisMarker,
            new string('c', 64)
        );
        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Audit.Sequence", result.Error.Code);
    }

    [Fact]
    public void SignatureAuditEvent_rejects_oversized_payload()
    {
        var big = new string('x', SignatureAuditEvent.MaxPayloadJsonLength + 1);
        var result = SignatureAuditEvent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            SignatureAuditEventKind.RequestCreated,
            DateTime.UtcNow,
            big,
            SignatureAuditEvent.GenesisMarker,
            new string('c', 64)
        );
        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Audit.PayloadSize", result.Error.Code);
    }
}
