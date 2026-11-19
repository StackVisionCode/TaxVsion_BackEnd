import type { FastifyInstance, FastifyReply } from 'fastify';
import { z } from 'zod';
import {
  AuthPermissions,
  CommunicationPermissions,
  checkPermission,
  isPlatformAdmin,
  isStaffActor,
  permissionCheckHttpStatus,
  type PermissionCheckResult,
  type RequiredPermission,
} from '../../../domain/shared/permissions.js';
import {
  getMemberWork,
  getMemberWorkBatch,
  officeTimeZone,
  parseWorkUserIds,
  workWindow,
  type WorkViewer,
} from '../../../application/use-cases/member-work.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../../infrastructure/jwks/jwt-verifier.js';

const UserIdParams = z.object({ userId: z.string().uuid() });
const BatchQuery = z.object({ userIds: z.string().optional() });

const STAFF_ONLY = { code: 'Auth.Forbidden', message: 'Work profile is staff-only.' } as const;
const NO_MODULE_READ = {
  code: 'Auth.Forbidden',
  message: `Missing ${CommunicationPermissions.MeetingJoin} or ${CommunicationPermissions.SupportAgent}.`,
} as const;

type Denied = Extract<PermissionCheckResult, { allowed: false }>;

/**
 * Work Profile (proveedor `communication`): tickets de soporte asignados y reuniones próximas. Lo propio con el
 * permiso de lectura de cada módulo; lo de otra persona y el lote exigen además `users.work.view`. Siempre el
 * tenant del JWT; la cuota HTTP por usuario la aplica `authenticate`.
 */
export async function registerWorkRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get('/communication/work/summary', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal || !isStaffActor(principal.actorType)) return reply.code(403).send(STAFF_ONLY);

    const supervise = await checkPermission(
      principal,
      AuthPermissions.UsersWorkView,
      container.userPermissions,
    );
    if (!supervise.allowed) return deny(reply, supervise);

    const viewer = await resolveViewer(principal, container);
    if ('denied' in viewer) return deny(reply, viewer.denied);

    const ids = parseWorkUserIds(BatchQuery.parse(request.query).userIds);
    if (!ids.isSuccess) return reply.code(400).send(ids.error);

    const zone = await officeTimeZone(container.tenantTimeZones, principal.tenantId);
    return reply.send(
      await getMemberWorkBatch(
        { viewer, userIds: ids.value, window: workWindow(new Date(), zone) },
        container,
      ),
    );
  });

  app.get('/communication/work/:userId', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal || !isStaffActor(principal.actorType)) return reply.code(403).send(STAFF_ONLY);

    const params = UserIdParams.safeParse(request.params);
    if (!params.success)
      return reply
        .code(400)
        .send({ code: 'Communication.Work.InvalidUserId', message: 'userId must be a GUID.' });

    if (params.data.userId.toLowerCase() !== principal.userId.toLowerCase()) {
      const supervise = await checkPermission(
        principal,
        AuthPermissions.UsersWorkView,
        container.userPermissions,
      );
      if (!supervise.allowed) return deny(reply, supervise);
    }

    const viewer = await resolveViewer(principal, container);
    if ('denied' in viewer) return deny(reply, viewer.denied);

    const zone = await officeTimeZone(container.tenantTimeZones, principal.tenantId);
    return reply.send(
      await getMemberWork(
        { viewer, userId: params.data.userId, window: workWindow(new Date(), zone) },
        container,
      ),
    );
  });
}

/**
 * Qué módulos puede leer el que mira. Soporte solo existe en la oficina de agentes (plataforma), igual que la
 * vista de agente de `GET /communication/support`. Sin ninguno, 403; un token viejo, 401.
 */
async function resolveViewer(
  principal: AuthenticatedPrincipal,
  container: AppContainer,
): Promise<WorkViewer | { denied: Denied }> {
  const isAgentTenant = principal.tenantId === container.platform.getPlatformTenantId();
  const [meetings, support] = await Promise.all([
    check(principal, CommunicationPermissions.MeetingJoin, container),
    isAgentTenant
      ? check(principal, CommunicationPermissions.SupportAgent, container)
      : Promise.resolve(null),
  ]);
  const stale = [meetings, support].find(
    (r): r is Denied => !!r && !r.allowed && r.code === 'Auth.TokenStale',
  );
  if (stale) return { denied: stale };

  const canReadMeetings = meetings.allowed;
  const canReadSupport = support?.allowed === true;
  if (!canReadMeetings && !canReadSupport) return { denied: { allowed: false, ...NO_MODULE_READ } };
  return {
    userId: principal.userId,
    tenantId: principal.tenantId,
    isPlatformAdmin: isPlatformAdmin(principal.actorType),
    canReadMeetings,
    canReadSupport,
  };
}

function check(principal: AuthenticatedPrincipal, permission: RequiredPermission, container: AppContainer) {
  return checkPermission(principal, permission, container.userPermissions);
}

function deny(reply: FastifyReply, result: Denied) {
  return reply.code(permissionCheckHttpStatus(result)).send({ code: result.code, message: result.message });
}
