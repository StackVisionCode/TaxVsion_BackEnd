import type { FastifyInstance, FastifyReply, FastifyRequest } from 'fastify';
import fp from 'fastify-plugin';
import {
  UnauthorizedError,
  verifyAccessToken,
  type AuthenticatedPrincipal,
} from '../../../infrastructure/jwks/jwt-verifier.js';
import type { HttpRateLimiter } from '../../../infrastructure/redis/http-rate-limiter.js';
import { sendRateLimited } from '../../../infrastructure/http/rate-limit-rejection.js';

declare module 'fastify' {
  interface FastifyRequest {
    principal?: AuthenticatedPrincipal;
  }
  interface FastifyInstance {
    authenticate: (request: FastifyRequest, reply: FastifyReply) => Promise<void>;
  }
}

export const USER_HTTP_RATE_LIMIT_POLICY = 'communication.user_http';

export interface AuthPluginOptions {
  readonly httpRateLimiter: HttpRateLimiter;
  readonly userRateLimit: { readonly maxPerWindow: number; readonly windowSeconds: number };
}

/**
 * Plugin de autenticacion HTTP. Se registra como decorador `authenticate` y se
 * agrega al `preHandler` de las rutas privadas:
 *
 *   app.get('/x', { preHandler: [app.authenticate] }, handler);
 *
 * NUNCA lee actor/rol del body/query — solo del JWT firmado por Auth (JWKS).
 * Cierra CRIT-18 del legacy (`isDepartmentMember` desde query).
 *
 * Tambien aplica la cuota HTTP por usuario. Va aca y no en el onRequest global porque recien aca
 * la identidad esta verificada: con el `sub` sin verificar, cualquiera podria forjarlo y agotarle
 * el cupo a otro usuario. El limite global por IP sigue cubriendo el trafico anonimo.
 */
async function authPlugin(app: FastifyInstance, options: AuthPluginOptions): Promise<void> {
  app.decorate(
    'authenticate',
    async function authenticate(request: FastifyRequest, reply: FastifyReply): Promise<void> {
      const header = request.headers.authorization;
      if (!header || !header.startsWith('Bearer ')) {
        await reply.code(401).send({ code: 'Auth.MissingBearer', message: 'Missing Bearer token.' });
        return;
      }
      const token = header.slice('Bearer '.length).trim();
      let principal: AuthenticatedPrincipal;
      try {
        principal = await verifyAccessToken(token);
      } catch (err) {
        if (err instanceof UnauthorizedError) {
          await reply.code(401).send({ code: err.code, message: err.message });
          return;
        }
        request.log.error({ err }, 'Unexpected auth error');
        await reply.code(401).send({ code: 'Auth.InvalidToken', message: 'Access token could not be verified.' });
        return;
      }
      request.principal = principal;

      const decision = await options.httpRateLimiter.allow({
        key: `comm:rl:http.user:${principal.tenantId}:${principal.userId}`,
        policy: USER_HTTP_RATE_LIMIT_POLICY,
        maxPerWindow: options.userRateLimit.maxPerWindow,
        windowSeconds: options.userRateLimit.windowSeconds,
      });
      if (!decision.allowed) {
        await sendRateLimited(reply, decision.retryAfterSeconds, USER_HTTP_RATE_LIMIT_POLICY);
      }
    },
  );
}

export const registerAuthPlugin = fp(authPlugin, {
  name: 'communication-auth',
});
