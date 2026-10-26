using TaxVision.Scribe.Application.Templates.Commands;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Tests.Rendering;

namespace TaxVision.Scribe.Tests.Templates.Commands;

public sealed class CreateEmailTemplateHandlerTests
{
    private const string PushKey = "task.form_request_assigned.push.v1";

    [Theory]
    [InlineData(TemplateChannel.Email, false)]
    [InlineData(TemplateChannel.Push, true)]
    public async Task A_tenant_override_must_use_the_channel_of_the_system_template(
        TemplateChannel channel,
        bool allowed
    )
    {
        var system = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKey.Create(PushKey).Value,
                "Assigned push",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                TemplateChannel.Push
            )
            .Value;
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateEmailTemplateHandler.Handle(
            new CreateEmailTemplateCommand(
                TemplateScope.Tenant,
                Guid.NewGuid(),
                false,
                PushKey,
                "Office push",
                null,
                Guid.NewGuid(),
                channel
            ),
            new FakeEmailTemplateRepository(system),
            unitOfWork,
            default
        );

        Assert.Equal(allowed, result.IsSuccess);
        Assert.Equal(allowed ? 1 : 0, unitOfWork.SaveChangesCallCount);
        if (!allowed)
            Assert.Equal("EmailTemplate.ChannelMismatch", result.Error.Code);
    }
}
