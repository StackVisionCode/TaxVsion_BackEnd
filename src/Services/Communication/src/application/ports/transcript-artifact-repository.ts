import type { TranscriptArtifact, TranscriptScope } from '../../domain/recording/transcript-artifact.js';

/** F7 (T-34): un transcript por (Scope, ScopeId); save() es upsert. */
export interface TranscriptArtifactRepository {
  save(artifact: TranscriptArtifact): Promise<void>;
  findByScope(tenantId: string, scope: TranscriptScope, scopeId: string): Promise<TranscriptArtifact | null>;
}
