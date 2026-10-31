/**
 * Aviso realtime de que la configuración vigente de la oficina cambió (ADD-10 §13.5). Va al room
 * de MIEMBROS del tenant: el CRM y el portal vuelven a pedir `GET /communication/settings` o su
 * bootstrap; el payload no lleva valores, solo qué claves cambiaron.
 */
export interface SettingsChangedDto {
  readonly scope: 'platform' | 'tenant';
  readonly changedKeys: readonly string[];
}

export const SettingsSocketEvents = {
  Changed: 'settings.changed',
} as const;
