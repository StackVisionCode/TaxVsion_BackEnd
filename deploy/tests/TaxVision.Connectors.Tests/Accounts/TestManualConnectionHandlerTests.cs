using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;

namespace TaxVision.Connectors.Tests.Accounts;

// "Test connection" del alta manual: las dos pruebas siempre, sin guardar, y con el guard de identidad del alta.
public sealed class TestManualConnectionHandlerTests
{
    private static TestManualConnectionQuery Query(bool asOffice = false, string email = "me@firm.com") =>
        new(
            email,
            InitiatorEmail: "me@firm.com",
            asOffice,
            "imap.firm.com",
            993,
            true,
            email,
            "secret",
            "smtp.firm.com",
            587,
            true,
            email,
            "secret"
        );

    [Fact]
    public async Task Reports_both_checks_even_when_imap_fails()
    {
        var validator = new FakeManualAccountConnectivityValidator
        {
            ImapResult = Result.Failure(
                new Error(
                    "ManualAccountConnectivityValidator.ImapAuthFailed",
                    "The IMAP server rejected the username or password."
                )
            ),
        };

        var result = await TestManualConnectionHandler.Handle(Query(), validator, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Imap.Ok);
        Assert.Equal("ManualAccountConnectivityValidator.ImapAuthFailed", result.Value.Imap.ErrorCode);
        Assert.True(result.Value.Smtp.Ok);
        Assert.Null(result.Value.Smtp.ErrorCode);
        Assert.Equal(["smtp.firm.com"], validator.ValidatedSmtpHosts);
    }

    [Fact]
    public async Task A_personal_mailbox_that_is_not_the_login_email_is_rejected_without_contacting_servers()
    {
        var validator = new FakeManualAccountConnectivityValidator();

        var result = await TestManualConnectionHandler.Handle(
            Query(email: "someone.else@firm.com"),
            validator,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Connectors.EmailIdentity.Mismatch", result.Error.Code);
        Assert.Empty(validator.ValidatedImapHosts);
        Assert.Empty(validator.ValidatedSmtpHosts);
    }

    [Fact]
    public async Task The_office_mailbox_can_be_any_address()
    {
        var validator = new FakeManualAccountConnectivityValidator();

        var result = await TestManualConnectionHandler.Handle(
            Query(asOffice: true, email: "office@firm.com"),
            validator,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Imap.Ok);
        Assert.True(result.Value.Smtp.Ok);
    }
}
