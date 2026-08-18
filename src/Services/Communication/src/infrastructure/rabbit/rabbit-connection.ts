import amqplib, { type Channel, type Connection } from 'amqplib';
import type { EventEmitter } from 'node:events';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';

/**
 * Conexion durable a RabbitMQ. Comunicacion:
 *   - PUBLICA al exchange fanout taxvision-events (usando outbox worker).
 *   - CONSUME de la cola communication-events (bind al mismo exchange).
 *
 * La estrategia de reconnect es exponencial con log estructurado; el proceso
 * NO se cae por perder Rabbit — reintenta hasta reconectar.
 */
export interface RabbitContext {
  connection: Connection;
  channel: Channel;
}

let context: RabbitContext | undefined;

/**
 * H-15 — cooldowns de reintento, en milisegundos. Son los mismos tres que aplican los 17
 * servicios .NET (`WolverineFailurePolicies`), y ese es justamente el punto: hasta ahora un
 * handler que fallaba en .NET tenia 4 intentos y en Node exactamente 1, sin que nadie hubiera
 * elegido ninguna de las dos cifras — cada lado heredo el default de su libreria.
 */
export const RETRY_COOLDOWNS_MS = [1_000, 5_000, 15_000] as const;

/** Cabecera con el numero de intento ya consumido. La pone este servicio al reencolar. */
export const ATTEMPT_HEADER = 'x-taxvision-attempt';

/** Cola de espera del intento n (1-based). Se deriva del nombre de la cola principal. */
export function retryQueueName(attempt: number): string {
  return `${config.rabbitmq.queue}.retry.${attempt}`;
}

async function tryConnect(): Promise<RabbitContext> {
  // Pass an object so amqplib skips url-parse and uses decoded credentials verbatim.
  const parsed = new URL(config.rabbitmq.uri);
  const connection = await amqplib.connect({
    protocol:  parsed.protocol.replace(':', ''),
    hostname:  parsed.hostname,
    port:      parsed.port ? Number(parsed.port) : 5672,
    username:  decodeURIComponent(parsed.username),
    password:  decodeURIComponent(parsed.password),
    vhost:     parsed.pathname && parsed.pathname !== '/'
                 ? decodeURIComponent(parsed.pathname.slice(1))
                 : '/',
    frameMax:  131072, // RabbitMQ 4.x requires >= 8192; amqplib default 4096 is rejected
  });
  const channel = await connection.createChannel();
  await channel.assertExchange(config.rabbitmq.exchange, 'fanout', { durable: true });
  // DLQ must exist before the main queue references it as dead-letter target.
  await channel.assertQueue(config.rabbitmq.dlq, { durable: true });
  // Route rejected/expired messages to the DLQ via the default exchange (empty
  // string). With the default exchange, the routing key IS the target queue
  // name, so we hard-set `deadLetterRoutingKey` to the DLQ. Without this
  // routing key, messages nack'd with requeue=false get silently dropped.
  await channel.assertQueue(config.rabbitmq.queue, {
    durable: true,
    deadLetterExchange: '',
    deadLetterRoutingKey: config.rabbitmq.dlq,
  });
  await channel.bindQueue(config.rabbitmq.queue, config.rabbitmq.exchange, '');

  // H-15 — cadena de espera con TTL. RabbitMQ no sabe reintentar con backoff por si solo: la
  // unica forma sin plugins es una cola por cooldown cuyo `messageTtl` vence y dead-letterea el
  // mensaje de vuelta a la cola principal. `nack(requeue=true)` NO vale como alternativa —
  // reencola al frente sin esperar nada y deja al consumidor girando en bucle cerrado sobre el
  // mismo mensaje. Estas colas NO se bindean al exchange: solo reciben por sendToQueue directo.
  for (const [index, ttl] of RETRY_COOLDOWNS_MS.entries()) {
    await channel.assertQueue(retryQueueName(index + 1), {
      durable: true,
      messageTtl: ttl,
      deadLetterExchange: '',
      deadLetterRoutingKey: config.rabbitmq.queue,
    });
  }

  const emitter = connection as unknown as EventEmitter;
  emitter.on('error', (err: Error) => logger.error({ err: err.message }, 'RabbitMQ connection error'));
  emitter.on('close', () => {
    logger.warn('RabbitMQ connection closed — reconnecting');
    context = undefined;
    setTimeout(() => {
      void connectRabbit();
    }, 3000);
  });

  logger.info(
    { exchange: config.rabbitmq.exchange, queue: config.rabbitmq.queue },
    'RabbitMQ connected',
  );
  return { connection, channel };
}

export async function connectRabbit(): Promise<void> {
  let attempts = 0;
  while (!context) {
    try {
      context = await tryConnect();
    } catch (err) {
      attempts += 1;
      const delay = Math.min(1000 * Math.pow(2, attempts - 1), 30_000);
      logger.error({ attempts, delayMs: delay, err: (err as Error).message }, 'RabbitMQ connect failed');
      await new Promise((resolve) => setTimeout(resolve, delay));
    }
  }
}

export function getRabbitContext(): RabbitContext {
  if (!context) {
    throw new Error('RabbitMQ not connected — call connectRabbit() during boot');
  }
  return context;
}

export async function disconnectRabbit(): Promise<void> {
  if (context) {
    try {
      await context.channel.close();
      await context.connection.close();
    } catch (err) {
      logger.warn({ err: (err as Error).message }, 'RabbitMQ close error');
    }
    context = undefined;
  }
}
