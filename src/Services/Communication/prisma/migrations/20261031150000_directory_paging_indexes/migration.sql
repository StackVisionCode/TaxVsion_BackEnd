-- F8c (T-14): índices del rail paginado del chat (orden por nombre) y del ABAC por staff asignado.

CREATE NONCLUSTERED INDEX [UserDirectoryEntry_TenantId_IsActive_DisplayName_idx]
  ON [dbo].[UserDirectoryEntry]([TenantId], [IsActive], [DisplayName]);

CREATE NONCLUSTERED INDEX [CustomerDirectoryEntry_TenantId_IsActive_DisplayName_idx]
  ON [dbo].[CustomerDirectoryEntry]([TenantId], [IsActive], [DisplayName]);

CREATE NONCLUSTERED INDEX [CustomerAssignmentProjection_TenantId_UserId_idx]
  ON [dbo].[CustomerAssignmentProjection]([TenantId], [UserId]);
