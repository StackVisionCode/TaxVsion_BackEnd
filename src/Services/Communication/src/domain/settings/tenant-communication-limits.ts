/**
 * Proyeccion local desde Subscription (subscription.entitlements_changed.v1).
 * NO es un aggregate — es un read model. Se actualiza por consumers.
 *
 * null = el plan no define la clave `communication.*`; rige entonces el valor global de
 * PlatformCommunicationSettings (los defaults ya no viven en código, T-38).
 */
export interface TenantCommunicationLimitsSnapshot {
  readonly tenantId: string;
  readonly planCode: string;
  readonly maxMeetingParticipants: number | null;
  readonly maxMeetingMinutes: number | null;
  readonly maxConcurrentCalls: number | null;
  readonly maxMonthlyMinutes: number | null;
  readonly recordingEnabled: boolean | null;
  readonly supportEnabled: boolean;
  readonly isSuspended: boolean;
  /** Modulos comerciales habilitados por el plan (los `module.*` del snapshot de entitlements). Gate de modulo. */
  readonly enabledModules: readonly string[];
  readonly maxRecordingMinutesMeeting: number | null;
  readonly maxRecordingMinutesAudioCall: number | null;
  readonly maxRecordingMinutesVideoCall: number | null;
  readonly recordingVideoEnabled: boolean | null;
  readonly transcriptEnabled: boolean | null;
  readonly callsEnabled: boolean | null;
  readonly maxCallMinutes: number | null;
  /** Inicio del período de la suscripción: su día local es el ancla del cupo mensual (P11). null = mes UTC. */
  readonly usagePeriodAnchorUtc?: Date | null;
  /**
   * Plan a la medida: claves `communication.*` cuyo valor es una excepción de ESTA oficina (no el de su plan).
   * Esas pueden pasar el máximo global de la plataforma; las demás no. Ausente o vacío = ninguna.
   */
  readonly overriddenKeys?: readonly string[];
  /** Revisión de entitlements del evento; null en mensajes viejos (se aplica sin comparar). */
  readonly revisionNumber: number | null;
  readonly updatedAtUtc: Date;
}

/**
 * Combina settings (opcion del tenant admin) con limits (proyeccion del plan).
 * Regla: `effective = min(planLimit, tenantSetting)` para cualquier N; feature
 * booleana es AND. Suspended anula todo.
 */
export function computeEffectiveMaxMeetingParticipants(input: {
  planLimit: number;
  settingLimit: number;
  isSuspended: boolean;
}): number {
  if (input.isSuspended) return 0;
  return Math.max(0, Math.min(input.planLimit, input.settingLimit));
}

export function isFeatureAllowed(input: {
  planFlag: boolean;
  settingFlag: boolean;
  isSuspended: boolean;
}): boolean {
  if (input.isSuspended) return false;
  return input.planFlag && input.settingFlag;
}
