namespace BuildingBlocks.Authorization;

public static class CorrespondencePermissions
{
    public const string Read = "correspondence.read";

    /// <summary>Fase 8 — disparar la descarga bajo demanda de un attachment y pedir su URL firmada.</summary>
    public const string AttachmentDownload = "correspondence.attachment.download";

    /// <summary>Fase 11 — crear/editar (autoguardado) un <c>Draft</c>: <c>POST /correspondence/drafts</c>, <c>GET /correspondence/drafts/{id}</c>, <c>PATCH /correspondence/drafts/{id}</c>, <c>DELETE /correspondence/drafts/{id}</c>. Separado de <see cref="Send"/> (plan §27: redactar y enviar son acciones de riesgo distinto).</summary>
    public const string Compose = "correspondence.compose";

    /// <summary>Fase 11 — arrancar (o reutilizar) un reply sobre un mensaje entrante: <c>POST /correspondence/messages/{id}/reply/draft</c>. Independiente de <see cref="Compose"/> — un rol puede responder sin poder redactar correspondencia nueva desde cero, y viceversa.</summary>
    public const string Reply = "correspondence.reply";

    /// <summary>Fase 14 — enviar un <c>Draft</c> ya redactado: <c>POST /correspondence/drafts/{id}/send</c>. Separado de <see cref="Compose"/> (plan §27: redactar/editar es reversible, enviar no lo es — mismo criterio de riesgo distinto que ya separaba Compose de Reply).</summary>
    public const string Send = "correspondence.send";
}
