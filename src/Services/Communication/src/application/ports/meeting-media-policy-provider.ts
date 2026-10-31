import type { MeetingMediaPolicy } from '../../domain/meetings/meeting.js';

/**
 * De donde sale la politica de media (umbral mesh/SFU, cupo por defecto) para una oficina.
 * F1: configuracion del proceso. F2: PlatformCommunicationSettings + techos por oficina via el
 * resolver de settings efectivos, sin tocar a quien la consume.
 */
export interface MeetingMediaPolicyProvider {
  get(tenantId: string): Promise<MeetingMediaPolicy>;
}
