using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// A1 — de qué buzón se puede redactar. El <c>AccountId</c> llega en el cuerpo del request y nadie lo
/// validaba: un empleado podía crear un borrador —y después enviarlo— <b>desde la cuenta personal de un
/// colega</b>, que es exactamente el caso que el gate de buzón ya cubría del lado de la lectura.
///
/// <para>
/// El set visible lo resuelve <c>MailboxVisibilityResolver</c> en la capa Api: <c>null</c> significa
/// "ve (y usa) todos los buzones de la oficina" —lo tiene quien posee <c>connectors.accounts.office.read</c>
/// o <c>accounts.write</c>—; cualquier otro set es la lista de sus buzones personales. Un set vacío es
/// fail-closed a propósito: si Connectors no responde, no se redacta desde ningún buzón en vez de
/// abrirlos todos.
/// </para>
/// </summary>
public static class SendingAccountGuard
{
    public static readonly Error NotVisible = new(
        "Draft.AccountNotVisible",
        "You can only compose from a mailbox you have access to."
    );

    public static Result Validate(Guid accountId, IReadOnlyCollection<Guid>? visibleAccountIds) =>
        visibleAccountIds is null || visibleAccountIds.Contains(accountId)
            ? Result.Success()
            : Result.Failure(NotVisible);
}
