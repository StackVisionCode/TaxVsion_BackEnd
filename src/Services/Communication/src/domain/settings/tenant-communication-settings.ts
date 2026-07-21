import { Result, makeError } from '../shared/result.js';
import { RecordingConsentPolicy, isRecordingConsentPolicy } from '../recording/recording-consent.js';

/**
 * Aggregate root: TenantCommunicationSettings. Un aggregate por tenant. Se
 * crea perezosamente al primer TenantCreated o al primer GET; para simplicidad
 * el use-case `getOrCreate` construye defaults conservadores.
 */
export interface TenantCommunicationSettingsSnapshot {
  readonly tenantId: string;
  readonly chatEnabled: boolean;
  readonly callsEnabled: boolean;
  readonly videoCallsEnabled: boolean;
  readonly meetingsEnabled: boolean;
  readonly supportEnabled: boolean;
  readonly screenshotsEnabled: boolean;
  readonly internalGroupsEnabled: boolean;
  readonly employeeToEmployeeChatEnabled: boolean;
  /**
   * Fase B5 (chat tipado) — si esta en true, un chat directo que involucra a
   * un customer solo se permite si el otro lado es su preparador asignado
   * (CustomerPreparerAssignment). Default false, opt-in por tenant.
   */
  readonly restrictCustomerChatToAssignedPreparer: boolean;
  readonly defaultCameraOff: boolean;
  readonly defaultMicrophoneOff: boolean;
  readonly persistChatOnEnd: boolean;
  readonly messageRetentionDays: number;
  readonly recordingRetentionDays: number;
  readonly purgeEnabled: boolean;
  /**
   * Gap cerrado en Fase Backend 3 — la columna Prisma existia desde Fase 2
   * (default 'NoRejections') pero nunca se conecto al aggregate. Ver
   * evaluateRecordingConsentPolicy en domain/recording/recording-consent.ts.
   */
  readonly recordingConsentPolicy: RecordingConsentPolicy;
  readonly createdAtUtc: Date;
  readonly updatedAtUtc: Date;
}

export class TenantCommunicationSettings {
  private constructor(private state: TenantCommunicationSettingsSnapshot) {}

  static rehydrate(snapshot: TenantCommunicationSettingsSnapshot): TenantCommunicationSettings {
    return new TenantCommunicationSettings(snapshot);
  }

  static defaults(tenantId: string, now: Date = new Date()): TenantCommunicationSettings {
    return new TenantCommunicationSettings({
      tenantId,
      chatEnabled: true,
      callsEnabled: true,
      videoCallsEnabled: true,
      meetingsEnabled: true,
      supportEnabled: true,
      screenshotsEnabled: true,
      internalGroupsEnabled: false,
      employeeToEmployeeChatEnabled: false,
      restrictCustomerChatToAssignedPreparer: false,
      defaultCameraOff: true,
      defaultMicrophoneOff: false,
      persistChatOnEnd: false,
      messageRetentionDays: 365,
      recordingRetentionDays: 90,
      purgeEnabled: false,
      recordingConsentPolicy: RecordingConsentPolicy.NoRejections,
      createdAtUtc: now,
      updatedAtUtc: now,
    });
  }

  update(input: Partial<Omit<TenantCommunicationSettingsSnapshot, 'tenantId' | 'createdAtUtc' | 'updatedAtUtc'>>): Result<void> {
    if (input.messageRetentionDays !== undefined && (input.messageRetentionDays < 1 || input.messageRetentionDays > 3650)) {
      return Result.fail(makeError('Settings.InvalidRetention', 'messageRetentionDays must be 1..3650.'));
    }
    if (input.recordingRetentionDays !== undefined && (input.recordingRetentionDays < 1 || input.recordingRetentionDays > 3650)) {
      return Result.fail(makeError('Settings.InvalidRetention', 'recordingRetentionDays must be 1..3650.'));
    }
    if (input.recordingConsentPolicy !== undefined && !isRecordingConsentPolicy(input.recordingConsentPolicy)) {
      return Result.fail(makeError('Settings.InvalidRecordingConsentPolicy', 'Invalid recordingConsentPolicy.'));
    }
    this.state = { ...this.state, ...input, updatedAtUtc: new Date() };
    return Result.okVoid();
  }

  toSnapshot(): TenantCommunicationSettingsSnapshot {
    return this.state;
  }

  get tenantId(): string {
    return this.state.tenantId;
  }
}
