import type { TrackCodec } from '../contracts/api.js';

export interface SdpInput {
  readonly listenIp: string;
  readonly kind: 'audio' | 'video';
  readonly rtpPort: number;
  readonly rtcpPort: number;
  readonly codec: TrackCodec;
  readonly ssrc: number;
}

/** 'audio/opus' -> 'opus', 'video/VP8' -> 'VP8' (nombre de rtpmap que espera el demuxer SDP de ffmpeg). */
export function rtpmapName(mimeType: string): string {
  const sub = mimeType.split('/')[1] ?? mimeType;
  return sub.toLowerCase() === 'opus' ? 'opus' : sub.toUpperCase();
}

function addressType(ip: string): 'IP4' | 'IP6' {
  return ip.includes(':') ? 'IP6' : 'IP4';
}

/** SDP de una pista recibida por PlainTransport (sin rtcp-mux, RTCP = RTP + 1), igual que el PoC T-31. */
export function buildSdp(input: SdpInput): string {
  const { codec } = input;
  const ipType = addressType(input.listenIp);
  const name = rtpmapName(codec.mimeType);
  const rtpmap =
    input.kind === 'audio'
      ? `${codec.payloadType} ${name}/${codec.clockRate}/${codec.channels ?? 2}`
      : `${codec.payloadType} ${name}/${codec.clockRate}`;
  const lines = [
    'v=0',
    `o=- 0 0 IN ${ipType} ${input.listenIp}`,
    's=TaxVision recorder',
    `c=IN ${ipType} ${input.listenIp}`,
    't=0 0',
    `m=${input.kind} ${input.rtpPort} RTP/AVP ${codec.payloadType}`,
    `a=rtpmap:${rtpmap}`,
    `a=rtcp:${input.rtcpPort}`,
    `a=ssrc:${input.ssrc} cname:recorder`,
    'a=recvonly',
  ];
  return `${lines.join('\r\n')}\r\n`;
}
