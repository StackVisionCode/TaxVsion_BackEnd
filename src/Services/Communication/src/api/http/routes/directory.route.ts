import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import type { AppContainer } from '../../../infrastructure/container.js';
import { searchEmployeeDirectory } from '../../../application/use-cases/search-employee-directory.js';
import { searchCustomerDirectory } from '../../../application/use-cases/search-customer-directory.js';

const SearchQuery = z.object({
  q: z.string().min(1).max(100),
  limit: z.coerce.number().int().min(1).max(25).optional(),
});

/**
 * Fase Frontend 5 — autocomplete de employees/customers al armar invitaciones
 * de meeting (InviteToMeetingPanel). Tenant-scoped via `request.principal`.
 */
export async function registerDirectoryRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get('/communication/directory/employees', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const query = SearchQuery.parse(request.query);
    const results = await searchEmployeeDirectory(
      { tenantId: principal.tenantId, query: query.q, ...(query.limit !== undefined ? { limit: query.limit } : {}) },
      container,
    );
    return reply.send(results);
  });

  app.get('/communication/directory/customers', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const query = SearchQuery.parse(request.query);
    const results = await searchCustomerDirectory(
      { tenantId: principal.tenantId, query: query.q, ...(query.limit !== undefined ? { limit: query.limit } : {}) },
      container,
    );
    return reply.send(results);
  });
}
