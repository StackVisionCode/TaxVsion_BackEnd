import type { NotificationActionMappingRepository } from '../../application/ports/notification-action-mapping-repository.js';

/**
 * Fase 6 del plan de notificaciones dinamicas — filas iniciales reales de
 * `NotificationActionMapping`. Idempotente: se salta cada combinacion que ya exista, para
 * poder correr en cada arranque sin duplicar ni pisar una fila que un admin ya edito via
 * el CRUD (`PUT /communication/admin/notification-action-mappings/:id`).
 */
const DEFAULT_MAPPINGS = [
  {
    eventKey: 'signature.document.signed.v1',
    audienceRole: 'Preparer',
    actionType: 'DeepLink' as const,
    urlTemplate: '/crm/firmas/{signatureRequestId}',
  },
  {
    eventKey: 'signature.document.signed.v1',
    audienceRole: 'CustomerSigner',
    actionType: 'None' as const,
    urlTemplate: null,
  },
];

export async function seedDefaultNotificationActionMappings(
  mappings: NotificationActionMappingRepository,
): Promise<void> {
  for (const mapping of DEFAULT_MAPPINGS) {
    const existing = await mappings.findByEventKeyAndAudienceRole(mapping.eventKey, mapping.audienceRole);
    if (existing) continue;
    await mappings.create(mapping);
  }
}
