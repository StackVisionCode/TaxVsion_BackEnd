/** Conversación del ámbito con su última actividad (para quedarse con las más recientes). */
export interface ChatScopeRow {
  readonly conversationId: string;
  readonly lastActivityAtUtc: Date | null;
}

/** Adjunto de chat disponible, de un mensaje vivo, y su conversación. */
export interface ChatAttachmentRow {
  readonly fileId: string;
  readonly conversationId: string;
}

/** Adjunto a declarar de nuevo (backfill). */
export interface RedeclareChatRow {
  readonly fileId: string;
  readonly conversationId: string;
  readonly isVoiceNote: boolean;
}

/** Participante que puede ser el cliente de una conversación. */
export interface ConversationClientRow {
  readonly conversationId: string;
  readonly actorType: string;
  readonly customerId: string | null;
  readonly isRemoved: boolean;
}

/** Llamada o reunión dueña de una grabación (columna `Scope`). */
export type RecordingKind = 'Call' | 'Meeting';

/** Llamada o reunión del ámbito con su última grabación lista. */
export interface RecordingScopeRow {
  readonly scopeId: string;
  readonly lastActivityAtUtc: Date | null;
}

/** Grabación `Ready` no purgada, o `.txt` `Ready` de una sesión no purgada, por archivo. */
export interface RecordingFileRow {
  readonly fileId: string;
  readonly scope: RecordingKind;
  readonly scopeId: string;
  readonly role: 'recording' | 'transcript';
  /** Número de la grabación dentro de la llamada o reunión. */
  readonly sequence: number;
  /** Grabaciones listas y no purgadas de esa llamada o reunión (para "· Recording N"). */
  readonly readyCount: number;
}

/** Grabación con archivo y sin purgar (backfill). */
export interface RedeclareRecordingRow {
  readonly id: string;
  readonly scope: RecordingKind;
  readonly scopeId: string;
  readonly fileId: string;
}

/** Transcript `Ready` de una sesión no purgada (backfill). */
export interface RedeclareTranscriptRow {
  readonly id: string;
  readonly scope: RecordingKind;
  readonly scopeId: string;
  readonly txtFileId: string | null;
  readonly jsonFileId: string | null;
}

/** Participante con cliente de una llamada o reunión. */
export interface RecordingClientRow {
  readonly scopeId: string;
  readonly actorType: string;
  readonly customerId: string | null;
}

/**
 * Lecturas de la biblioteca de archivos (CloudStorage pregunta qué ve una persona). Siempre con tenant explícito;
 * los adjuntos que son el `.txt` de un transcript no son de chat (los responde la rama de grabaciones).
 */
export interface FileLibraryReadRepository {
  /** Conversaciones donde la persona es participante activa y hay algún adjunto disponible de un mensaje vivo. */
  listMemberChatScope(input: { tenantId: string; userId: string; take: number }): Promise<ChatScopeRow[]>;

  /**
   * Chats Direct de cliente cerrados por un retiro, con algún adjunto disponible. `assignedOnly`: solo los de
   * clientes asignados a la persona.
   */
  listClosedClientChatScope(input: {
    tenantId: string;
    userId: string;
    assignedOnly: boolean;
    take: number;
  }): Promise<ChatScopeRow[]>;

  /** Trackings `Available` de mensajes no borrados, por `FileId`. */
  findChatAttachments(input: { tenantId: string; fileIds: readonly string[] }): Promise<ChatAttachmentRow[]>;

  /** Trackings con mensaje existente, ordenados por `FileId`, a partir del último visto. */
  listChatAttachmentsForRedeclare(input: {
    tenantId: string;
    afterFileId: string | null;
    take: number;
  }): Promise<RedeclareChatRow[]>;

  /** Mismo criterio que `listChatAttachmentsForRedeclare`, por `FileId`. */
  findChatAttachmentsForRedeclare(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RedeclareChatRow[]>;

  /** Participantes cliente (cuenta de portal o con cliente) de estas conversaciones. */
  listConversationClients(input: {
    tenantId: string;
    conversationIds: readonly string[];
  }): Promise<ConversationClientRow[]>;
  /**
   * Llamadas con alguna grabación `Ready` no purgada donde la persona participó; con `clientCalls` también las que
   * tienen un cliente (todas, o solo las de clientes asignados).
   */
  listCallRecordingScope(input: {
    tenantId: string;
    userId: string;
    clientCalls: 'none' | 'all' | 'assigned';
    take: number;
  }): Promise<RecordingScopeRow[]>;

  /** Reuniones con alguna grabación `Ready` no purgada donde la persona fue anfitriona o participante. */
  listMeetingRecordingScope(input: {
    tenantId: string;
    userId: string;
    take: number;
  }): Promise<RecordingScopeRow[]>;

  /** Grabaciones y `.txt` visibles en la biblioteca, por `FileId` (pistas y `.json` nunca). */
  findRecordingFiles(input: { tenantId: string; fileIds: readonly string[] }): Promise<RecordingFileRow[]>;

  /** Grabaciones con archivo y sin purgar, ordenadas por id. */
  listRecordingsForRedeclare(input: {
    tenantId: string;
    afterId: string | null;
    take: number;
  }): Promise<RedeclareRecordingRow[]>;

  /** Transcripts `Ready` de llamadas y reuniones cuya sesión no está purgada, ordenados por id. */
  listTranscriptsForRedeclare(input: {
    tenantId: string;
    afterId: string | null;
    take: number;
  }): Promise<RedeclareTranscriptRow[]>;

  /** Mismo criterio que `listRecordingsForRedeclare`, por `RecordingFileId`. */
  findRecordingsForRedeclare(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RedeclareRecordingRow[]>;

  /** Mismo criterio que `listTranscriptsForRedeclare`, con el `.txt` o el `.json` entre los ids. */
  findTranscriptsForRedeclare(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RedeclareTranscriptRow[]>;

  /** Participantes con cliente de estas llamadas o reuniones. */
  listRecordingClients(input: {
    tenantId: string;
    scope: RecordingKind;
    scopeIds: readonly string[];
  }): Promise<RecordingClientRow[]>;
}
