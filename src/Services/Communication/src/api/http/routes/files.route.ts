import type { FastifyInstance, FastifyReply } from 'fastify';
import { z } from 'zod';
import {
  checkPermission,
  CommunicationPermissions,
  permissionCheckHttpStatus,
  type PermissionCheckResult,
  type RequiredPermission,
} from '../../../domain/shared/permissions.js';
import { getFileScope, type FileLibraryViewer } from '../../../application/use-cases/file-scope.js';
import { getVisibleFiles } from '../../../application/use-cases/file-visibility.js';
import { FilesLimits } from '../../../contracts/http/files-http.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../../infrastructure/jwks/jwt-verifier.js';

const VisibilityBody = z.object({
  fileIds: z.array(z.string().uuid()).max(FilesLimits.VisibilityBatch),
  purpose: z.string().max(32).nullish(),
});

const INVALID_BODY = {
  code: 'Communication.Files.InvalidRequest',
  message: `fileIds is required and must have at most ${FilesLimits.VisibilityBatch} GUIDs.`,
} as const;
const STAFF_ONLY = { code: 'Auth.Forbidden', message: 'The file library is for office staff only.' } as const;
const NO_ENTRY_PERMISSION = {
  code: 'Auth.Forbidden',
  message: `Missing ${CommunicationPermissions.ChatReply}, ${CommunicationPermissions.CallStart} or ${CommunicationPermissions.MeetingJoin}.`,
} as const;

type Denied = Extract<PermissionCheckResult, { allowed: false }>;

/**
 * Biblioteca de archivos: CloudStorage pregunta con el token de la persona qué archivos de Communication ve. Solo
 * personal de la oficina (sin portal ni PlatformAdmin), tenant y usuario del token. Cada rama exige su permiso: chat,
 * llamadas y reuniones. Cuota propia por usuario (auth.plugin) y fuera del límite por IP en la llamada interna.
 */
export async function registerFileRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get('/communication/files/scope', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal || !isOfficeStaff(principal.actorType)) return reply.code(403).send(STAFF_ONLY);

    const viewer = await resolveViewer(principal, container);
    if ('denied' in viewer) return deny(reply, viewer.denied);

    return reply.send(await getFileScope(viewer, container));
  });

  app.post('/communication/files/visibility', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal || !isOfficeStaff(principal.actorType)) return reply.code(403).send(STAFF_ONLY);

    const body = VisibilityBody.safeParse(request.body);
    if (!body.success) return reply.code(400).send(INVALID_BODY);

    const viewer = await resolveViewer(principal, container);
    if ('denied' in viewer) return deny(reply, viewer.denied);
    if (!viewer.canReadChat && !viewer.canReadCalls && !viewer.canReadMeetings) {
      return reply.code(403).send(NO_ENTRY_PERMISSION);
    }

    const items = await getVisibleFiles(viewer, body.data.fileIds, container);
    return reply.send({ items });
  });
}

// Personal de una oficina: el PlatformAdmin no entra (su bypass de permisos no aplica a la biblioteca).
function isOfficeStaff(actorType: string): boolean {
  return actorType === 'TenantEmployee' || actorType === 'TenantAdmin';
}

/** Permiso de entrada de cada rama (con su módulo) y el de grabar llamadas; un token viejo, 401. */
async function resolveViewer(
  principal: AuthenticatedPrincipal,
  container: AppContainer,
): Promise<FileLibraryViewer | { denied: Denied }> {
  const [chat, call, meeting, callRecord] = await Promise.all([
    check(principal, CommunicationPermissions.ChatReply, container),
    check(principal, CommunicationPermissions.CallStart, container),
    check(principal, CommunicationPermissions.MeetingJoin, container),
    check(principal, CommunicationPermissions.CallRecord, container),
  ]);
  const stale = [chat, call, meeting, callRecord].find(isStale);
  if (stale) return { denied: stale };
  return {
    tenantId: principal.tenantId,
    userId: principal.userId,
    actorType: principal.actorType,
    canReadChat: chat.allowed,
    canReadCalls: call.allowed,
    canReadMeetings: meeting.allowed,
    holdsCallRecordPermission: callRecord.allowed,
  };
}

function check(principal: AuthenticatedPrincipal, permission: RequiredPermission, container: AppContainer) {
  return checkPermission(principal, permission, container.userPermissions);
}

function isStale(result: PermissionCheckResult): result is Denied {
  return !result.allowed && result.code === 'Auth.TokenStale';
}

function deny(reply: FastifyReply, result: Denied) {
  return reply.code(permissionCheckHttpStatus(result)).send({ code: result.code, message: result.message });
}
