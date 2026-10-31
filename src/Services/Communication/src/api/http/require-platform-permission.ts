import {
  checkPermission,
  isPlatformAdmin,
  permissionCheckHttpStatus,
  type CommunicationPermission,
} from '../../domain/shared/permissions.js';
import type { AuthenticatedPrincipal } from '../../infrastructure/jwks/jwt-verifier.js';
import type { AppContainer } from '../../infrastructure/container.js';

/** Respuesta a enviar cuando el actor no pasa; null = pasa. */
export interface PlatformDenial {
  readonly status: number;
  readonly body: { readonly code: string; readonly message: string };
}

/**
 * Rutas PlatformOnly: el actor debe ser PlatformAdmin (no basta el bypass genérico) y tener el código
 * del catálogo — equivalente a `[AllowActorTypes(PlatformAdmin)]` + `[HasPermission]` en .NET.
 *
 * Devuelve la decisión, nunca el `reply`: `FastifyReply` es thenable y un `await` sobre un helper
 * async que lo devuelve resuelve a `undefined`, así que la ruta seguía ejecutándose tras el 403.
 * Uso: `if (denied) return reply.code(denied.status).send(denied.body);`
 */
export async function requirePlatformPermission(
  principal: AuthenticatedPrincipal,
  required: CommunicationPermission,
  container: Pick<AppContainer, 'userPermissions'>,
): Promise<PlatformDenial | null> {
  if (!isPlatformAdmin(principal.actorType)) {
    return { status: 403, body: { code: 'Auth.PlatformOnly', message: 'Only platform administrators can do this.' } };
  }
  const permCheck = await checkPermission(principal, required, container.userPermissions);
  if (!permCheck.allowed) {
    return { status: permissionCheckHttpStatus(permCheck), body: { code: permCheck.code, message: permCheck.message } };
  }
  return null;
}
