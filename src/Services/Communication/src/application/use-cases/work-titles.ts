import {
  WorkResourceTypes,
  clipWorkTitle,
  type WorkTitle,
  type WorkTitleRef,
  type WorkTitlesResponse,
} from '../../contracts/http/work-http.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';

/** Quién pide los títulos; mismas señales que `GET /communication/support/:id`. */
export interface WorkTitlesActor {
  readonly userId: string;
  readonly tenantId: string;
  readonly hasAgentPermission: boolean;
  readonly isPlatformAdmin: boolean;
}

/**
 * Títulos en vivo del historial (`POST /communication/work/titles`): asunto de cada ticket que el actor podría
 * abrir (`SupportTicket.canBeAccessedBy`). Los tickets solo viven en la oficina de agentes: en otra oficina, vacío.
 * Lo inexistente, ajeno o no visible se omite; los tipos que no son de este servicio se ignoran.
 */
export async function getWorkTitles(
  input: { actor: WorkTitlesActor; isAgentTenant: boolean; items: readonly WorkTitleRef[] },
  deps: { supportTickets: Pick<SupportTicketRepository, 'findManyForAgentTenant'> },
): Promise<WorkTitlesResponse> {
  if (!input.isAgentTenant) return { items: [] };
  const ids = [
    ...new Set(
      input.items
        .filter((i) => i.resourceType === WorkResourceTypes.SupportTicket)
        .map((i) => i.id.toLowerCase()),
    ),
  ];
  if (ids.length === 0) return { items: [] };

  const { actor } = input;
  const tickets = await deps.supportTickets.findManyForAgentTenant(actor.tenantId, ids);
  const subjects = new Map<string, string>();
  for (const ticket of tickets) {
    const canRead = ticket.canBeAccessedBy({
      actorUserId: actor.userId,
      actorTenantId: actor.tenantId,
      actorHasAgentPermission: actor.hasAgentPermission,
      isPlatformAdmin: actor.isPlatformAdmin,
    });
    if (canRead) subjects.set(ticket.id.toLowerCase(), ticket.toSnapshot().subject);
  }

  const items: WorkTitle[] = [];
  for (const id of ids) {
    const title = clipWorkTitle(subjects.get(id));
    if (title) items.push({ resourceType: WorkResourceTypes.SupportTicket, id, title });
  }
  return { items };
}
