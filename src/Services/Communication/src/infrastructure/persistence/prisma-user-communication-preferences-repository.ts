import type { PrismaClient } from '@prisma/client';
import { UserCommunicationPreferences } from '../../domain/settings/user-communication-preferences.js';
import type { UserCommunicationPreferencesRepository } from '../../application/ports/user-communication-preferences-repository.js';

export class PrismaUserCommunicationPreferencesRepository implements UserCommunicationPreferencesRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async find(tenantId: string, userId: string): Promise<UserCommunicationPreferences | null> {
    const row = await this.prisma.userCommunicationPreferences.findUnique({
      where: { TenantId_UserId: { TenantId: tenantId, UserId: userId } },
    });
    if (!row) return null;
    return UserCommunicationPreferences.rehydrate({
      tenantId: row.TenantId,
      userId: row.UserId,
      meetingSoundsMuted: row.MeetingSoundsMuted,
      transcriptLanguage: row.TranscriptLanguage,
      preferAudioOnly: row.PreferAudioOnly,
      echoCancellation: row.EchoCancellation,
      noiseSuppression: row.NoiseSuppression,
      autoGainControl: row.AutoGainControl,
      joinMuted: row.JoinMuted,
      joinCameraOff: row.JoinCameraOff,
      updatedAtUtc: row.UpdatedAtUtc,
    });
  }

  async save(preferences: UserCommunicationPreferences): Promise<void> {
    const s = preferences.toSnapshot();
    const data = {
      MeetingSoundsMuted: s.meetingSoundsMuted,
      TranscriptLanguage: s.transcriptLanguage,
      PreferAudioOnly: s.preferAudioOnly,
      EchoCancellation: s.echoCancellation,
      NoiseSuppression: s.noiseSuppression,
      AutoGainControl: s.autoGainControl,
      JoinMuted: s.joinMuted,
      JoinCameraOff: s.joinCameraOff,
    };
    await this.prisma.userCommunicationPreferences.upsert({
      where: { TenantId_UserId: { TenantId: s.tenantId, UserId: s.userId } },
      create: { TenantId: s.tenantId, UserId: s.userId, ...data },
      update: data,
    });
  }
}
