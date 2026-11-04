BEGIN TRY

BEGIN TRAN;

-- Origen de los mensajes System (Call | Membership | Notice): el hilo elige el ícono sin leer el texto.
ALTER TABLE [dbo].[Message] ADD [SystemKind] NVARCHAR(32) NULL;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH

-- Backfill de los System existentes por su texto (los nuevos lo traen del servidor). La columna nueva se
-- referencia por sp_executesql (no compila en el mismo lote del ALTER).
EXEC sp_executesql N'
UPDATE [dbo].[Message]
SET [SystemKind] = CASE
    WHEN [Body] IN (N''Missed call'', N''Missed video call'', N''Call'', N''Video call'', N''Call transcript is ready'')
      OR [Body] LIKE N''Call · %'' OR [Body] LIKE N''Video call · %''
      THEN N''Call''
    WHEN [Body] LIKE N''% created the group'' OR [Body] LIKE N''% added %'' OR [Body] LIKE N''% left''
      OR [Body] LIKE N''% removed %'' OR [Body] LIKE N''% an admin%'' OR [Body] LIKE N''% the group owner%''
      OR [Body] LIKE N''%is no longer with the office%'' OR [Body] LIKE N''Your preparer changed:%''
      THEN N''Membership''
    ELSE N''Notice''
  END
WHERE [Kind] = N''System'' AND [SystemKind] IS NULL;
';
