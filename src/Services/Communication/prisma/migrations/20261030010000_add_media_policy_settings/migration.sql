BEGIN TRY

BEGIN TRAN;

-- AlterTable: F4 politica de media (calidad, video, audio, pantalla, sonidos, eco, SFU) en la fila global.
ALTER TABLE [dbo].[PlatformCommunicationSettings] ADD
    [QualitySampleIntervalSeconds] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualitySampleIntervalSeconds_df] DEFAULT 3,
    [QualityExcellentRttMs] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityExcellentRttMs_df] DEFAULT 150,
    [QualityGoodRttMs] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityGoodRttMs_df] DEFAULT 300,
    [QualityUnstableRttMs] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityUnstableRttMs_df] DEFAULT 500,
    [QualityExcellentLossPct] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityExcellentLossPct_df] DEFAULT 2,
    [QualityGoodLossPct] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityGoodLossPct_df] DEFAULT 5,
    [QualityUnstableLossPct] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityUnstableLossPct_df] DEFAULT 10,
    [QualityExcellentJitterMs] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityExcellentJitterMs_df] DEFAULT 30,
    [QualityGoodJitterMs] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityGoodJitterMs_df] DEFAULT 50,
    [QualityUnstableJitterMs] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityUnstableJitterMs_df] DEFAULT 100,
    [QualityUpgradeSamples] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityUpgradeSamples_df] DEFAULT 2,
    [QualityPoorSustainedSamples] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityPoorSustainedSamples_df] DEFAULT 3,
    [QualityRecoverySamples] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_QualityRecoverySamples_df] DEFAULT 3,
    [CameraMaxWidth] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_CameraMaxWidth_df] DEFAULT 1280,
    [CameraMaxHeight] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_CameraMaxHeight_df] DEFAULT 720,
    [CameraMaxFps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_CameraMaxFps_df] DEFAULT 30,
    [MeshCameraMaxWidth] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MeshCameraMaxWidth_df] DEFAULT 640,
    [MeshCameraMaxHeight] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MeshCameraMaxHeight_df] DEFAULT 360,
    [MeshCameraMaxFps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MeshCameraMaxFps_df] DEFAULT 24,
    [ScreenMaxWidth] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ScreenMaxWidth_df] DEFAULT 1920,
    [ScreenMaxHeight] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ScreenMaxHeight_df] DEFAULT 1080,
    [ScreenMaxFps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ScreenMaxFps_df] DEFAULT 15,
    [ScreenVideoMaxFps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ScreenVideoMaxFps_df] DEFAULT 30,
    [ScreenBitrateKbps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ScreenBitrateKbps_df] DEFAULT 1500,
    [ScreenHighBitrateKbps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ScreenHighBitrateKbps_df] DEFAULT 3000,
    [AudioMaxAverageBitrateKbps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_AudioMaxAverageBitrateKbps_df] DEFAULT 40,
    [MuteReleasesHardwareMinutes] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MuteReleasesHardwareMinutes_df] DEFAULT 5,
    [SoundsVolumePct] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_SoundsVolumePct_df] DEFAULT 60,
    [SoundsRepeatGapMs] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_SoundsRepeatGapMs_df] DEFAULT 400,
    [SfuMaxIncomingBitrateKbps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_SfuMaxIncomingBitrateKbps_df] DEFAULT 2500,
    [SfuStatsSampleSeconds] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_SfuStatsSampleSeconds_df] DEFAULT 10,
    [OpusFecEnabled] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_OpusFecEnabled_df] DEFAULT 1,
    [OpusDtxEnabled] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_OpusDtxEnabled_df] DEFAULT 1,
    [AudioEchoCancellationDefault] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_AudioEchoCancellationDefault_df] DEFAULT 1,
    [AudioNoiseSuppressionDefault] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_AudioNoiseSuppressionDefault_df] DEFAULT 1,
    [AudioAutoGainControlDefault] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_AudioAutoGainControlDefault_df] DEFAULT 1,
    [ClientsCanShareScreen] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ClientsCanShareScreen_df] DEFAULT 1,
    [ScreenAudioAllowed] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ScreenAudioAllowed_df] DEFAULT 1,
    [EchoDetectionEnabled] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_EchoDetectionEnabled_df] DEFAULT 1,
    [EchoMuteOnDetect] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_EchoMuteOnDetect_df] DEFAULT 0;

-- AlterTable: overrides de oficina (null = hereda): pantalla D-42, procesamiento de audio D-46, eco D-52.
ALTER TABLE [dbo].[TenantCommunicationSettings] ADD
    [ClientsCanShareScreen] BIT,
    [ScreenAudioAllowed] BIT,
    [AudioEchoCancellationDefault] BIT,
    [AudioNoiseSuppressionDefault] BIT,
    [AudioAutoGainControlDefault] BIT,
    [EchoMuteOnDetect] BIT;

-- AlterTable: preferencias de usuario D-46 (procesamiento de audio y como entrar).
ALTER TABLE [dbo].[UserCommunicationPreferences] ADD
    [EchoCancellation] BIT,
    [NoiseSuppression] BIT,
    [AutoGainControl] BIT,
    [JoinMuted] BIT NOT NULL CONSTRAINT [UserCommunicationPreferences_JoinMuted_df] DEFAULT 0,
    [JoinCameraOff] BIT NOT NULL CONSTRAINT [UserCommunicationPreferences_JoinCameraOff_df] DEFAULT 0;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
