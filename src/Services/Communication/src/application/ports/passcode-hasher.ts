/**
 * Hasher para passcode de meeting. Cierra CRIT-15 legacy (passcode plano en BD).
 * Argon2id es el default recomendado por OWASP 2024.
 */
export interface PasscodeHasher {
  hash(passcode: string): Promise<string>;
  verify(hash: string, passcode: string): Promise<boolean>;
}
