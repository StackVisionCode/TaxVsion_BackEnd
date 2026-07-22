import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import {
  hasPermission,
  CommunicationPermissions,
  isPlatformAdmin as isPlatformAdminActorType,
} from '../../../domain/shared/permissions.js';
import { openSupportTicket } from '../../../application/use-cases/open-support-ticket.js';
import {
  claimSupportTicket,
  closeSupportTicket,
  resolveSupportTicket,
  reassignSupportTicket,
  escalateSupportTicket,
  reopenSupportTicket,
} from '../../../application/use-cases/support-actions.js';
import {
  listSupportTicketsForAgent,
  listSupportTicketsForCustomer,
} from '../../../application/use-cases/support-queries.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const OpenBody = z.object({
  subject: z.string().min(1).max(200),
  category: z.enum(['Billing', 'Technical', 'Account', 'Other']).default('Other'),
  priority: z.enum(['Low', 'Normal', 'High', 'Urgent']).default('Normal'),
  initialMessage: z.string().max(4000).optional(),
});

const ReassignBody = z.object({ newAgentUserId: z.string().uuid() });
const EscalateBody = z.object({ newPriority: z.enum(['Low', 'Normal', 'High', 'Urgent']) });
const ReopenBody = z.object({ reason: z.string().max(500).optional() });

const ListQuery = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
  includeClosed: z.coerce.boolean().optional(),
  view: z.enum(['customer', 'agent']).optional(),
  mine: z.coerce.boolean().optional(),
});

const IdParams = z.object({ id: z.string().uuid() });

export async function registerSupportRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  // POST /communication/support — el customer (o TenantEmployee) abre un ticket.
  app.post('/communication/support', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    if (!hasPermission(principal.actorType, principal.permissions, CommunicationPermissions.SupportOpen)) {
      return reply.code(403).send({ code: 'Auth.Forbidden', message: 'Missing communication.support.open.' });
    }
    if (principal.tenantId === container.platform.getPlatformTenantId()) {
      return reply
        .code(400)
        .send({ code: 'Support.PlatformCannotOpen', message: 'Platform users open tickets from a customer tenant only.' });
    }
    const body = OpenBody.parse(request.body);
    const result = await openSupportTicket(
      {
        tenantId: principal.tenantId,
        correlationId: request.id,
        opener: { userId: principal.userId, displayName: principal.userId, actorType: principal.actorType },
        subject: body.subject,
        category: body.category,
        priority: body.priority,
        ...(body.initialMessage !== undefined ? { initialMessage: body.initialMessage } : {}),
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.code(201).send(result.value);
  });

  // GET /communication/support — view depende de si el actor es agente o customer.
  app.get('/communication/support', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const query = ListQuery.parse(request.query);
    const isPlatformTenant = principal.tenantId === container.platform.getPlatformTenantId();
    const hasAgentPerm = hasPermission(
      principal.actorType,
      principal.permissions,
      CommunicationPermissions.SupportAgent,
    );
    const view =
      query.view ?? (isPlatformTenant && (hasAgentPerm || isPlatformAdminActorType(principal.actorType)) ? 'agent' : 'customer');

    if (view === 'agent') {
      if (!isPlatformTenant || (!hasAgentPerm && !isPlatformAdminActorType(principal.actorType))) {
        return reply.code(403).send({ code: 'Auth.Forbidden', message: 'Missing communication.support.agent.' });
      }
      const result = await listSupportTicketsForAgent(
        {
          agentTenantId: container.platform.getPlatformTenantId(),
          assignedAgentId: query.mine === true ? principal.userId : null,
          page: query.page,
          size: query.size,
          ...(query.includeClosed !== undefined ? { includeClosed: query.includeClosed } : {}),
        },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    }

    const result = await listSupportTicketsForCustomer(
      {
        tenantId: principal.tenantId,
        openedByUserId: principal.userId,
        page: query.page,
        size: query.size,
        ...(query.includeClosed !== undefined ? { includeClosed: query.includeClosed } : {}),
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.send(result.value);
  });

  // POST /communication/support/:id/claim — agente reclama un ticket Open.
  app.post('/communication/support/:id/claim', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const hasAgentPerm = hasPermission(
      principal.actorType,
      principal.permissions,
      CommunicationPermissions.SupportAgent,
    );
    const isPlatformAdmin = isPlatformAdminActorType(principal.actorType);
    const result = await claimSupportTicket(
      {
        correlationId: request.id,
        ticketId: params.id,
        agent: {
          userId: principal.userId,
          tenantId: principal.tenantId,
          hasAgentPermission: hasAgentPerm,
          isPlatformAdmin,
        },
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(result.error.code === 'Auth.Forbidden' ? 403 : 400).send({
        code: result.error.code,
        message: result.error.message,
      });
    }
    return reply.send(result.value);
  });

  // POST /communication/support/:id/resolve — cualquiera de las dos partes.
  app.post('/communication/support/:id/resolve', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const result = await resolveSupportTicket(
      {
        correlationId: request.id,
        ticketId: params.id,
        actor: {
          userId: principal.userId,
          tenantId: principal.tenantId,
          hasAgentPermission: hasPermission(
            principal.actorType,
            principal.permissions,
            CommunicationPermissions.SupportAgent,
          ),
          isPlatformAdmin: isPlatformAdminActorType(principal.actorType),
        },
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(result.error.code === 'Auth.Forbidden' ? 403 : 400).send({
        code: result.error.code,
        message: result.error.message,
      });
    }
    return reply.send(result.value);
  });

  // POST /communication/support/:id/reassign — agente o PlatformAdmin reasigna a otro agente.
  app.post('/communication/support/:id/reassign', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const body = ReassignBody.parse(request.body);
    const hasAgentPerm = hasPermission(
      principal.actorType,
      principal.permissions,
      CommunicationPermissions.SupportAgent,
    );
    const isPlatformAdmin = isPlatformAdminActorType(principal.actorType);
    const result = await reassignSupportTicket(
      {
        correlationId: request.id,
        ticketId: params.id,
        actor: {
          userId: principal.userId,
          tenantId: principal.tenantId,
          hasAgentPermission: hasAgentPerm,
          isPlatformAdmin,
        },
        newAgentUserId: body.newAgentUserId,
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(result.error.code === 'Auth.Forbidden' ? 403 : 400).send({
        code: result.error.code,
        message: result.error.message,
      });
    }
    return reply.send(result.value);
  });

  // POST /communication/support/:id/escalate — agente o PlatformAdmin cambia prioridad.
  app.post('/communication/support/:id/escalate', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const body = EscalateBody.parse(request.body);
    const hasAgentPerm = hasPermission(
      principal.actorType,
      principal.permissions,
      CommunicationPermissions.SupportAgent,
    );
    const isPlatformAdmin = isPlatformAdminActorType(principal.actorType);
    const result = await escalateSupportTicket(
      {
        correlationId: request.id,
        ticketId: params.id,
        actor: {
          userId: principal.userId,
          tenantId: principal.tenantId,
          hasAgentPermission: hasAgentPerm,
          isPlatformAdmin,
        },
        newPriority: body.newPriority,
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(result.error.code === 'Auth.Forbidden' ? 403 : 400).send({
        code: result.error.code,
        message: result.error.message,
      });
    }
    return reply.send(result.value);
  });

  // POST /communication/support/:id/reopen — opener, agente asignado o PlatformAdmin.
  app.post('/communication/support/:id/reopen', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const body = ReopenBody.safeParse(request.body);
    const result = await reopenSupportTicket(
      {
        correlationId: request.id,
        ticketId: params.id,
        actor: {
          userId: principal.userId,
          tenantId: principal.tenantId,
          isPlatformAdmin: isPlatformAdminActorType(principal.actorType),
        },
        reason: body.success ? body.data.reason ?? null : null,
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(result.error.code === 'Auth.Forbidden' ? 403 : 400).send({
        code: result.error.code,
        message: result.error.message,
      });
    }
    return reply.send(result.value);
  });

  // POST /communication/support/:id/close — opener, agente asignado o PlatformAdmin.
  app.post('/communication/support/:id/close', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const result = await closeSupportTicket(
      {
        correlationId: request.id,
        ticketId: params.id,
        actor: {
          userId: principal.userId,
          tenantId: principal.tenantId,
          hasAgentPermission: hasPermission(
            principal.actorType,
            principal.permissions,
            CommunicationPermissions.SupportAgent,
          ),
          isPlatformAdmin: isPlatformAdminActorType(principal.actorType),
        },
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(result.error.code === 'Auth.Forbidden' ? 403 : 400).send({
        code: result.error.code,
        message: result.error.message,
      });
    }
    return reply.send(result.value);
  });
}
