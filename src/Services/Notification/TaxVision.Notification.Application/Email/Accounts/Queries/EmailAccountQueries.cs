using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Email.Accounts.Queries;

public sealed record GetEmailAccountsQuery(Guid TenantId);

public static class GetEmailAccountsHandler
{
    public static async Task<Result<IReadOnlyList<EmailAccountResponse>>> Handle(
        GetEmailAccountsQuery query,
        IEmailAccountRepository repository,
        CancellationToken ct
    )
    {
        var items = await repository.ListAsync(query.TenantId, ct);
        IReadOnlyList<EmailAccountResponse> responses = items.Select(EmailAccountMapper.ToResponse).ToList();
        return Result.Success(responses);
    }
}

public sealed record GetEmailAccountByIdQuery(Guid AccountId, Guid TenantId);

public static class GetEmailAccountByIdHandler
{
    public static async Task<Result<EmailAccountResponse>> Handle(
        GetEmailAccountByIdQuery query,
        IEmailAccountRepository repository,
        CancellationToken ct
    )
    {
        var account = await repository.GetByIdAsync(query.AccountId, query.TenantId, ct);
        return account is null
            ? Result.Failure<EmailAccountResponse>(new Error("EmailAccount.NotFound", "Account not found."))
            : Result.Success(EmailAccountMapper.ToResponse(account));
    }
}

public sealed record GetAccountFoldersQuery(Guid AccountId, Guid TenantId);

public static class GetAccountFoldersHandler
{
    public static async Task<Result<IReadOnlyList<EmailFolderResponse>>> Handle(
        GetAccountFoldersQuery query,
        IEmailAccountRepository repository,
        CancellationToken ct
    )
    {
        var account = await repository.GetByIdAsync(query.AccountId, query.TenantId, ct);
        if (account is null)
            return Result.Failure<IReadOnlyList<EmailFolderResponse>>(new Error("EmailAccount.NotFound", "Account not found."));

        var folders = await repository.GetFoldersAsync(account.Id, ct);
        IReadOnlyList<EmailFolderResponse> responses = folders.Select(EmailAccountMapper.ToResponse).ToList();
        return Result.Success(responses);
    }
}

public sealed record GetAccountMessagesQuery(Guid AccountId, Guid TenantId, Guid? FolderId, int Page = 1, int Size = 20);

public static class GetAccountMessagesHandler
{
    public static async Task<Result<PagedResult<EmailMessageSummaryResponse>>> Handle(
        GetAccountMessagesQuery query,
        IEmailAccountRepository repository,
        CancellationToken ct
    )
    {
        if (query.Page < 1 || query.Size is < 1 or > 100)
            return Result.Failure<PagedResult<EmailMessageSummaryResponse>>(new Error("Query.Pagination", "Page must be >= 1 and size between 1 and 100."));

        var account = await repository.GetByIdAsync(query.AccountId, query.TenantId, ct);
        if (account is null)
            return Result.Failure<PagedResult<EmailMessageSummaryResponse>>(new Error("EmailAccount.NotFound", "Account not found."));

        var (items, total) = await repository.GetMessagesAsync(account.Id, query.FolderId, query.Page, query.Size, ct);
        IReadOnlyList<EmailMessageSummaryResponse> responses = items.Select(EmailAccountMapper.ToSummary).ToList();
        return Result.Success(new PagedResult<EmailMessageSummaryResponse>(responses, query.Page, query.Size, total));
    }
}

public sealed record GetAccountMessageQuery(Guid AccountId, Guid TenantId, Guid MessageId);

public static class GetAccountMessageHandler
{
    public static async Task<Result<EmailMessageDetailResponse>> Handle(
        GetAccountMessageQuery query,
        IEmailAccountRepository repository,
        CancellationToken ct
    )
    {
        var account = await repository.GetByIdAsync(query.AccountId, query.TenantId, ct);
        if (account is null)
            return Result.Failure<EmailMessageDetailResponse>(new Error("EmailAccount.NotFound", "Account not found."));

        var message = await repository.GetMessageAsync(account.Id, query.MessageId, ct);
        return message is null
            ? Result.Failure<EmailMessageDetailResponse>(new Error("EmailMessage.NotFound", "Message not found."))
            : Result.Success(EmailAccountMapper.ToDetail(message));
    }
}

public sealed record GetAccountThreadQuery(Guid AccountId, Guid TenantId, string ThreadId);

public static class GetAccountThreadHandler
{
    public static async Task<Result<IReadOnlyList<EmailMessageDetailResponse>>> Handle(
        GetAccountThreadQuery query,
        IEmailAccountRepository repository,
        CancellationToken ct
    )
    {
        var account = await repository.GetByIdAsync(query.AccountId, query.TenantId, ct);
        if (account is null)
            return Result.Failure<IReadOnlyList<EmailMessageDetailResponse>>(new Error("EmailAccount.NotFound", "Account not found."));

        var messages = await repository.GetThreadAsync(account.Id, query.ThreadId, ct);
        IReadOnlyList<EmailMessageDetailResponse> responses = messages.Select(EmailAccountMapper.ToDetail).ToList();
        return Result.Success(responses);
    }
}

public sealed record GetAccountSyncLogsQuery(Guid AccountId, Guid TenantId);

public static class GetAccountSyncLogsHandler
{
    public static async Task<Result<IReadOnlyList<EmailSyncLogResponse>>> Handle(
        GetAccountSyncLogsQuery query,
        IEmailAccountRepository repository,
        CancellationToken ct
    )
    {
        var account = await repository.GetByIdAsync(query.AccountId, query.TenantId, ct);
        if (account is null)
            return Result.Failure<IReadOnlyList<EmailSyncLogResponse>>(new Error("EmailAccount.NotFound", "Account not found."));

        var logs = await repository.GetSyncLogsAsync(account.Id, 50, ct);
        IReadOnlyList<EmailSyncLogResponse> responses = logs.Select(EmailAccountMapper.ToResponse).ToList();
        return Result.Success(responses);
    }
}
