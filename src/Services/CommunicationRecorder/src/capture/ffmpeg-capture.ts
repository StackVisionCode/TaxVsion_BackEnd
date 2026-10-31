import { spawn, type ChildProcess } from 'node:child_process';
import { createSocket } from 'node:dgram';
import { createWriteStream } from 'node:fs';
import { readFile } from 'node:fs/promises';

export interface CaptureSpec {
  readonly sdpPath: string;
  readonly outputPath: string;
  readonly logPath: string;
  /** Destino del RTCP BYE de cierre (puerto RTCP de este ffmpeg) y SSRC de la pista. */
  readonly rtcp: { readonly ip: string; readonly port: number; readonly ssrc: number };
}

export interface CaptureHandle {
  isAlive(): boolean;
  /** Cierre ordenado (RTCP BYE + 'q' + SIGINT en POSIX) y kill al vencer el plazo. */
  stop(timeoutMs: number): Promise<void>;
  /** Se resuelve cuando el proceso termina (por cualquier causa). */
  readonly exited: Promise<number | null>;
}

export interface CaptureLauncher {
  launch(spec: CaptureSpec): Promise<CaptureHandle>;
}

export class CaptureStartError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'CaptureStartError';
  }
}

/** Argumentos del grabador por pista (PoC T-31): RTP por SDP y WebM sin recodificar. */
export function buildCaptureArgs(spec: Pick<CaptureSpec, 'sdpPath' | 'outputPath'>): string[] {
  return [
    '-hide_banner',
    '-loglevel',
    'error',
    '-protocol_whitelist',
    'file,udp,rtp',
    '-fflags',
    '+genpts',
    '-i',
    spec.sdpPath,
    '-c',
    'copy',
    // Si hubiera que matarlo, en disco queda todo salvo el último cluster (no el búfer de AVIO).
    '-flush_packets',
    '1',
    '-f',
    'webm',
    '-y',
    spec.outputPath,
  ];
}

/**
 * Paquete RTCP compuesto RR vacío + BYE (RFC 3550 §6.1/§6.6). 16 bytes: el demuxer RTP de ffmpeg
 * descarta paquetes de menos de 12 antes de mirar si son RTCP, así que un BYE suelto (8) no basta.
 */
export function rtcpByePacket(ssrc: number): Buffer {
  const buf = Buffer.alloc(16);
  buf.writeUInt8(0x80, 0); // V=2, RC=0
  buf.writeUInt8(201, 1); // RR
  buf.writeUInt16BE(1, 2);
  buf.writeUInt32BE(ssrc >>> 0, 4);
  buf.writeUInt8(0x81, 8); // V=2, SC=1
  buf.writeUInt8(203, 9); // BYE
  buf.writeUInt16BE(1, 10);
  buf.writeUInt32BE(ssrc >>> 0, 12);
  return buf;
}

/** 0.0.0.0/:: no es destino válido: se envía a loopback (ffmpeg enlaza el RTCP en todas las interfaces). */
export function byeTarget(listenIp: string): string {
  if (listenIp === '0.0.0.0') return '127.0.0.1';
  if (listenIp === '::') return '::1';
  return listenIp;
}

function sendBye(target: CaptureSpec['rtcp']): Promise<void> {
  return new Promise((resolve) => {
    const socket = createSocket(target.ip.includes(':') ? 'udp6' : 'udp4');
    const packet = rtcpByePacket(target.ssrc);
    socket.once('error', () => {
      socket.close();
      resolve();
    });
    socket.send(packet, target.port, target.ip, () => {
      socket.close();
      resolve();
    });
  });
}

const delay = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

class FfmpegCaptureHandle implements CaptureHandle {
  readonly exited: Promise<number | null>;
  private alive = true;

  constructor(
    private readonly child: ChildProcess,
    private readonly rtcp: CaptureSpec['rtcp'],
    private readonly platform: NodeJS.Platform,
  ) {
    this.exited = new Promise((resolve) => {
      child.once('exit', (code) => {
        this.alive = false;
        resolve(code);
      });
      child.once('error', () => {
        this.alive = false;
        resolve(null);
      });
    });
  }

  isAlive(): boolean {
    return this.alive;
  }

  async stop(timeoutMs: number): Promise<void> {
    if (!this.alive) return;
    // Sin paquetes, ffmpeg queda bloqueado leyendo el socket y no atiende 'q'. Un RTCP BYE hace que el
    // demuxer RTP dé EOF, escriba el trailer WebM y salga; funciona igual en Windows y Linux.
    await sendBye(this.rtcp);
    try {
      this.child.stdin?.write('q');
      this.child.stdin?.end();
    } catch {
      // stdin ya cerrado
    }
    if (this.platform !== 'win32') this.child.kill('SIGINT');
    const result = await Promise.race([
      this.exited.then(() => 'exited' as const),
      delay(timeoutMs).then(() => 'timeout' as const),
    ]);
    if (result === 'timeout' && this.alive) {
      this.child.kill('SIGKILL');
      await this.exited;
    }
  }
}

export function createFfmpegCaptureLauncher(opts: {
  ffmpegPath: string;
  readyDelayMs: number;
  platform?: NodeJS.Platform;
}): CaptureLauncher {
  const platform = opts.platform ?? process.platform;
  return {
    async launch(spec: CaptureSpec): Promise<CaptureHandle> {
      const log = createWriteStream(spec.logPath, { flags: 'a' });
      const child = spawn(opts.ffmpegPath, buildCaptureArgs(spec), {
        stdio: ['pipe', 'ignore', 'pipe'],
        windowsHide: true,
      });
      child.stderr?.pipe(log);
      const handle = new FfmpegCaptureHandle(child, spec.rtcp, platform);
      // ffmpeg abre el socket al leer el SDP: se espera un margen y se comprueba que siga vivo.
      await Promise.race([delay(opts.readyDelayMs), handle.exited]);
      if (!handle.isAlive()) {
        const tail = await readFile(spec.logPath, 'utf-8').catch(() => '');
        throw new CaptureStartError(`ffmpeg exited during startup: ${tail.slice(-500).trim() || 'no output'}`);
      }
      return handle;
    },
  };
}
