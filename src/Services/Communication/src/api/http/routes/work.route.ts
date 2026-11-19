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
  WorkItemsKinds,
  getMemberWork,
  getMemberWorkBatch,
  getMemberWorkItems,
  officeTimeZone,
  parseWorkUserIds,
  workWindow,
  type WorkViewer,
} from '../../../application/use-cases/member-work.js';
import { getWorkTitles } from '../../../application/use-cases/work-titles.js';
import { WorkLimits } from '../../../contracts/http/work-http.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../../infrastructure/jwks/jwt-verifier.js';

const UserIdParams = z.object({ userId: z.string().uuid() });
const BatchQuery = z.object({ userIds: z.string().optional() });
const ItemsKindQuery = z.object({ kind: z.enum(WorkItemsKinds) });
// size mayor al tope se recorta a 50, como los listados del resto de servicios.
const ItemsPagingQuery = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce
    .number()
    .int()
    .min(1)
    .default(WorkLimits.ItemsPageSize)
    .transform((size) => Math.min(size, WorkLimits.ItemsMaxPageSize)),
});
const TitlesBody = z.object({
  items: z
    .array(z.object({ resourceType: z.string().min(1).max(64), id: z.string().uuid() }))
    .max(WorkLimits.TitleBatch),
});

const INVALID_USER_ID = {
  code: 'Communication.Work.InvalidUserId',
  message: 'userId must be a GUID.',
} as const;
const INVALID_KIND = {
  code: 'Communication.Work.InvalidKind',
  message: `kind must be one of: ${WorkItemsKinds.join(', ')}.`,
} as const;
const INVALID_PAGING = {
  code: 'Communication.Work.InvalidPaging',
  message: 'page and size must be positive integers.',
} as const;
const INVALID_TITLES = {
  code: 'Communication.Work.InvalidTitles',
  message: `items is required and must have at most ${WorkLimits.TitleBatch} entries with a GUID id.`,
} as const;

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
    if (!params.success) return reply.code(400).send(INVALID_USER_ID);

    const viewer = await authorizeMemberWork(principal, params.data.userId, container);
    if ('denied' in viewer) return deny(reply, viewer.denied);

    const zone = await officeTimeZone(container.tenantTimeZones, principal.tenantId);
    return reply.send(
      await getMemberWork(
        { viewer, userId: params.data.userId, window: workWindow(new Date(), zone) },
        container,
      ),
    );
  });

  // "Show all" de un bloque: misma autorización que GET /communication/work/:userId.
  app.get('/communication/work/:userId/items', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal || !isStaffActor(principal.actorType)) return reply.code(403).send(STAFF_ONLY);

    const params = UserIdParams.safeParse(request.params);
    if (!params.success) return reply.code(400).send(INVALID_USER_ID);
    const kind = ItemsKindQuery.safeParse(request.query);
    if (!kind.success) return reply.code(400).send(INVALID_KIND);
    const paging = ItemsPagingQuery.safeParse(request.query);
    if (!paging.success) return reply.code(400).send(INVALID_PAGING);

    const viewer = await authorizeMemberWork(principal, params.data.userId, container);
    if ('denied' in viewer) return deny(reply, viewer.denied);

    const result = await getMemberWorkItems(
      {
        viewer,
        userId: params.data.userId,
        kind: kind.data.kind,
        page: paging.data.page,
        size: paging.data.size,
        nowUtc: new Date(),
      },
      container,
    );
    return result.isSuccess ? reply.send(result.value) : reply.code(403).send(result.error);
  });

  // Títulos en vivo del historial: solo los tickets que quien mira podría abrir.
  app.post('/communication/work/titles', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal || !isStaffActor(principal.actorType)) return reply.code(403).send(STAFF_ONLY);

    const body = TitlesBody.safeParse(request.body);
    if (!body.success) return reply.code(400).send(INVALID_TITLES);

    const isAgentTenant = principal.tenantId === container.platform.getPlatformTenantId();
    const agent = isAgentTenant
      ? await check(principal, CommunicationPermissions.SupportAgent, container)
      : null;
    if (agent && !agent.allowed && agent.code === 'Auth.TokenStale') return deny(reply, agent);

    return reply.send(
      await getWorkTitles(
        {
          actor: {
            userId: principal.userId,
            tenantId: principal.tenantId,
            hasAgentPermission: agent?.allowed === true,
            isPlatformAdmin: isPlatformAdmin(principal.actorType),
          },
          isAgentTenant,
          items: body.data.items,
        },
        container,
      ),
    );
  });
}

/** Lo propio con la lectura del módulo; lo de otra persona exige además `users.work.view`. */
async function authorizeMemberWork(
  principal: AuthenticatedPrincipal,
  userId: string,
  container: AppContainer,
): Promise<WorkViewer | { denied: Denied }> {
  if (userId.toLowerCase() !== principal.userId.toLowerCase()) {
    const supervise = await checkPermission(
      principal,
      AuthPermissions.UsersWorkView,
      container.userPermissions,
    );
    if (!supervise.allowed) return { denied: supervise };
  }
  return resolveViewer(principal, container);
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
