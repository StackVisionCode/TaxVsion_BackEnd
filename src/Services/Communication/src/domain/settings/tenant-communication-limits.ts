/**
 * Proyeccion local desde Subscription (SubscriptionActivated / PlanChanged / etc).
 * NO es un aggregate — es un read model. Se actualiza por consumers.
 */
export interface TenantCommunicationLimitsSnapshot {
  readonly tenantId: string;
  readonly planCode: string;
  readonly maxMeetingParticipants: number;
  readonly maxMeetingMinutes: number;
  readonly maxConcurrentCalls: number;
  readonly maxMonthlyMinutes: number;
  readonly recordingEnabled: boolean;
  readonly supportEnabled: boolean;
  readonly isSuspended: boolean;
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
