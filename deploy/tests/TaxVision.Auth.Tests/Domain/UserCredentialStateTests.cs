using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>Tabla de transiciones del estado de la credencial (03 §4.6) e invariantes I-1, I-2 e I-3.</summary>
public sealed class UserCredentialStateTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static User NewUser() =>
        User.Register(Guid.NewGuid(), "Ana", "Perez", "ana@example.com", "hash", UserActorType.TenantEmployee).Value;

    private static CredentialAdministrationReason Reason(
        CredentialAdministrationReasonCode code = CredentialAdministrationReasonCode.OfficePolicy,
        string? note = null
    ) => CredentialAdministrationReason.Create(code, note).Value;

    private static CredentialAdministrationReason Compromise() =>
        Reason(CredentialAdministrationReasonCode.SuspectedCompromise);

    [Fact]
    public void A_new_user_is_active()
    {
        Assert.Equal(CredentialState.Active, NewUser().CredentialState);
    }

    [Fact]
    public void Require_change_on_an_active_user_marks_change_required_with_reason_author_and_time()
    {
        var user = NewUser();

        var result = user.RequirePasswordChange(Reason(note: "  rotate  "), Owner, Now);

        Assert.Equal(CredentialTransition.Changed, result.Value);
        Assert.Equal(CredentialStatus.ChangeRequired, user.CredentialStatus);
        Assert.Equal(CredentialAdministrationReasonCode.OfficePolicy, user.CredentialState.Reason!.Code);
        Assert.Equal("rotate", user.CredentialState.Reason.Note);
        Assert.Equal(Now, user.CredentialStateChangedAtUtc);
        Assert.Equal(Owner, user.CredentialStateChangedByUserId);
    }

    [Fact]
    public void Require_change_twice_is_idempotent()
    {
        var user = NewUser();
        user.RequirePasswordChange(Reason(), Owner, Now);

        var again = user.RequirePasswordChange(
            Reason(CredentialAdministrationReasonCode.UserRequest),
            Owner,
            Now.AddHours(1)
        );

        Assert.Equal(CredentialTransition.AlreadyInState, again.Value);
        Assert.Equal(Now, user.CredentialStateChangedAtUtc);
        Assert.Equal(CredentialAdministrationReasonCode.OfficePolicy, user.CredentialStateReasonCode);
    }

    [Fact]
    public void Require_change_on_a_secured_account_is_an_invalid_transition()
    {
        var user = NewUser();
        user.MarkResetRequired(Compromise(), Owner, Now);

        var result = user.RequirePasswordChange(Reason(), Owner, Now);

        Assert.Equal("Auth.CredentialAdmin.InvalidTransition", result.Error.Code);
        Assert.Equal(CredentialStatus.ResetRequired, user.CredentialStatus);
    }

    [Fact]
    public void Require_change_does_not_accept_a_suspected_compromise()
    {
        var result = NewUser().RequirePasswordChange(Compromise(), Owner, Now);

        Assert.Equal("Auth.CredentialAdmin.InvalidReason", result.Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Secure_moves_active_or_change_required_to_reset_required(bool changeRequiredFirst)
    {
        var user = NewUser();
        if (changeRequiredFirst)
            user.RequirePasswordChange(Reason(), Owner, Now);

        var result = user.MarkResetRequired(Compromise(), Owner, Now.AddMinutes(5));

        Assert.Equal(CredentialTransition.Changed, result.Value);
        Assert.Equal(CredentialStatus.ResetRequired, user.CredentialStatus);
        Assert.Equal(Now.AddMinutes(5), user.CredentialStateChangedAtUtc);
    }

    [Fact]
    public void Secure_twice_is_idempotent_and_the_new_reason_replaces_the_saved_one()
    {
        var user = NewUser();
        user.MarkResetRequired(Compromise(), Owner, Now);

        var again = user.MarkResetRequired(
            Reason(CredentialAdministrationReasonCode.Other, "phishing mail"),
            Owner,
            Now.AddHours(1)
        );

        Assert.Equal(CredentialTransition.AlreadyInState, again.Value);
        Assert.Equal(Now, user.CredentialStateChangedAtUtc);
        Assert.Equal(CredentialAdministrationReasonCode.Other, user.CredentialStateReasonCode);
        Assert.Equal("phishing mail", user.CredentialStateReasonNote);
    }

    [Theory]
    [InlineData(CredentialAdministrationReasonCode.OfficePolicy)]
    [InlineData(CredentialAdministrationReasonCode.SharedPassword)]
    [InlineData(CredentialAdministrationReasonCode.UserRequest)]
    public void Secure_only_accepts_a_suspected_compromise_or_other(CredentialAdministrationReasonCode code)
    {
        var result = NewUser().MarkResetRequired(Reason(code), Owner, Now);

        Assert.Equal("Auth.CredentialAdmin.InvalidReason", result.Error.Code);
    }

    [Fact]
    public void Change_password_clears_change_required()
    {
        var user = NewUser();
        user.RequirePasswordChange(Reason(), Owner, Now);

        var result = user.ChangePassword("new-hash", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(CredentialState.Active, user.CredentialState);
    }

    // I-3: una cuenta asegurada solo sale con un reset por link.
    [Fact]
    public void Change_password_on_a_secured_account_fails_and_keeps_the_old_password()
    {
        var user = NewUser();
        user.MarkResetRequired(Compromise(), Owner, Now);

        var result = user.ChangePassword("new-hash", Now);

        Assert.Equal("User.CredentialResetRequired", result.Error.Code);
        Assert.Equal("hash", user.PasswordHash);
    }

    [Theory]
    [InlineData(CredentialStatus.Active)]
    [InlineData(CredentialStatus.ChangeRequired)]
    [InlineData(CredentialStatus.ResetRequired)]
    public void Completing_a_reset_clears_any_state(CredentialStatus from)
    {
        var user = NewUser();
        if (from == CredentialStatus.ChangeRequired)
            user.RequirePasswordChange(Reason(), Owner, Now);
        if (from == CredentialStatus.ResetRequired)
            user.MarkResetRequired(Compromise(), Owner, Now);

        var result = user.CompletePasswordReset("new-hash", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-hash", user.PasswordHash);
        Assert.Equal(CredentialState.Active, user.CredentialState);
    }

    // I-2.
    [Fact]
    public void Only_a_secured_account_refuses_password_sign_in()
    {
        var user = NewUser();
        Assert.True(user.EnsurePasswordSignInAllowed().IsSuccess);

        user.RequirePasswordChange(Reason(), Owner, Now);
        Assert.True(user.EnsurePasswordSignInAllowed().IsSuccess);

        user.MarkResetRequired(Compromise(), Owner, Now);
        Assert.Equal("User.CredentialResetRequired", user.EnsurePasswordSignInAllowed().Error.Code);
    }

    // I-1: una cuenta desactivada u offboarded no cambia de estado por administración.
    [Fact]
    public void An_inactive_or_offboarded_user_cannot_change_state()
    {
        var deactivated = NewUser();
        deactivated.Deactivate(Owner, Now);
        var offboarded = NewUser();
        offboarded.Offboard(Owner, Now);

        Assert.Equal(
            "Auth.CredentialAdmin.TargetInactive",
            deactivated.RequirePasswordChange(Reason(), Owner, Now).Error.Code
        );
        Assert.Equal(
            "Auth.CredentialAdmin.TargetInactive",
            offboarded.MarkResetRequired(Compromise(), Owner, Now).Error.Code
        );
    }

    [Fact]
    public void The_reason_needs_a_note_with_other_and_the_note_is_capped()
    {
        Assert.Equal(
            "Auth.CredentialAdmin.ReasonRequired",
            CredentialAdministrationReason.Create(CredentialAdministrationReasonCode.Other, "  ").Error.Code
        );
        Assert.Equal(
            "Auth.CredentialAdmin.InvalidReason",
            CredentialAdministrationReason.Create((CredentialAdministrationReasonCode)9, null).Error.Code
        );
        var capped = CredentialAdministrationReason.Create(
            CredentialAdministrationReasonCode.UserRequest,
            new string('x', 300)
        );
        Assert.Equal(CredentialAdministrationReason.MaxNoteLength, capped.Value.Note!.Length);
    }
}
