import { randomUUID } from 'node:crypto';
import { Result } from '../../domain/shared/result.js';
import {
  UserCommunicationPreferences,
  type UserCommunicationPreferencesPatch,
  type UserCommunicationPreferencesSnapshot,
} from '../../domain/settings/user-communication-preferences.js';
import { ConfigurationAuditReason, ConfigurationAuditScope } from '../../domain/settings/configuration-audit.js';
import type { UserCommunicationPreferencesRepository } from '../ports/user-communication-preferences-repository.js';
import type { ConfigurationAuditRepository } from '../ports/configuration-audit-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { SettingsEventTypes, type UserPreferencesChangedEvent } from '../../contracts/events/settings-events.js';
import { buildAuditEntries } from './configuration-audit-writer.js';

export interface UserPreferencesDeps {
  readonly userPreferences: UserCommunicationPreferencesRepository;
  readonly configurationAudit: ConfigurationAuditRepository;
  readonly publisher: IntegrationEventPublisher;
}

export async function getUserPreferences(
  input: { tenantId: string; userId: string },
  deps: Pick<UserPreferencesDeps, 'userPreferences'>,
): Promise<UserCommunicationPreferencesSnapshot> {
  const existing = await deps.userPreferences.find(input.tenantId, input.userId);
  return (existing ?? UserCommunicationPreferences.defaults(input.tenantId, input.userId)).toSnapshot();
}

export interface UpdateUserPreferencesCommand {
  readonly tenantId: string;
  readonly userId: string;
  readonly correlationId: string;
  readonly patch: UserCommunicationPreferencesPatch;
}

/** Autoservicio: el usuario solo toca las suyas; no hay techo que validar (T-47). */
export async function updateUserPreferences(
  cmd: UpdateUserPreferencesCommand,
  deps: UserPreferencesDeps,
): Promise<Result<UserCommunicationPreferencesSnapshot>> {
  const now = new Date();
  const preferences =
    (await deps.userPreferences.find(cmd.tenantId, cmd.userId)) ??
    UserCommunicationPreferences.defaults(cmd.tenantId, cmd.userId, now);
  const applied = preferences.update(cmd.patch, now);
  if (!applied.isSuccess) return Result.fail(applied.error);
  const changes = applied.value;
  if (changes.length === 0) return Result.ok(preferences.toSnapshot());

  await deps.userPreferences.save(preferences);
  await deps.configurationAudit.append(
    buildAuditEntries({
      tenantId: cmd.tenantId,
      scope: ConfigurationAuditScope.UserPreferences,
      reason: ConfigurationAuditReason.Update,
      actorUserId: cmd.userId,
      correlationId: cmd.correlationId,
      changes,
      now,
    }),
  );
  const event: UserPreferencesChangedEvent = {
    eventId: randomUUID(),
    eventType: SettingsEventTypes.UserPreferencesChanged,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    userId: cmd.userId,
    changes,
  };
  await deps.publisher.enqueue(event);
  return Result.ok(preferences.toSnapshot());
}
