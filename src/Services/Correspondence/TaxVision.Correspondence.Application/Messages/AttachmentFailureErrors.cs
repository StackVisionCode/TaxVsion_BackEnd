using BuildingBlocks.Results;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Messages;

// Error apto para el usuario de un adjunto en Failed. Nunca expone FailureReason (texto técnico o
// del proveedor, plan §21): el mensaje sale del código de rechazo de storage. Los códigos no están
// mapeados en ErrorHttpMapping → 400, no 409 (el CRM solo sigue esperando ante un 409).
public static class AttachmentFailureErrors
{
    public const string StorageRejectedCode = "IncomingEmailAttachment.StorageRejected";
    public const string DownloadFailedCode = "IncomingEmailAttachment.DownloadFailed";

    public static Error For(IncomingEmailAttachment attachment) =>
        attachment.StorageRejectionReason is { } rejection
            ? new Error(StorageRejectedCode, StorageRejectionMessage(rejection))
            : new Error(DownloadFailedCode, "The attachment couldn't be downloaded from the mailbox. Try again.");

    private static string StorageRejectionMessage(string rejection) =>
        rejection switch
        {
            "EmptyFile" => "This attachment is empty and can't be saved.",
            "UploadPolicy" => "This file type can't be stored. Ask your admin to allow it, then try again.",
            "StorageQuota.Exceeded" => "Your office's storage is full. Free up space and try again.",
            "StorageQuota.Suspended" => "Storage is suspended for your office.",
            "StorageQuota.FileTooLarge" => "This attachment is larger than your plan allows.",
            _ => "This attachment couldn't be saved to storage.",
        };
}
