import type { Meeting, MeetingSnapshot } from '../../domain/meetings/meeting.js';
import type { MeetingInvitation } from '../../domain/meetings/meeting-invitation.js';

export interface MeetingRepository {
  save(meeting: Meeting): Promise<void>;
  findById(tenantId: string, meetingId: string): Promise<Meeting | null>;
  findByShortCode(tenantId: string, shortCode: string): Promise<Meeting | null>;
  saveInvitation(invitation: MeetingInvitation): Promise<void>;
  /**
   * Cross-tenant lookup por diseno: el TokenHash es globalmente unico (SHA-256
   * de 32 bytes cripto-random). Se necesita para validar la invitation antes
   * de conocer el tenant del meeting. Cierre CRIT-6 legacy — nunca se persiste
   * el JWT en claro, solo el hash.
   */
  findInvitationByHash(tokenHash: string): Promise<MeetingInvitation | null>;
  listUpcomingForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
  }): Promise<MeetingSnapshot[]>;
  countUpcomingForUser(tenantId: string, userId: string): Promise<number>;
}
