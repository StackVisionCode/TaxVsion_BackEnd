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
  await channel.assertQueue(config.rabbitmq.queue, { durable: true, deadLetterExchange: '' });
  await channel.assertQueue(config.rabbitmq.dlq, { durable: true });
  await channel.bindQueue(config.rabbitmq.queue, config.rabbitmq.exchange, '');

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
