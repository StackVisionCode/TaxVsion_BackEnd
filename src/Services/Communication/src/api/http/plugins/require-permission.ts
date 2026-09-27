import type { FastifyReply, FastifyRequest } from 'fastify';
import {
  checkPermission,
  permissionCheckHttpStatus,
  type CommunicationPermission,
} from '../../../domain/shared/permissions.js';
import type { AppContainer } from '../../../infrastructure/container.js';

/**
 * A6/A5.4 — `preHandler` que exige un permiso antes de entrar al handler. Es el equivalente en
 * Fastify de `[HasPermission("...")]` del lado .NET, y se encadena despues de `app.authenticate`.
 *
 * Existe porque varias rutas HTTP de chat, llamadas y reuniones solo comprobaban el RECURSO (¿soy
 * participante de esta conversacion?) y nunca el PERMISO. Con eso, un tenant cuyo plan no incluye
 * `comms` seguia leyendo y escribiendo por HTTP: el gate de modulo cuelga de `checkPermission`, asi
 * que una ruta sin comprobacion de permiso tampoco pasa por el gate. Las dos capas son distintas y
 * ninguna sustituye a la otra — esta no reemplaza los chequeos de participacion, se suma antes.
 *
 * Devolver el error desde el `preHandler` (en vez de repetir el bloque en cada handler) quita la
 * forma de fallo mas comun de este patron: comprobar y olvidar el `return`, que deja el handler
 * ejecutandose igual despues de haber escrito un 403.
 */
export function requirePermission(container: AppContainer, permission: CommunicationPermission) {
  return async function requirePermissionPreHandler(
    request: FastifyRequest,
    reply: FastifyReply,
  ): Promise<void> {
    // `app.authenticate` corre antes y ya rechazo al no autenticado; si no hay principal es un
    // encadenado mal hecho, y fallar cerrado es lo correcto.
    const principal = request.principal;
    if (!principal) {
      await reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      return;
    }

    const result = await checkPermission(principal, permission, container.userPermissions);
    if (!result.allowed) {
      await reply
        .code(permissionCheckHttpStatus(result))
        .send({ code: result.code, message: result.message });
    }
  };
}
