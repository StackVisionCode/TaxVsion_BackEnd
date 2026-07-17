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
    // Registrado ANTES de cualquier assert/bind: un 406 PRECONDITION-FAILED (ver
    // mas abajo) u otro error de protocolo durante el setup emite 'error' en el
    // objeto connection de forma asincrona: si el listener se agrega recien al
    // final, ese evento no tiene handler todavia y Node lo trata como excepcion
    // no capturada (tumba el proceso entero, saltandose el try/catch + retry de
    // `connectRabbit()`). Con el listener ya puesto, cualquier error de aca en
    // adelante se loguea y el `close` que le sigue dispara el reintento normal.
    const emitter = connection;
    emitter.on('error', (err) => logger.error({ err: err.message }, 'RabbitMQ connection error'));
    emitter.on('close', () => {
        logger.warn('RabbitMQ connection closed — reconnecting');
        context = undefined;
        setTimeout(() => {
            void connectRabbit();
        }, 3000);
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
    // Fase D2 — cola dedicada de CloudStorage para SaveFileRequestedIntegrationEvent
    // (ver save-file-requested-publisher.ts). CloudStorage (Wolverine, con
    // AutoProvision) es la duena real de esta cola y la declara con sus propios
    // argumentos (incluido `x-dead-letter-exchange`, que Wolverine agrega solo).
    // Un `assertQueue({ durable: true })` de este lado, SIN esos mismos
    // argumentos, choca con RabbitMQ: reabrir una cola ya declarada exige
    // argumentos identicos byte a byte o el broker cierra el canal con 406
    // PRECONDITION-FAILED (confirmado en un run real: "inequivalent arg
    // 'x-dead-letter-exchange'... received none but current is... 'wolverine-
    // dead-letter-queue'"). Un `checkQueue` (declare pasivo, sin argumentos) solo
    // confirma que la cola existe — nunca puede chocar por argumentos. Si
    // CloudStorage todavia no arranco nunca (cola inexistente), esto rechaza la
    // promesa como cualquier otro fallo de setup, y el loop de `connectRabbit()`
    // reintenta hasta que CloudStorage la haya creado.
    await channel.checkQueue(config.cloudStorage.externalUploadsQueue);
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