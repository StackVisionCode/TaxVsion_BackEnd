import type { Redis } from 'ioredis';
import { config } from '../config.js';

/**
 * Inbox de idempotencia — SET NX EX por eventId. Cierra el gap real de
 * produccion: sin esto, una redelivery de RabbitMQ (consumer muere entre el
 * `ack` pendiente y el trabajo real, o un requeue manual desde la DLQ)
 * reprocesa la grabacion completa (descarga + ffmpeg + whisper.cpp, varios
 * minutos de CPU) y publica un segundo `TranscriptReady` — Communication lo
 * absorbe sin corromper estado (`attachTranscript` rechaza duplicados), pero
 * es trabajo desperdiciado que en produccion, bajo carga, compite por CPU
 * con transcripciones reales. Redis (no in-memory) porque el worker escala
 * a N replicas y un Map local no es visible entre pods.
 */
export class TranscriptInbox {
  constructor(private readonly redis: Redis) {}

  private key(eventId: string): string {
    return `transcript-worker:inbox:${eventId}`;
  }

  /** true si es la primera vez que se ve este eventId dentro del TTL. */
  async tryMarkProcessed(eventId: string): Promise<boolean> {
    const result = await this.redis.set(this.key(eventId), '1', 'EX', config.redis.inboxTtlSeconds, 'NX');
    return result === 'OK';
  }

  /** Revierte la marca — usado cuando el pipeline falla, para que un retry legitimo no se trate como duplicado. */
  async unmark(eventId: string): Promise<void> {
    await this.redis.del(this.key(eventId));
  }
}
