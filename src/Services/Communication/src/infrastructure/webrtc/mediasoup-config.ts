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

export const WEBRTC_TRANSPORT_OPTIONS: MediasoupTypes.WebRtcTransportOptions = {
  listenIps: [
    {
      ip: config.mediasoup.listenIp,
      ...(config.mediasoup.announcedIp ? { announcedIp: config.mediasoup.announcedIp } : {}),
    },
  ],
  enableUdp: true,
  enableTcp: true,
  preferUdp: true,
  initialAvailableOutgoingBitrate: 800_000,
};

/**
 * 0 = auto: un worker por core disponible, con techo de 4 para no saturar
 * una maquina de desarrollo compartida con el resto de TaxVision.
 */
export function resolveNumWorkers(): number {
  if (config.mediasoup.numWorkers > 0) return config.mediasoup.numWorkers;
  return Math.max(1, Math.min(4, os.cpus().length));
}
