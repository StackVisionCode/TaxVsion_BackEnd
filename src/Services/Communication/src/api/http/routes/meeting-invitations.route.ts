import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { config } from '../../../infrastructure/config.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import { createMeetingInvitations } from '../../../application/use-cases/create-meeting-invitations.js';
import { listMeetingInvitations } from '../../../application/use-cases/list-meeting-invitations.js';
import { revokeMeetingInvitation } from '../../../application/use-cases/revoke-meeting-invitation.js';
import { resolveInvitationToken } from '../../../application/use-cases/resolve-invitation-token.js';
import { resolveMeetingByCode } from '../../../application/use-cases/resolve-meeting-by-code.js';
import type { MeetingInviteeKind } from '../../../domain/meetings/meeting-invitation.js';

const IdParams = z.object({ id: z.string().uuid() });
const InvitationIdParams = z.object({ id: z.string().uuid(), invitationId: z.string().uuid() });
const ShortCodeParams = z.object({ shortCode: z.string().min(1).max(16) });

const MeetingInviteeInputSchema = z.object({
  kind: z.enum(['employee', 'customer', 'external']),
  userId: z.string().uuid().optional(),
  email: z.string().email().optional(),
  name: z.string().min(1).max(120).optional(),
});
const CreateInvitationsBody = z.object({
  invitees: z.array(MeetingInviteeInputSchema).min(1).max(50),
});

const JoinByTokenBody = z.object({
  token: z.string().length(64),
  displayName: z.string().min(1).max(120).optional(),
});

const InviteeKindMap: Record<'employee' | 'customer' | 'external', MeetingInviteeKind> = {
  employee: 'Employee',
  customer: 'Customer',
  external: 'External',
};

/**
 * Fase Backend 5 — invitaciones a meetings. Rutas Host/Cohost (auth normal,
 * `app.authenticate`) + 2 rutas PUBLICAS sin JWT para el flujo de guest
 * (join-by-token / by-code), rate-limited via `config.rateLimit` per-route
 * (@fastify/rate-limit ya esta registrado global en build-server.ts; el
 * override per-route aca es mas estricto porque son endpoints sin auth,
 * sensibles a token-guessing). F11 QA gap: hasta ahora estos dos limites eran
 * literales inline (5/20 por minuto) pese a lo que decia este docblock —
 * ahora sí salen de `config.rateLimit.meetingJoinByToken/ByCode`.
 */
export async function registerMeetingInvitationRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.post('/communication/meetings/:id/invitations', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const body = CreateInvitationsBody.parse(request.body);

    const result = await createMeetingInvitations(
      {
        tenantId: principal.tenantId,
        correlationId: request.id,
        meetingId: params.id,
        actorUserId: principal.userId,
        invitees: body.invitees.map((invitee) => ({
          kind: InviteeKindMap[invitee.kind],
          ...(invitee.userId !== undefined ? { userId: invitee.userId } : {}),
          ...(invitee.email !== undefined ? { email: invitee.email } : {}),
          ...(invitee.name !== undefined ? { name: invitee.name } : {}),
        })),
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.code(201).send(result.value);
  });

  app.get('/communication/meetings/:id/invitations', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const result = await listMeetingInvitations(
      { tenantId: principal.tenantId, meetingId: params.id, actorUserId: principal.userId },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.send(result.value);
  });

  app.delete(
    '/communication/meetings/:id/invitations/:invitationId',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const principal = request.principal!;
      const params = InvitationIdParams.parse(request.params);
      const result = await revokeMeetingInvitation(
        {
          tenantId: principal.tenantId,
          meetingId: params.id,
          invitationId: params.invitationId,
          actorUserId: principal.userId,
        },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      return reply.code(204).send();
    },
  );

  // ---------- Publicas — sin app.authenticate, sin request.principal ----------

  app.post(
    '/communication/meetings/join-by-token',
    {
      config: {
        rateLimit: {
          max: config.rateLimit.meetingJoinByToken.maxPerWindow,
          timeWindow: `${config.rateLimit.meetingJoinByToken.windowSeconds} seconds`,
        },
      },
    },
    async (request, reply) => {
      const body = JoinByTokenBody.parse(request.body);
      const result = await resolveInvitationToken(
        { token: body.token, ...(body.displayName !== undefined ? { displayName: body.displayName } : {}) },
        container,
      );
      if (!result.isSuccess) {
        // Anti-enumeracion: siempre 404, sin distinguir revoked/used/expired/not-found.
        return reply.code(404).send({ code: 'Meeting.Invitation.NotFound', message: 'Invitation not found or no longer valid.' });
      }
      return reply.send(result.value);
    },
  );

  app.get(
    '/communication/meetings/by-code/:shortCode',
    {
      config: {
        rateLimit: {
          max: config.rateLimit.meetingJoinByCode.maxPerWindow,
          timeWindow: `${config.rateLimit.meetingJoinByCode.windowSeconds} seconds`,
        },
      },
    },
    async (request, reply) => {
      const params = ShortCodeParams.parse(request.params);
      const result = await resolveMeetingByCode({ shortCode: params.shortCode }, container);
      if (!result.isSuccess) {
        return reply.code(404).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );
}
