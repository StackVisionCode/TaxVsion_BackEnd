import type { FastifyInstance, FastifyReply, FastifyRequest } from 'fastify';
import fp from 'fastify-plugin';
import {
  UnauthorizedError,
  verifyAccessToken,
  type AuthenticatedPrincipal,
} from '../../../infrastructure/jwks/jwt-verifier.js';

declare module 'fastify' {
  interface FastifyRequest {
    principal?: AuthenticatedPrincipal;
  }
  interface FastifyInstance {
    authenticate: (request: FastifyRequest, reply: FastifyReply) => Promise<void>;
  }
}

/**
 * Plugin de autenticacion HTTP. Se registra como decorador `authenticate` y se
 * agrega al `preHandler` de las rutas privadas:
 *
 *   app.get('/x', { preHandler: [app.authenticate] }, handler);
 *
 * NUNCA lee actor/rol del body/query — solo del JWT firmado por Auth (JWKS).
 * Cierra CRIT-18 del legacy (`isDepartmentMember` desde query).
 */
async function authPlugin(app: FastifyInstance): Promise<void> {
  app.decorate(
    'authenticate',
    async function authenticate(request: FastifyRequest, reply: FastifyReply): Promise<void> {
      const header = request.headers.authorization;
      if (!header || !header.startsWith('Bearer ')) {
        await reply.code(401).send({ code: 'Auth.MissingBearer', message: 'Missing Bearer token.' });
        return;
      }
      const token = header.slice('Bearer '.length).trim();
      try {
        const principal = await verifyAccessToken(token);
        request.principal = principal;
      } catch (err) {
        if (err instanceof UnauthorizedError) {
          await reply.code(401).send({ code: err.code, message: err.message });
          return;
        }
        request.log.error({ err }, 'Unexpected auth error');
        await reply.code(401).send({ code: 'Auth.InvalidToken', message: 'Access token could not be verified.' });
      }
    },
  );
}

export const registerAuthPlugin = fp(authPlugin, {
  name: 'communication-auth',
});
