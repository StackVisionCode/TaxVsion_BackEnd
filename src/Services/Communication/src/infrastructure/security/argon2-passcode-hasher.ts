import argon2 from 'argon2';
import type { PasscodeHasher } from '../../application/ports/passcode-hasher.js';

/**
 * Argon2id — memoria 64MB, 3 pasadas, paralelismo 1. Balance seguridad/latencia
 * razonable para verificacion online. Cierra CRIT-15 del legacy.
 */
export class Argon2PasscodeHasher implements PasscodeHasher {
  private static readonly OPTIONS = {
    type: argon2.argon2id,
    memoryCost: 65536,
    timeCost: 3,
    parallelism: 1,
  } as const;

  async hash(passcode: string): Promise<string> {
    return argon2.hash(passcode, Argon2PasscodeHasher.OPTIONS);
  }

  async verify(hash: string, passcode: string): Promise<boolean> {
    try {
      return await argon2.verify(hash, passcode);
    } catch {
      return false;
    }
  }
}
