import amqplib from 'amqplib';
import { config } from '../config.js';
import { logger } from '../logger.js';
let context;
async function tryConnect() {
    const parsed = new URL(config.rabbitmq.uri);
    const connection = await amqplib.connect({
        protocol: parsed.protocol.replace(':', ''),
        hostname: parsed.hostname,
        port: parsed.port ? Number(parsed.port) : 5672,
        username: decodeURIComponent(parsed.username),
        password: decodeURIComponent(parsed.password),
        vhost: parsed.pathname && parsed.pathname !== '/' ? decodeURIComponent(parsed.pathname.slice(1)) : '/',
        frameMax: 131072,
    });
    const channel = await connection.createChannel();
    const dlq = `${config.rabbitmq.queue}.dlq`;
    await channel.assertExchange(config.rabbitmq.exchange, 'fanout', { durable: true });
    await channel.assertQueue(dlq, { durable: true });
    await channel.assertQueue(config.rabbitmq.queue, {
        durable: true,
        deadLetterExchange: '',
        deadLetterRoutingKey: dlq,
    });
    await channel.bindQueue(config.rabbitmq.queue, config.rabbitmq.exchange, '');
    const emitter = connection;
    emitter.on('error', (err) => logger.error({ err: err.message }, 'RabbitMQ connection error'));
    emitter.on('close', () => {
        logger.warn('RabbitMQ connection closed — reconnecting');
        context = undefined;
        setTimeout(() => {
            void connectRabbit();
        }, 3000);
    });
    logger.info({ exchange: config.rabbitmq.exchange, queue: config.rabbitmq.queue }, 'RabbitMQ connected');
    return { connection, channel };
}
export async function connectRabbit() {
    let attempts = 0;
    while (!context) {
        try {
            context = await tryConnect();
        }
        catch (err) {
            attempts += 1;
            const delay = Math.min(1000 * Math.pow(2, attempts - 1), 30_000);
            logger.error({ attempts, delayMs: delay, err: err.message }, 'RabbitMQ connect failed');
            await new Promise((resolve) => setTimeout(resolve, delay));
        }
    }
}
export function getRabbitContext() {
    if (!context) {
        throw new Error('RabbitMQ not connected — call connectRabbit() during boot');
    }
    return context;
}
export async function disconnectRabbit() {
    if (context) {
        try {
            await context.channel.close();
            await context.connection.close();
        }
        catch (err) {
            logger.warn({ err: err.message }, 'RabbitMQ close error');
        }
        context = undefined;
    }
}
//# sourceMappingURL=rabbit-connection.js.map