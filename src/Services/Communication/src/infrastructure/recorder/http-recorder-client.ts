import { lookup } from 'node:dns/promises';
import {
  RecorderRequestError,
  type RecorderClient,
  type RecorderSessionStart,
  type RecorderTimeline,
  type RecorderTrackEndpoint,
  type RecorderTrackStart,
} from '../../application/ports/recorder-client.js';
import type { RecordingCompositionPolicy } from '../../domain/settings/effective-communication-settings.js';
import type { ServiceTokenClient } from '../auth/service-token-client.js';

/**
 * HTTP M2M contra `communication-recorder` (contrato 9.1). El token es el de servicio de Communication
 * (`communication-worker`); el recorder valida JWKS + `client_id`. Timeout corto: grabar no debe
 * colgar el flujo de la reunion.
 */
export class HttpRecorderClient implements RecorderClient {
  constructor(
    private readonly tokens: Pick<ServiceTokenClient, 'getToken'>,
    private readonly options: { baseUrl: string; timeoutMs: number },
  ) {}

  get configured(): boolean {
    return this.options.baseUrl.length > 0;
  }

  async startSession(tenantId: string, input: RecorderSessionStart): Promise<void> {
    await this.request(tenantId, 'POST', '/v1/sessions', input);
  }

  async addTrack(tenantId: string, sessionId: string, input: RecorderTrackStart): Promise<RecorderTrackEndpoint> {
    const body = (await this.request(tenantId, 'POST', `/v1/sessions/${sessionId}/tracks`, input)) as Partial<RecorderTrackEndpoint>;
    if (typeof body.port !== 'number' || typeof body.rtcpPort !== 'number') {
      throw new Error('Recorder returned an invalid track endpoint.');
    }
    return { ip: typeof body.ip === 'string' && body.ip.length > 0 ? body.ip : null, port: body.port, rtcpPort: body.rtcpPort };
  }

  async closeTrack(tenantId: string, sessionId: string, trackId: string): Promise<void> {
    await this.request(tenantId, 'DELETE', `/v1/sessions/${sessionId}/tracks/${trackId}`);
  }

  async stopSession(
    tenantId: string,
    sessionId: string,
    input: { stoppedAtUtc: string; reason: string; timeline: RecorderTimeline },
  ): Promise<void> {
    await this.request(tenantId, 'POST', `/v1/sessions/${sessionId}/stop`, input);
  }

  async recompose(
    tenantId: string,
    sessionId: string,
    input: { timeline: RecorderTimeline; composition: Omit<RecordingCompositionPolicy, 'captureSpatialLayer'> },
  ): Promise<void> {
    await this.request(tenantId, 'POST', `/v1/sessions/${sessionId}/recompose`, input);
  }

  async abortSession(tenantId: string, sessionId: string): Promise<void> {
    await this.request(tenantId, 'DELETE', `/v1/sessions/${sessionId}`);
  }

  async resolveHostAddress(): Promise<string> {
    const host = new URL(this.options.baseUrl).hostname;
    const { address } = await lookup(host, { family: 4 });
    return address;
  }

  private async request(tenantId: string, method: 'POST' | 'DELETE', path: string, body?: unknown): Promise<unknown> {
    if (!this.configured) throw new RecorderRequestError('Recording.RecorderNotConfigured', 503, 'The recorder is not configured.');
    const token = await this.tokens.getToken(tenantId);
    const response = await fetch(`${this.options.baseUrl}${path}`, {
      method,
      headers: {
        authorization: `Bearer ${token}`,
        ...(body !== undefined ? { 'content-type': 'application/json' } : {}),
      },
      ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
      signal: AbortSignal.timeout(this.options.timeoutMs),
    });
    if (response.status === 204) return null;
    const text = await response.text();
    const parsed: unknown = text.length > 0 ? safeJson(text) : null;
    if (!response.ok) {
      const code = (parsed as { code?: unknown } | null)?.code;
      const message = (parsed as { message?: unknown } | null)?.message;
      throw new RecorderRequestError(
        typeof code === 'string' ? code : 'Recording.RecorderError',
        response.status,
        typeof message === 'string' ? message : `Recorder answered ${response.status}.`,
      );
    }
    return parsed;
  }
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return null;
  }
}
