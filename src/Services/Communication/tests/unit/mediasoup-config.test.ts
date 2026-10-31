import { describe, expect, it } from 'vitest';
import {
  buildFallbackTransportListenInfos,
  buildWebRtcServerListenInfos,
  resolveWebRtcServerPorts,
  type SfuNetworkSettings,
} from '../../src/infrastructure/webrtc/mediasoup-config.js';

const settings: SfuNetworkSettings = {
  listenIp: '0.0.0.0',
  announcedAddress: '203.0.113.10',
  enableTcp: true,
  webRtcServerEnabled: true,
  webRtcServerPortBase: 40000,
  rtcMinPort: 40000,
  rtcMaxPort: 49999,
};

describe('mediasoup-config (WebRtcServer, D-05/D-57)', () => {
  it('asigna un puerto fijo por worker a partir de la base', () => {
    expect(resolveWebRtcServerPorts(4, settings)).toEqual([40000, 40001, 40002, 40003]);
    expect(resolveWebRtcServerPorts(0, settings)).toEqual([]);
  });

  it('el WebRtcServer escucha UDP y TCP en el MISMO puerto con la IP anunciada', () => {
    const infos = buildWebRtcServerListenInfos(2, settings);
    expect(infos).toEqual([
      { protocol: 'udp', ip: '0.0.0.0', announcedAddress: '203.0.113.10', port: 40002 },
      { protocol: 'tcp', ip: '0.0.0.0', announcedAddress: '203.0.113.10', port: 40002 },
    ]);
  });

  it('sin TCP solo publica UDP; sin IP anunciada no agrega announcedAddress (local)', () => {
    const infos = buildWebRtcServerListenInfos(0, { ...settings, enableTcp: false, announcedAddress: undefined });
    expect(infos).toEqual([{ protocol: 'udp', ip: '0.0.0.0', port: 40000 }]);
  });

  it('el respaldo sin WebRtcServer usa el rango por transport (listenInfos, no listenIps)', () => {
    const infos = buildFallbackTransportListenInfos(settings);
    expect(infos).toHaveLength(2);
    expect(infos[0]).toMatchObject({ protocol: 'udp', portRange: { min: 40000, max: 49999 } });
    expect(infos[1]).toMatchObject({ protocol: 'tcp', portRange: { min: 40000, max: 49999 } });
  });
});
