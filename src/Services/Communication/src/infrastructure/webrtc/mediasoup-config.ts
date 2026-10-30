import os from 'node:os';
import type { types as MediasoupTypes } from 'mediasoup';
import { config } from '../config.js';

/**
 * Codecs soportados por el SFU. Opus para audio (estandar de facto en
 * WebRTC); VP8 para video (soportado por todos los browsers sin licencias,
 * a diferencia de H.264 que en algunos casos requiere pagar patentes —
 * coherente con la filosofia 100% OSS del resto del stack).
 */
export const MEDIA_CODECS: MediasoupTypes.RouterRtpCodecCapability[] = [
  {
    kind: 'audio',
    mimeType: 'audio/opus',
    clockRate: 48000,
    channels: 2,
  },
  {
    kind: 'video',
    mimeType: 'video/VP8',
    clockRate: 90000,
    parameters: {
      'x-google-start-bitrate': 1000,
    },
  },
];

export const WORKER_SETTINGS: MediasoupTypes.WorkerSettings = {
  logLevel: 'warn',
  rtcMinPort: config.mediasoup.rtcMinPort,
  rtcMaxPort: config.mediasoup.rtcMaxPort,
};

/** Parametros de red del SFU, separados de `config` para poder probarlos sin entorno. */
export interface SfuNetworkSettings {
  readonly listenIp: string;
  readonly announcedAddress: string | undefined;
  readonly enableTcp: boolean;
  readonly webRtcServerEnabled: boolean;
  readonly webRtcServerPortBase: number;
  readonly rtcMinPort: number;
  readonly rtcMaxPort: number;
}

export const SFU_NETWORK_SETTINGS: SfuNetworkSettings = {
  listenIp: config.mediasoup.listenIp,
  announcedAddress: config.mediasoup.announcedAddress,
  enableTcp: config.mediasoup.enableTcp,
  webRtcServerEnabled: config.mediasoup.webRtcServerEnabled,
  webRtcServerPortBase: config.mediasoup.webRtcServerPortBase,
  rtcMinPort: config.mediasoup.rtcMinPort,
  rtcMaxPort: config.mediasoup.rtcMaxPort,
};

/** Opciones comunes a todo WebRtcTransport (con o sin WebRtcServer). */
export const WEBRTC_TRANSPORT_BASE_OPTIONS = {
  enableUdp: true,
  enableTcp: SFU_NETWORK_SETTINGS.enableTcp,
  preferUdp: true,
  initialAvailableOutgoingBitrate: 800_000,
} as const;

/**
 * 0 = auto: un worker por core disponible, con techo de 4 para no saturar
 * una maquina de desarrollo compartida con el resto de TaxVision.
 */
export function resolveNumWorkers(): number {
  if (config.mediasoup.numWorkers > 0) return config.mediasoup.numWorkers;
  return Math.max(1, Math.min(4, os.cpus().length));
}

/** Puerto fijo del worker `index` (UDP y TCP comparten numero). */
export function webRtcServerPortFor(index: number, settings: SfuNetworkSettings): number {
  return settings.webRtcServerPortBase + index;
}

/** Lista completa de puertos que el compose/router deben publicar para `numWorkers` workers. */
export function resolveWebRtcServerPorts(numWorkers: number, settings: SfuNetworkSettings): readonly number[] {
  const ports: number[] = [];
  for (let i = 0; i < numWorkers; i++) {
    ports.push(webRtcServerPortFor(i, settings));
  }
  return ports;
}

/** `listenInfos` del WebRtcServer del worker `index`: UDP siempre, TCP si esta habilitado. */
export function buildWebRtcServerListenInfos(
  index: number,
  settings: SfuNetworkSettings,
): MediasoupTypes.TransportListenInfo[] {
  const port = webRtcServerPortFor(index, settings);
  const infos: MediasoupTypes.TransportListenInfo[] = [listenInfo('udp', settings, { port })];
  if (settings.enableTcp) infos.push(listenInfo('tcp', settings, { port }));
  return infos;
}

/** Respaldo sin WebRtcServer: un puerto del rango por transport (como antes, pero con `listenInfos`). */
export function buildFallbackTransportListenInfos(
  settings: SfuNetworkSettings,
): MediasoupTypes.TransportListenInfo[] {
  const portRange = { min: settings.rtcMinPort, max: settings.rtcMaxPort };
  const infos: MediasoupTypes.TransportListenInfo[] = [listenInfo('udp', settings, { portRange })];
  if (settings.enableTcp) infos.push(listenInfo('tcp', settings, { portRange }));
  return infos;
}

function listenInfo(
  protocol: MediasoupTypes.TransportProtocol,
  settings: SfuNetworkSettings,
  where: { port: number } | { portRange: MediasoupTypes.TransportPortRange },
): MediasoupTypes.TransportListenInfo {
  return {
    protocol,
    ip: settings.listenIp,
    ...(settings.announcedAddress ? { announcedAddress: settings.announcedAddress } : {}),
    ...where,
  };
}
