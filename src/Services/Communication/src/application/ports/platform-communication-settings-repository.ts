import type { PlatformCommunicationSettings } from '../../domain/settings/platform-communication-settings.js';

/** Fila única "global". `get` devuelve defaults (sin persistir) si la fila no existe todavía. */
export interface PlatformCommunicationSettingsRepository {
  get(): Promise<PlatformCommunicationSettings>;
  save(settings: PlatformCommunicationSettings): Promise<void>;
}
