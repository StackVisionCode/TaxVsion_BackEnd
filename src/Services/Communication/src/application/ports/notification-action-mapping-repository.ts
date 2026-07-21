/**
 * Regla de accion (deep-link o ninguna) para una notificacion in-app, indexada por
 * (EventKey, AudienceRole). Fase 6 del plan de notificaciones dinamicas — mismo espiritu
 * que EventTemplateMapping de Scribe, ver docblock del modelo Prisma.
 */
export type NotificationActionType = 'DeepLink' | 'None';

export interface NotificationActionMappingSnapshot {
  readonly id: string;
  readonly eventKey: string;
  readonly audienceRole: string;
  readonly actionType: NotificationActionType;
  readonly urlTemplate: string | null;
}

export interface NotificationActionMappingRepository {
  findByEventKeyAndAudienceRole(
    eventKey: string,
    audienceRole: string,
  ): Promise<NotificationActionMappingSnapshot | null>;

  list(): Promise<readonly NotificationActionMappingSnapshot[]>;

  create(input: {
    eventKey: string;
    audienceRole: string;
    actionType: NotificationActionType;
    urlTemplate: string | null;
  }): Promise<NotificationActionMappingSnapshot>;

  update(
    id: string,
    input: { actionType: NotificationActionType; urlTemplate: string | null },
  ): Promise<NotificationActionMappingSnapshot | null>;
}
