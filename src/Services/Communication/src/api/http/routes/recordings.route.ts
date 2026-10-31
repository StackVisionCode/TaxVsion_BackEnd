import type { FastifyInstance, FastifyReply } from 'fastify';
import { z } from 'zod';
import {
  checkPermission,
  CommunicationPermissions,
  isStaffActor,
  type CommunicationPermission,
} from '../../../domain/shared/permissions.js';
import { RecordingScope } from '../../../domain/recording/recording-session-state.js';
import { getRecordingDetails } from '../../../application/use-cases/get-recording-details.js';
import type { AuthenticatedPrincipal } from '../../../infrastructure/jwks/jwt-verifier.js';
import { requirePermission } from '../plugins/require-permission.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const IdParams = z.object({ id: z.string().uuid() });

/**
 * F7 (contrato 9.6): ficha de la grabacion de una reunion o llamada. El permiso de entrada es el de
 * unirse/llamar (participantes y portal); el de grabar solo amplia la vista al staff que no participo.
 */
export async function registerRecordingRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get(
    '/communication/meetings/:id/recording',
    { preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingJoin)] },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      return sendDetails(reply, container, principal, RecordingScope.Meeting, IdParams.parse(request.params).id, CommunicationPermissions.MeetingRecord);
    },
  );

  app.get(
    '/communication/calls/:id/recording',
    { preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.CallStart)] },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      return sendDetails(reply, container, principal, RecordingScope.Call, IdParams.parse(request.params).id, CommunicationPermissions.CallRecord);
    },
  );
}

async function sendDetails(
  reply: FastifyReply,
  container: AppContainer,
  principal: AuthenticatedPrincipal,
  scope: RecordingScope,
  scopeId: string,
  recordPermission: CommunicationPermission,
): Promise<FastifyReply> {
  const holdsRecordPermission =
    isStaffActor(principal.actorType) && (await checkPermission(principal, recordPermission, container.userPermissions)).allowed;
  const result = await getRecordingDetails(
    {
      viewer: { tenantId: principal.tenantId, userId: principal.userId, actorType: principal.actorType, holdsRecordPermission },
      scope,
      scopeId,
    },
    container,
  );
  if (!result.isSuccess) {
    const status = result.error.code.endsWith('.NotFound') ? 404 : 403;
    return reply.code(status).send({ code: result.error.code, message: result.error.message });
  }
  return reply.send(result.value);
}
