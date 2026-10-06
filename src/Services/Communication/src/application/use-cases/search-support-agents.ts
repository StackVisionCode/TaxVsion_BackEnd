import { CommunicationPermissions } from '../../domain/shared/permissions.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';

export interface SearchSupportAgentsQuery {
  readonly agentTenantId: string;
  readonly query: string;
  readonly limit?: number;
}

export interface SupportAgentDto {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string;
  readonly actorType: string;
}

export async function searchSupportAgents(
  query: SearchSupportAgentsQuery,
  deps: {
    readonly userDirectory: UserDirectoryRepository;
    readonly userPermissions: UserPermissionsProjectionRepository;
  },
): Promise<SupportAgentDto[]> {
  const limit = Math.min(query.limit ?? 10, 25);
  const entries = await deps.userDirectory.searchByDisplayNameOrEmail(query.agentTenantId, query.query, limit);
  const results: SupportAgentDto[] = [];

  for (const entry of entries) {
    if (entry.tenantId !== query.agentTenantId || !entry.isActive) {
      continue;
    }

    if (entry.actorType === 'PlatformAdmin') {
      results.push(toDto(entry));
      continue;
    }

    const permissions = await deps.userPermissions.findByUserId(entry.userId);
    const canActAsSupport =
      permissions?.isActive === true &&
      permissions.tenantId === query.agentTenantId &&
      permissions.permissions.includes(CommunicationPermissions.SupportAgent);
    if (canActAsSupport) {
      results.push(toDto(entry));
    }
  }

  return results;
}

function toDto(entry: {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string;
  readonly actorType: string;
}): SupportAgentDto {
  return {
    userId: entry.userId,
    displayName: entry.displayName,
    email: entry.email,
    actorType: entry.actorType,
  };
}
