BEGIN TRY

BEGIN TRAN;

-- AlterTable: F4, dos numeros que el cliente tenia como default tecnico pasan a la politica global.
ALTER TABLE [dbo].[PlatformCommunicationSettings] ADD
    [CameraMaxBitrateKbps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_CameraMaxBitrateKbps_df] DEFAULT 1250,
    [ClientRecordingAudioKbps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ClientRecordingAudioKbps_df] DEFAULT 56;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
