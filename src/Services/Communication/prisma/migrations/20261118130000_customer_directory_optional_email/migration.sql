BEGIN TRY

BEGIN TRAN;

-- F-E (email opcional): un cliente sin email también entra al directorio (sale en el rail y en el chat por nombre).
ALTER TABLE [dbo].[CustomerDirectoryEntry] ALTER COLUMN [Email] NVARCHAR(320) NULL;

-- Fecha (occurredOn) del último aviso de perfil aplicado (alta, cambio, bloque del import): un aviso viejo que
-- llega tarde no pisa nombre ni email. Null = sin aviso con fecha aplicado (el próximo gana).
ALTER TABLE [dbo].[CustomerDirectoryEntry] ADD [ProfileChangedAtUtc] DATETIME2 NULL;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
