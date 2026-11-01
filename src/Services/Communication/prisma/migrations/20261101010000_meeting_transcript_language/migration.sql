-- INV-T01 R-7 (E-282): idioma del transcript por reunión; null = el de cada participante o la oficina.
ALTER TABLE [dbo].[Meeting] ADD [TranscriptLanguage] NVARCHAR(16) NULL;
