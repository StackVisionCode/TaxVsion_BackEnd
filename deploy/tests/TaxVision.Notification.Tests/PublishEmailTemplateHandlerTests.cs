using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Email.Templates.Commands;
using TaxVision.Notification.Domain.Emailing;
using TaxVision.Notification.Domain.Emailing.Templates;

namespace TaxVision.Notification.Tests;

/// <summary>NOT-14: publicar una plantilla archivada no toca sus versiones.</summary>
public sealed class PublishEmailTemplateHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly InMemoryTemplates _repository = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Archived_template_is_not_published()
    {
        var template = NewTemplate();
        var draft = NewVersion(template, 1);
        template.Archive();

        var result = await PublishAsync(template, draft);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.Archived", result.Error.Code);
        Assert.Equal(EmailTemplateVersionStatus.Draft, draft.Status);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Archived_template_keeps_its_published_version()
    {
        var template = NewTemplate();
        var published = NewVersion(template, 1);
        var draft = NewVersion(template, 2);
        await PublishAsync(template, published);
        template.Archive();

        await PublishAsync(template, draft);

        Assert.Equal(EmailTemplateVersionStatus.Published, published.Status);
        Assert.Equal(EmailTemplateVersionStatus.Draft, draft.Status);
        Assert.Equal(published.Id, template.CurrentVersionId);
    }

    [Fact]
    public async Task Publishing_a_new_version_supersedes_the_previous_one()
    {
        var template = NewTemplate();
        var first = NewVersion(template, 1);
        var second = NewVersion(template, 2);
        await PublishAsync(template, first);

        var result = await PublishAsync(template, second);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailTemplateVersionStatus.Superseded, first.Status);
        Assert.Equal(EmailTemplateVersionStatus.Published, second.Status);
        Assert.Equal(second.Id, template.CurrentVersionId);
    }

    [Fact]
    public async Task Publishing_the_current_version_again_keeps_it_published()
    {
        var template = NewTemplate();
        var version = NewVersion(template, 1);
        await PublishAsync(template, version);

        var result = await PublishAsync(template, version);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailTemplateVersionStatus.Published, version.Status);
    }

    private Task<BuildingBlocks.Results.Result> PublishAsync(EmailTemplate template, EmailTemplateVersion version) =>
        PublishEmailTemplateHandler.Handle(
            new PublishEmailTemplateCommand(template.Id, version.Id, _tenantId, IsPlatformAdmin: false),
            _repository,
            _unitOfWork,
            CancellationToken.None
        );

    private EmailTemplate NewTemplate()
    {
        var template = EmailTemplate
            .Create(EmailScope.Tenant, _tenantId, "welcome", "Hi", null, null, "[]", null)
            .Value;
        _repository.Templates.Add(template);
        return template;
    }

    private EmailTemplateVersion NewVersion(EmailTemplate template, int number)
    {
        var version = EmailTemplateVersion
            .Create(template.Id, number, "Hi", $"key-{number}", Guid.NewGuid(), null, null, null, null, null)
            .Value;
        _repository.Versions.Add(version);
        return version;
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class InMemoryTemplates : IEmailTemplateRepository
    {
        public List<EmailTemplate> Templates { get; } = [];
        public List<EmailTemplateVersion> Versions { get; } = [];

        public Task<EmailTemplate?> GetByIdAsync(Guid id, Guid? tenantId, CancellationToken ct = default) =>
            Task.FromResult(Templates.FirstOrDefault(t => t.Id == id));

        public Task<EmailTemplateVersion?> GetVersionAsync(
            Guid templateId,
            Guid versionId,
            CancellationToken ct = default
        ) => Task.FromResult(Versions.FirstOrDefault(v => v.Id == versionId && v.TemplateId == templateId));

        public Task AddAsync(EmailTemplate template, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddVersionAsync(EmailTemplateVersion version, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EmailTemplate?> GetByKeyAsync(
            EmailScope scope,
            Guid? tenantId,
            string templateKey,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<int> GetNextVersionNumberAsync(Guid templateId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EmailTemplate>> ListAsync(
            Guid? tenantId,
            bool includeSystem,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<EmailTemplateVersion>> ListVersionsAsync(
            Guid templateId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
