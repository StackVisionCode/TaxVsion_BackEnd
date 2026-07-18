using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Templates;
using TaxVision.Signature.Domain.Templates.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class SignatureTemplateTests
{
    // -------------------- Factory --------------------

    [Fact]
    public void CreateDraft_starts_in_draft_with_no_slots_or_fields()
    {
        var result = NewDraft();

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureTemplateStatus.Draft, result.Value.Status);
        Assert.Empty(result.Value.Slots);
        Assert.Empty(result.Value.Fields);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public void CreateDraft_rejects_invalid_titles(string title)
    {
        var result = SignatureTemplate.CreateDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            title,
            null,
            SignatureCategory.Fiscal,
            defaultTokenExpirationHours: 72,
            requiresSequentialSigning: false,
            requiresConsent: false,
            generateCertificate: false
        );

        Assert.True(result.IsFailure);
    }

    // -------------------- Slots --------------------

    [Fact]
    public void AddSlot_assigns_incrementing_order()
    {
        var template = NewDraft().Value;
        var role = TemplateSlotRole.Create("Primary Taxpayer").Value;

        var a = template.AddSlot(role, "En").Value;
        var b = template.AddSlot(TemplateSlotRole.Create("Spouse").Value, "En").Value;

        Assert.Equal(1, a.Order);
        Assert.Equal(2, b.Order);
    }

    [Fact]
    public void RemoveSlot_renumbers_remaining_slots_and_rewires_fields()
    {
        var template = NewDraft().Value;
        template.AddSlot(TemplateSlotRole.Create("Slot1").Value, "En");
        template.AddSlot(TemplateSlotRole.Create("Slot2").Value, "En");
        template.AddSlot(TemplateSlotRole.Create("Slot3").Value, "En");
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        template.PlaceField(3, SignatureFieldKind.Signature, pos, null, false);

        template.RemoveSlot(2);

        Assert.Equal(2, template.Slots.Count);
        Assert.Contains(template.Slots, s => s.Order == 1 && s.Role.Value == "Slot1");
        Assert.Contains(template.Slots, s => s.Order == 2 && s.Role.Value == "Slot3");
        var remainingField = Assert.Single(template.Fields);
        Assert.Equal(2, remainingField.SlotOrder);
    }

    [Fact]
    public void PlaceField_fails_when_slot_does_not_exist()
    {
        var template = NewDraft().Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;

        var result = template.PlaceField(99, SignatureFieldKind.Signature, pos, null, false);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Template.SlotMissing", result.Error.Code);
    }

    // -------------------- Publish --------------------

    [Fact]
    public void Publish_requires_at_least_one_slot_and_signature_field()
    {
        var template = NewDraft().Value;

        var noSlots = template.Publish();
        Assert.True(noSlots.IsFailure);
        Assert.Equal("Signature.Template.NoSlots", noSlots.Error.Code);

        template.AddSlot(TemplateSlotRole.Create("Solo").Value, "En");
        var noField = template.Publish();
        Assert.True(noField.IsFailure);
        Assert.Equal("Signature.Template.NoSignatureField", noField.Error.Code);

        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        template.PlaceField(1, SignatureFieldKind.Signature, pos, null, false);
        Assert.True(template.Publish().IsSuccess);
        Assert.Equal(SignatureTemplateStatus.Published, template.Status);
    }

    [Fact]
    public void Cannot_edit_after_publish()
    {
        var template = NewPublishedTemplate();

        Assert.Throws<InvalidOperationException>(() =>
            template.AddSlot(TemplateSlotRole.Create("New Slot").Value, "En")
        );
    }

    [Fact]
    public void Archive_from_published_marks_as_archived()
    {
        var template = NewPublishedTemplate();

        var result = template.Archive();

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureTemplateStatus.Archived, template.Status);
        Assert.NotNull(template.ArchivedAtUtc);
    }

    [Fact]
    public void Archive_is_idempotent()
    {
        var template = NewPublishedTemplate();
        template.Archive();

        var second = template.Archive();

        Assert.True(second.IsSuccess);
    }

    // -------------------- TemplateSlotRole VO --------------------

    [Fact]
    public void TemplateSlotRole_preserves_case_and_trims()
    {
        var result = TemplateSlotRole.Create("  Preparer  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Preparer", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Bad<Char>Role")]
    public void TemplateSlotRole_rejects_invalid_values(string raw)
    {
        Assert.True(TemplateSlotRole.Create(raw).IsFailure);
    }

    // ================== helpers ==================

    private static BuildingBlocks.Results.Result<SignatureTemplate> NewDraft() =>
        SignatureTemplate.CreateDraft(
            tenantId: Guid.NewGuid(),
            createdByUserId: Guid.NewGuid(),
            title: "Standard Consent 2026",
            description: null,
            category: SignatureCategory.ConsentToDisclose,
            defaultTokenExpirationHours: 72,
            requiresSequentialSigning: false,
            requiresConsent: true,
            generateCertificate: false
        );

    private static SignatureTemplate NewPublishedTemplate()
    {
        var template = NewDraft().Value;
        template.AddSlot(TemplateSlotRole.Create("Solo").Value, "En");
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        template.PlaceField(1, SignatureFieldKind.Signature, pos, null, false);
        template.Publish();
        return template;
    }
}
