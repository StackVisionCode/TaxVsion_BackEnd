import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import type { AppContainer } from '../../../infrastructure/container.js';
import { searchEmployeeDirectory } from '../../../application/use-cases/search-employee-directory.js';
import { searchCustomerDirectory } from '../../../application/use-cases/search-customer-directory.js';
import { isStaffActor } from '../../../domain/shared/permissions.js';

const SearchQuery = z.object({
  q: z.string().min(1).max(100),
  limit: z.coerce.number().int().min(1).max(25).optional(),
});

const STAFF_ONLY = { code: 'Auth.Forbidden', message: 'Directory search is staff-only.' } as const;

/**
 * Fase Frontend 5 — autocomplete de employees/customers al armar invitaciones
 * de meeting (InviteToMeetingPanel). Tenant-scoped via `request.principal`.
 *
 * Solo staff: `CustomerPortal`/`Guest` quedan fuera con 403. Antes estaba solo
 * `authenticate`, asi que un cliente podia enumerar la plantilla y la cartera de
 * clientes del tenant. El cliente resuelve a su preparador por otra via (la
 * conversacion ya sembrada), no por este autocomplete abierto.
 */
export async function registerDirectoryRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get('/communication/directory/employees', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    if (!isStaffActor(principal.actorType)) return reply.code(403).send(STAFF_ONLY);
    const query = SearchQuery.parse(request.query);
    const results = await searchEmployeeDirectory(
      { tenantId: principal.tenantId, query: query.q, ...(query.limit !== undefined ? { limit: query.limit } : {}) },
      container,
    );
    return reply.send(results);
  });

  app.get('/communication/directory/customers', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    if (!isStaffActor(principal.actorType)) return reply.code(403).send(STAFF_ONLY);
    const query = SearchQuery.parse(request.query);
    const results = await searchCustomerDirectory(
      { tenantId: principal.tenantId, query: query.q, ...(query.limit !== undefined ? { limit: query.limit } : {}) },
      container,
    );
    return reply.send(results);
  });
}
