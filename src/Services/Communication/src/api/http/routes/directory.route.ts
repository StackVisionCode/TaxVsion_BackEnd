import type { FastifyInstance, FastifyReply, FastifyRequest } from 'fastify';
import { z } from 'zod';
import { CommunicationPermissions } from '../../../domain/shared/permissions.js';
import { requirePermission } from '../plugins/require-permission.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import { searchEmployeeDirectory } from '../../../application/use-cases/search-employee-directory.js';
import { searchCustomerDirectory } from '../../../application/use-cases/search-customer-directory.js';
import { isStaffActor } from '../../../domain/shared/permissions.js';

const SearchQuery = z.object({
  q: z.string().min(1).max(100),
  limit: z.coerce.number().int().min(1).max(25).optional(),
});

const STAFF_ONLY = { code: 'Auth.Forbidden', message: 'Directory search is staff-only.' } as const;
// Sin eco del valor recibido: el término no debe volver en errores ni en logs.
const INVALID_SEARCH = {
  code: 'Directory.InvalidSearch',
  message: 'q is required (1-100 characters); limit must be 1-25.',
} as const;

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
  app.get(
    '/communication/directory/employees',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatStart)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      if (!isStaffActor(principal.actorType)) return reply.code(403).send(STAFF_ONLY);
      const query = SearchQuery.parse(request.query);
      const results = await searchEmployeeDirectory(
        {
          tenantId: principal.tenantId,
          query: query.q,
          ...(query.limit !== undefined ? { limit: query.limit } : {}),
        },
        container,
      );
      return reply.send(results);
    },
  );

  // Misma puerta y misma lógica para las dos variantes; solo cambia de dónde sale el término.
  const customerSearchOptions = {
    preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatStart)],
  };
  const searchCustomers = async (input: unknown, request: FastifyRequest, reply: FastifyReply) => {
    const principal = request.principal!;
    if (!isStaffActor(principal.actorType)) return reply.code(403).send(STAFF_ONLY);
    const parsed = SearchQuery.safeParse(input ?? {});
    if (!parsed.success) return reply.code(400).send(INVALID_SEARCH);
    const results = await searchCustomerDirectory(
      {
        tenantId: principal.tenantId,
        query: parsed.data.q,
        actorUserId: principal.userId,
        actorType: principal.actorType,
        ...(parsed.data.limit !== undefined ? { limit: parsed.data.limit } : {}),
      },
      container,
    );
    return reply.send(results);
  };

  // Solo compatibilidad durante el despliegue: el término viaja en la URL y acaba en los logs.
  app.get('/communication/directory/customers', customerSearchOptions, (request, reply) =>
    searchCustomers(request.query, request, reply),
  );

  // Variante que usan los clientes propios: el término (a menudo un email) va en el cuerpo.
  app.post('/communication/directory/customers/search', customerSearchOptions, (request, reply) =>
    searchCustomers(request.body, request, reply),
  );
}
