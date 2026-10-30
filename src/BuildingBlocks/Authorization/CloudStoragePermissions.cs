namespace BuildingBlocks.Authorization;

public static class CloudStoragePermissions
{
    public const string FileView = "cloudstorage.file.view";
    public const string FileUpload = "cloudstorage.file.upload";
    public const string FileDownload = "cloudstorage.file.download";
    public const string FileDelete = "cloudstorage.file.delete";
    public const string ReassignFormParticipant = "cloudstorage.file.reassign_form_participant";
    public const string SettingsManage = "cloudstorage.settings.manage";
    public const string AuditView = "cloudstorage.audit.view";
    public const string RecycleBinManage = "cloudstorage.recyclebin.manage";
    public const string FolderManage = "cloudstorage.folder.manage";
    public const string ShareCreate = "cloudstorage.share.create";
    public const string ShareRevoke = "cloudstorage.share.revoke";

    /// <summary>Otorga Upload/EditMetadata al crear un link y habilita cambiar expiracion de cualquier link del tenant.</summary>
    public const string ShareManage = "cloudstorage.share.manage";

    /// <summary>Fase L1.2 — legal hold sobre archivos del propio tenant. Peligroso (solo el admin raiz), nunca delegable.</summary>
    public const string LegalManage = "cloudstorage.legal.manage";

    /// <summary>Registrar y cerrar takedowns DMCA. Solo el equipo legal de la plataforma; un tenant nunca los ejerce.</summary>
    public const string DmcaManage = "cloudstorage.dmca.manage";

    /// <summary>Fase L1.3 — presentar contranotificacion DMCA sobre un archivo propio. Tenant-side (asignable normalmente), a diferencia de LegalManage.</summary>
    public const string DmcaCounterNotice = "cloudstorage.file.dmca_counternotice";

    /// <summary>Delegar: editar reglas de acceso a documentos solo sobre los clientes asignados al propio usuario, sin tocar los demas.</summary>
    public const string CustomerAccessManageAssigned = "cloudstorage.customer_access.manage_assigned";

    /// <summary>Admin: editar reglas de acceso a documentos sobre cualquier cliente del tenant, no solo los asignados.</summary>
    public const string CustomerAccessManageAny = "cloudstorage.customer_access.manage_any";

    /// <summary>PlatformOnly: topes de tamaño por carpeta/plan/oficina (UploadSizePolicy); ningún tenant los edita.</summary>
    public const string UploadPolicyManage = "cloudstorage.upload_policy.manage";
}
