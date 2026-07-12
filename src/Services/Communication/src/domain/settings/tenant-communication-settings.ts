import { Result, makeError } from '../shared/result.js';

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
  readonly defaultCameraOff: boolean;
  readonly defaultMicrophoneOff: boolean;
  readonly persistChatOnEnd: boolean;
  readonly messageRetentionDays: number;
  readonly recordingRetentionDays: number;
  readonly purgeEnabled: boolean;
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
      defaultCameraOff: true,
      defaultMicrophoneOff: false,
      persistChatOnEnd: false,
      messageRetentionDays: 365,
      recordingRetentionDays: 90,
      purgeEnabled: false,
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
