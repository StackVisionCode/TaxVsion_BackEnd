import type { MeetingMediaPolicy } from '../domain/meetings/meeting.js';
import type { MeetingMediaPolicyProvider } from '../application/ports/meeting-media-policy-provider.js';
import type { EffectiveSettingsResolver } from '../application/ports/effective-settings-resolver.js';

/** Politica de media por oficina: umbral mesh/SFU global y cupo efectivo (global → plan → techo → oficina). */
export class ResolverMeetingMediaPolicyProvider implements MeetingMediaPolicyProvider {
  constructor(private readonly resolver: EffectiveSettingsResolver) {}

  async get(tenantId: string): Promise<MeetingMediaPolicy> {
    const effective = await this.resolver.resolve(tenantId);
    return {
      meshMaxParticipants: effective.meeting.meshMaxParticipants,
      defaultMaxParticipants: effective.meeting.maxParticipants,
    };
  }
}
