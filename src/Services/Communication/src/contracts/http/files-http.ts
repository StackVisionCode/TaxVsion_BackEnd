/**
 * Espejo de `BuildingBlocks.Contracts.Files` (FileVisibilityContracts.cs): lo que CloudStorage pide a Communication
 * para la biblioteca de archivos. JSON en camelCase, como lo lee .NET.
 */
export const FilesLimits = {
  /** `FileVisibilityLimits.Batch`. */
  VisibilityBatch: 100,
  /** `CommunicationFileScopeLists.MaxIds`: ids por lista, los de actividad más reciente. */
  ScopeMaxIds: 2000,
} as const;

/** `CommunicationFileScopeLists`: nombre de la lista cortada en `truncated`. */
export const FileScopeLists = {
  Conversations: 'conversations',
  Calls: 'calls',
  Meetings: 'meetings',
} as const;

/** `FileVisibilityResourceTypes`: a dónde abre la fila. */
export const FileVisibilityResourceTypes = {
  Conversation: 'conversation',
  SupportTicket: 'support_ticket',
  Meeting: 'meeting',
  Call: 'call',
} as const;

/** `FileVisibilityRestrictions`. */
export const FileVisibilityRestrictions = {
  Participants: 'participants',
} as const;

/** `CommunicationFileScope`. */
export interface CommunicationFileScopeResponse {
  readonly conversationIds: string[];
  readonly callIds: string[];
  readonly meetingIds: string[];
  readonly truncated: string[];
}

/** `VisibleFile`. */
export interface VisibleFileItem {
  readonly fileId: string;
  readonly resourceType: string;
  readonly resourceId: string;
  readonly title: string | null;
  readonly customerId: string | null;
  readonly context: Record<string, string> | null;
  readonly canDownload: boolean;
  readonly restrictedTo: string | null;
}

/** `FileVisibilityResponse`. */
export interface FileVisibilityResponse {
  readonly items: VisibleFileItem[];
}
