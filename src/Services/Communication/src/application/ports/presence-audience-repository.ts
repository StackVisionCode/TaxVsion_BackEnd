export interface PresenceAudienceRepository {
  findActivePortalUserIdsForPreparer(tenantId: string, preparerUserId: string): Promise<readonly string[]>;
  // Inverso: dado el usuario del portal, devuelve los staff cuyo presencia tiene
  // derecho a conocer (preparadores asignados a cualquier customer vinculado).
  findStaffUserIdsVisibleToPortalUser(tenantId: string, portalUserId: string): Promise<readonly string[]>;
}
