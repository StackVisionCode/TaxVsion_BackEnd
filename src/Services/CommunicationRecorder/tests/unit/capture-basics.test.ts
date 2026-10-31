import { createSocket, type Socket } from 'node:dgram';
import { describe, expect, it } from 'vitest';
import { buildCaptureArgs, byeTarget, rtcpByePacket } from '../../src/capture/ffmpeg-capture.js';
import { PortAllocator, PortsExhaustedError, udpPortProbe } from '../../src/capture/port-allocator.js';
import { buildSdp, rtpmapName } from '../../src/capture/sdp.js';

describe('buildSdp', () => {
  it('audio opus con canales, RTCP en puerto propio y SSRC', () => {
    const sdp = buildSdp({
      listenIp: '127.0.0.1',
      kind: 'audio',
      rtpPort: 44000,
      rtcpPort: 44001,
      codec: { mimeType: 'audio/opus', payloadType: 100, clockRate: 48000, channels: 2 },
      ssrc: 123456,
    });
    expect(sdp).toBe(
      [
        'v=0',
        'o=- 0 0 IN IP4 127.0.0.1',
        's=TaxVision recorder',
        'c=IN IP4 127.0.0.1',
        't=0 0',
        'm=audio 44000 RTP/AVP 100',
        'a=rtpmap:100 opus/48000/2',
        'a=rtcp:44001',
        'a=ssrc:123456 cname:recorder',
        'a=recvonly',
        '',
      ].join('\r\n'),
    );
  });

  it('video VP8 sin canales; usa payloadType/clockRate recibidos', () => {
    const sdp = buildSdp({
      listenIp: '0.0.0.0',
      kind: 'video',
      rtpPort: 44010,
      rtcpPort: 44011,
      codec: { mimeType: 'video/VP8', payloadType: 101, clockRate: 90000 },
      ssrc: 42,
    });
    expect(sdp).toContain('m=video 44010 RTP/AVP 101\r\n');
    expect(sdp).toContain('a=rtpmap:101 VP8/90000\r\n');
    expect(sdp).toContain('c=IN IP4 0.0.0.0\r\n');
  });

  it('IPv6 y nombres de rtpmap', () => {
    expect(buildSdp({ listenIp: '::1', kind: 'audio', rtpPort: 1, rtcpPort: 2, codec: { mimeType: 'audio/opus', payloadType: 1, clockRate: 48000 }, ssrc: 1 })).toContain('c=IN IP6 ::1');
    expect(rtpmapName('audio/OPUS')).toBe('opus');
    expect(rtpmapName('video/vp9')).toBe('VP9');
  });
});

describe('PortAllocator', () => {
  it('reparte pares pares/impares en round-robin y libera', async () => {
    const ports = new PortAllocator(44001, 44008);
    expect(ports.capacity).toBe(3); // 44002, 44004, 44006
    const a = await ports.allocate();
    const b = await ports.allocate();
    const c = await ports.allocate();
    expect([a, b, c]).toEqual([
      { rtpPort: 44002, rtcpPort: 44003 },
      { rtpPort: 44004, rtcpPort: 44005 },
      { rtpPort: 44006, rtcpPort: 44007 },
    ]);
    await expect(ports.allocate()).rejects.toBeInstanceOf(PortsExhaustedError);
    ports.release(44004);
    expect(await ports.allocate()).toEqual({ rtpPort: 44004, rtcpPort: 44005 });
    expect(ports.inUse).toBe(3);
  });

  it('no reutiliza enseguida un par recién liberado', async () => {
    const ports = new PortAllocator(44000, 44009);
    const a = await ports.allocate();
    ports.release(a.rtpPort);
    expect((await ports.allocate()).rtpPort).toBe(44002);
  });

  it('salta pares ocupados según la sonda', async () => {
    const busy = new Set([44001, 44004]);
    const ports = new PortAllocator(44000, 44007, async (p) => !busy.has(p));
    expect((await ports.allocate()).rtpPort).toBe(44002);
    expect((await ports.allocate()).rtpPort).toBe(44006);
    await expect(ports.allocate()).rejects.toBeInstanceOf(PortsExhaustedError);
  });

  it('sonda UDP real detecta un puerto ocupado', async () => {
    const socket: Socket = createSocket('udp4');
    await new Promise<void>((resolve) => socket.bind({ port: 0, address: '127.0.0.1' }, resolve));
    const port = socket.address().port;
    const probe = udpPortProbe('127.0.0.1');
    expect(await probe(port)).toBe(false);
    await new Promise<void>((resolve) => socket.close(() => resolve()));
    expect(await probe(port)).toBe(true);
  });
});

describe('capture args y RTCP BYE', () => {
  it('ffmpeg -c copy desde el SDP a WebM', () => {
    expect(buildCaptureArgs({ sdpPath: 'a.sdp', outputPath: 'a.webm' })).toEqual([
      '-hide_banner', '-loglevel', 'error', '-protocol_whitelist', 'file,udp,rtp', '-fflags', '+genpts',
      '-i', 'a.sdp', '-c', 'copy', '-flush_packets', '1', '-f', 'webm', '-y', 'a.webm',
    ]);
  });

  it('paquete compuesto RR + BYE de 16 bytes con el SSRC', () => {
    const p = rtcpByePacket(0xdeadbeef);
    expect(p.length).toBe(16);
    expect([...p.subarray(0, 4)]).toEqual([0x80, 201, 0, 1]);
    expect([...p.subarray(8, 12)]).toEqual([0x81, 203, 0, 1]);
    expect(p.readUInt32BE(12)).toBe(0xdeadbeef);
    expect(byeTarget('0.0.0.0')).toBe('127.0.0.1');
    expect(byeTarget('10.0.0.5')).toBe('10.0.0.5');
  });
});
