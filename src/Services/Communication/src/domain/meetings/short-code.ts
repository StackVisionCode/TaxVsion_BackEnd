import { randomBytes } from 'node:crypto';

/**
 * Codigo corto (9 chars) para el link del meeting. Alfabeto sin caracteres
 * ambiguos (0/O/1/I/l) para dictado por voz. Aleatorio cripto — 30+ bits de
 * entropia, colision extremadamente improbable en el mismo tenant; el
 * indice unique `(TenantId, ShortCode)` protege el resto.
 */
const ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
const LENGTH = 9;

export function generateShortCode(): string {
  const bytes = randomBytes(LENGTH);
  let out = '';
  for (let i = 0; i < LENGTH; i++) {
    out += ALPHABET[bytes[i]! % ALPHABET.length];
  }
  return out;
}
