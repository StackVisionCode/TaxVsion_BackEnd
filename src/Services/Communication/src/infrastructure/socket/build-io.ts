import { Server as SocketIoServer, type Socket } from 'socket.io';
import { createAdapter } from '@socket.io/redis-adapter';
import type { Server as HttpServer } from 'node:http';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import { redisPub, redisSub } from '../redis/redis-client.js';
import {
  UnauthorizedError,
  verifyAccessToken,
  type AuthenticatedPrincipal,
} from '../jwks/jwt-verifier.js';

/**
 * `SocketData` es el tercer generic de `Server<S2C, C2S, IE, SocketData>` en
 * Socket.IO. Le pasamos el principal autenticado — Socket.IO garantiza que
 * `socket.data` tenga ese shape. Nunca leemos rol/tenant del handshake.query.
 * Cierra CRIT-18 del legacy.
 */
export interface CommunicationSocketData {
  principal?: AuthenticatedPrincipal;
}

// Los eventos concretos se declaran en cada handler; a nivel de tipo del
// Server aceptamos payloads arbitrarios (validados por Zod en runtime) para
// no acoplar el tipo generico Socket.IO a cada evento del dominio.
export type ServerToClientEvents = Record<string, (...args: unknown[]) => void>;
export type ClientToServerEvents = Record<string, (...args: unknown[]) => void>;
export type InterServerEvents = Record<string, (...args: unknown[]) => void>;

export type CommunicationSocket = Socket<
  ClientToServerEvents,
  ServerToClientEvents,
  InterServerEvents,
  CommunicationSocketData
>;
export type CommunicationIoServer = SocketIoServer<
  ClientToServerEvents,
  ServerToClientEvents,
  InterServerEvents,
  CommunicationSocketData
>;

/**
 * Construye Socket.IO montado en el mismo servidor HTTP (Fastify subyacente),
 * con Redis adapter (cierre CRIT-5) y auth middleware JWKS.
 * Path fijo `/communication/socket.io` — coincide con la ruta YARP del Gateway.
 */
export function buildSocketServer(httpServer: HttpServer): CommunicationIoServer {
  const io: CommunicationIoServer = new SocketIoServer<
    ClientToServerEvents,
    ServerToClientEvents,
    InterServerEvents,
    CommunicationSocketData
  >(httpServer, {
    path: '/communication/socket.io',
    transports: ['websocket', 'polling'],
    cors: {
      origin: config.cors.origins.length === 0 ? true : config.cors.origins,
      credentials: true,
    },
    // Rechazar frames enormes desde el cliente (chat max ~4 KB por mensaje, WebRTC
    // signaling ~8 KB; damos margen sin permitir DoS).
    maxHttpBufferSize: 1024 * 32,
  });

  io.adapter(createAdapter(redisPub, redisSub));
  logger.info('Socket.IO Redis adapter attached');

  io.use(async (socket, next) => {
    // Aceptamos el token SOLO desde handshake.auth.token, nunca desde query
    // string (fuga en logs) — misma politica que el design doc.
    const token = (socket.handshake.auth?.['token'] ?? '').toString().trim();
    if (!token) {
      next(new Error('Auth.MissingToken'));
      return;
    }
    try {
      const principal = await verifyAccessToken(token);
      socket.data.principal = principal;
      // Room de tenant + de usuario para broadcasts dirigidos.
      await socket.join(`t:${principal.tenantId}`);
      await socket.join(`t:${principal.tenantId}:u:${principal.userId}`);
      next();
    } catch (err) {
      if (err instanceof UnauthorizedError) {
        next(new Error(err.code));
        return;
      }
      logger.error({ err }, 'Unexpected socket auth error');
      next(new Error('Auth.InvalidToken'));
    }
  });

  io.on('connection', (socket) => {
    const p = socket.data.principal;
    if (!p) {
      socket.disconnect(true);
      return;
    }
    logger.info(
      { sid: socket.id, userId: p.userId, tenantId: p.tenantId, actorType: p.actorType },
      'Socket connected',
    );
    socket.on('disconnect', (reason) => {
      logger.info({ sid: socket.id, userId: p.userId, reason }, 'Socket disconnected');
    });
  });

  return io;
}
