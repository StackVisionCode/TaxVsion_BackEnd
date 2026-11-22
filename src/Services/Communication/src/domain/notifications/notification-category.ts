/** Categorías que llegan a la fila desde el catálogo de Notification (mismas que acepta el puente in-app). */
export const NOTIFICATION_CATEGORIES = [
  'Security',
  'Work',
  'Clients',
  'Signatures',
  'CalendarMeetings',
  'DocumentsStorage',
  'Billing',
  'Portal',
] as const;

export type NotificationCategory = (typeof NOTIFICATION_CATEGORIES)[number];

/** Filtro de la bandeja; lo comparten la lista y la acción masiva por filtro. */
export interface NotificationListFilter {
  readonly unreadOnly?: boolean;
  readonly category?: NotificationCategory;
  /** Texto ya recortado (2 a 64 caracteres). */
  readonly q?: string;
}
