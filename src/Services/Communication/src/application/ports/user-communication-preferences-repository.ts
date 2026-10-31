import type { UserCommunicationPreferences } from '../../domain/settings/user-communication-preferences.js';

export interface UserCommunicationPreferencesRepository {
  find(tenantId: string, userId: string): Promise<UserCommunicationPreferences | null>;
  save(preferences: UserCommunicationPreferences): Promise<void>;
}
