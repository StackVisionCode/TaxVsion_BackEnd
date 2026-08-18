import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { config } from '../../../infrastructure/config.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import { CommunicationRateLimitPolicyNames } from '../../../domain/rate-limit/rate-limit-policies.js';
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
 * (join-by-token / by-code). RateLimit Fase 7: ademas del gate generico por IP
 * que ya corre para toda la app (build-server.ts, `onRequest`), estas 2 rutas
 * llevan su propio gate atomico particionado por token/shortCode (no por IP) —
 * mas correcto contra token-guessing distribuido desde muchas IPs distintas.
 * Nombres de politica en `rate-limit-policies.ts`
 * (communication.d.meeting_join_by_token/by_code), cuotas en
 * `config.rateLimit.meetingJoinByToken/ByCode`.
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
      preHandler: async (request, reply) => {
        const body = JoinByTokenBody.parse(request.body);
        const allowed = await container.httpRateLimiter.allow({
          key: `comm:rl:${CommunicationRateLimitPolicyNames.MeetingJoinByToken}:${body.token}`,
          policy: CommunicationRateLimitPolicyNames.MeetingJoinByToken,
          maxPerWindow: config.rateLimit.meetingJoinByToken.maxPerWindow,
          windowSeconds: config.rateLimit.meetingJoinByToken.windowSeconds,
        });
        if (!allowed) {
          return reply
            .code(429)
            .header('Retry-After', String(config.rateLimit.meetingJoinByToken.windowSeconds))
            .send({ code: 'RateLimit.Exceeded', message: 'Too many requests.' });
        }
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
      preHandler: async (request, reply) => {
        const params = ShortCodeParams.parse(request.params);
        const allowed = await container.httpRateLimiter.allow({
          key: `comm:rl:${CommunicationRateLimitPolicyNames.MeetingJoinByCode}:${params.shortCode}`,
          policy: CommunicationRateLimitPolicyNames.MeetingJoinByCode,
          maxPerWindow: config.rateLimit.meetingJoinByCode.maxPerWindow,
          windowSeconds: config.rateLimit.meetingJoinByCode.windowSeconds,
        });
        if (!allowed) {
          return reply
            .code(429)
            .header('Retry-After', String(config.rateLimit.meetingJoinByCode.windowSeconds))
            .send({ code: 'RateLimit.Exceeded', message: 'Too many requests.' });
        }
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
