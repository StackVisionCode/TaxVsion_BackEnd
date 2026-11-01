import type { UserCommunicationPreferencesRepository } from '../ports/user-communication-preferences-repository.js';

/**
 * Idioma de transcript que el usuario eligió para su voz ("Language of my voice notes"); null si no
 * eligió (rige el de la oficina, normalmente `auto`). En oficinas bilingües cada uno habla su idioma:
 * forzar el de la oficina hace que Whisper traduzca en vez de transcribir (INV-T01).
 */
export async function speakerTranscriptLanguage(
  preferences: Pick<UserCommunicationPreferencesRepository, 'find'> | undefined,
  tenantId: string,
  userId: string,
): Promise<string | null> {
  if (!preferences) return null;
  const found = await preferences.find(tenantId, userId).catch(() => null);
  return found?.toSnapshot().transcriptLanguage?.trim() || null;
}
