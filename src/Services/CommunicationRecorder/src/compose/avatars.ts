import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { TimelineParticipant } from '../contracts/api.js';
import type { FfmpegRunner } from './ffmpeg-runner.js';
import { STYLE } from './style.js';

/** Descarga de la foto de perfil por fileId (CloudStorage `GET storage/avatars/{fileId}`, anónimo). */
export interface AvatarSource {
  fetch(fileId: string): Promise<Buffer | null>;
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * La URL sale de la configuración del recorder, nunca de la petición: solo viaja el fileId. Sin base
 * configurada no hay fotos y el video sale con iniciales.
 */
export function createAvatarSource(opts: {
  baseUrl: string;
  timeoutMs: number;
  maxBytes: number;
  fetchImpl?: typeof fetch;
}): AvatarSource | null {
  const base = opts.baseUrl.trim().replace(/\/+$/, '');
  if (base.length === 0) return null;
  const doFetch = opts.fetchImpl ?? fetch;
  return {
    async fetch(fileId) {
      if (!UUID.test(fileId)) return null;
      const response = await doFetch(`${base}/storage/avatars/${fileId}`, { signal: AbortSignal.timeout(opts.timeoutMs) });
      if (response.status === 404) return null;
      if (!response.ok) throw new Error(`avatar request answered ${response.status}`);
      const type = response.headers.get('content-type') ?? '';
      if (!type.toLowerCase().startsWith('image/')) throw new Error(`avatar is not an image (${type})`);
      const declared = Number(response.headers.get('content-length') ?? '0');
      if (declared > opts.maxBytes) throw new Error('avatar exceeds the size limit');
      const bytes = Buffer.from(await response.arrayBuffer());
      if (bytes.length === 0) return null;
      if (bytes.length > opts.maxBytes) throw new Error('avatar exceeds the size limit');
      return bytes;
    },
  };
}

/** Recorte circular con alfa, una sola vez por foto; cada tile luego solo la escala. */
export function circleAvatarArgs(input: string, output: string, size: number): string[] {
  const r = size / 2;
  const vf = [
    `scale=${size}:${size}:force_original_aspect_ratio=increase`,
    `crop=${size}:${size}`,
    'format=rgba',
    `geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='255*lte(hypot(X-${r - 0.5},Y-${r - 0.5}),${r})'`,
  ].join(',');
  return ['-hide_banner', '-loglevel', 'error', '-y', '-i', input, '-vf', vf, '-frames:v', '1', output];
}

/**
 * Baja y recorta la foto de cada participante que la tenga → userId → PNG circular. Fail-soft: una
 * foto que no baja o no se decodifica deja a esa persona con iniciales; nunca falla la composición.
 */
export async function prepareAvatars(
  participants: readonly TimelineParticipant[],
  workDir: string,
  deps: {
    source: AvatarSource;
    runner: FfmpegRunner;
    timeoutMs: number;
    onError?: (fileId: string, err: unknown) => void;
  },
): Promise<Map<string, string>> {
  const dir = path.join(workDir, 'avatars');
  const byFile = new Map<string, string | null>();
  const byUser = new Map<string, string>();
  for (const p of participants) {
    const fileId = p.avatarFileId?.toLowerCase();
    if (!fileId || !UUID.test(fileId)) continue;
    if (!byFile.has(fileId)) byFile.set(fileId, await renderOne(fileId, dir, deps));
    const rendered = byFile.get(fileId);
    if (rendered) byUser.set(p.userId, rendered);
  }
  return byUser;
}

async function renderOne(
  fileId: string,
  dir: string,
  deps: { source: AvatarSource; runner: FfmpegRunner; timeoutMs: number; onError?: (fileId: string, err: unknown) => void },
): Promise<string | null> {
  try {
    const bytes = await deps.source.fetch(fileId);
    if (!bytes) return null;
    await mkdir(dir, { recursive: true });
    const raw = path.join(dir, `${fileId}.img`);
    const png = path.join(dir, `${fileId}.png`);
    await writeFile(raw, bytes);
    await deps.runner.run(circleAvatarArgs(raw, png, STYLE.avatarRenderPx), { timeoutMs: deps.timeoutMs });
    return png;
  } catch (err) {
    deps.onError?.(fileId, err);
    return null;
  }
}
