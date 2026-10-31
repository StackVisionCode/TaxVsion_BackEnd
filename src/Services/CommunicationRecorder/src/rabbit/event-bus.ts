import amqplib, { type ConfirmChannel, type Connection } from 'amqplib';
import type { EventEmitter } from 'node:events';
import type { Logger } from 'pino';

/** Salida de mensajería del recorder: eventos al fanout y comandos a la cola de CloudStorage. */
export interface EventBus {
  /** Publica al exchange fanout con AMQP `type` = eventType y `messageId` = eventId; espera el confirm. */
  publishEvent(eventType: string, eventId: string, body: object): Promise<void>;
  /** Envía a una cola por el exchange por defecto (routingKey = cola); espera el confirm. */
  sendToQueue(queue: string, messageId: string, body: object): Promise<void>;
}

/**
 * Misma topología que el transcript worker (exchange fanout durable `taxvision-events`; la cola de
 * CloudStorage solo se comprueba con checkQueue porque la declara Wolverine con sus argumentos), pero
 * con canal de confirmación: el trabajo de composición no se da por hecho hasta que el broker confirma.
 */
export class RabbitEventBus implements EventBus {
  private connection: Connection | undefined;
  private channel: ConfirmChannel | undefined;
  private closing = false;

  constructor(
    private readonly opts: { uri: string; exchange: string; externalUploadsQueue: string },
    private readonly logger: Logger,
  ) {}

  async connect(): Promise<void> {
    let attempts = 0;
    while (!this.channel && !this.closing) {
      try {
        await this.tryConnect();
      } catch (err) {
        attempts += 1;
        const delay = Math.min(1000 * 2 ** (attempts - 1), 30_000);
        this.logger.error({ attempts, delayMs: delay, err: (err as Error).message }, 'RabbitMQ connect failed');
        await new Promise((resolve) => setTimeout(resolve, delay));
      }
    }
  }

  get connected(): boolean {
    return this.channel !== undefined;
  }

  private async tryConnect(): Promise<void> {
    const parsed = new URL(this.opts.uri);
    const connection = await amqplib.connect({
      protocol: parsed.protocol.replace(':', ''),
      hostname: parsed.hostname,
      port: parsed.port ? Number(parsed.port) : 5672,
      username: decodeURIComponent(parsed.username),
      password: decodeURIComponent(parsed.password),
      vhost: parsed.pathname && parsed.pathname !== '/' ? decodeURIComponent(parsed.pathname.slice(1)) : '/',
      frameMax: 131072,
    });
    // Listener antes de cualquier assert: un 406 durante el setup se emite asíncrono en la conexión.
    const emitter = connection as unknown as EventEmitter;
    emitter.on('error', (err: Error) => this.logger.error({ err: err.message }, 'RabbitMQ connection error'));
    emitter.on('close', () => {
      this.channel = undefined;
      this.connection = undefined;
      if (this.closing) return;
      this.logger.warn('RabbitMQ connection closed — reconnecting');
      setTimeout(() => void this.connect(), 3000);
    });
    const channel = await connection.createConfirmChannel();
    await channel.assertExchange(this.opts.exchange, 'fanout', { durable: true });
    await channel.checkQueue(this.opts.externalUploadsQueue);
    this.connection = connection;
    this.channel = channel;
    this.logger.info({ exchange: this.opts.exchange }, 'RabbitMQ connected');
  }

  private requireChannel(): ConfirmChannel {
    if (!this.channel) throw Object.assign(new Error('RabbitMQ not connected'), { code: 'ECONNREFUSED' });
    return this.channel;
  }

  async publishEvent(eventType: string, eventId: string, body: object): Promise<void> {
    const channel = this.requireChannel();
    channel.publish(this.opts.exchange, '', Buffer.from(JSON.stringify(body), 'utf-8'), {
      contentType: 'application/json',
      persistent: true,
      messageId: eventId,
      type: eventType,
      timestamp: Math.floor(Date.now() / 1000),
    });
    await channel.waitForConfirms();
  }

  async sendToQueue(queue: string, messageId: string, body: object): Promise<void> {
    const channel = this.requireChannel();
    channel.publish('', queue, Buffer.from(JSON.stringify(body), 'utf-8'), {
      contentType: 'application/json',
      persistent: true,
      messageId,
    });
    await channel.waitForConfirms();
  }

  async close(): Promise<void> {
    this.closing = true;
    try {
      await this.channel?.close();
      await this.connection?.close();
    } catch (err) {
      this.logger.warn({ err: (err as Error).message }, 'RabbitMQ close error');
    }
    this.channel = undefined;
    this.connection = undefined;
  }
}
