BEGIN TRY

BEGIN TRAN;

-- Retira la tabla auxiliar NotificationActionMapping. El puente in-app ahora viaja con
-- resourceType/resourceId/action/titleKey en el propio evento (contrato
-- notification.inapp.requested.v1), por lo que esta tabla queda sin consumidor.
DROP TABLE [dbo].[NotificationActionMapping];

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
