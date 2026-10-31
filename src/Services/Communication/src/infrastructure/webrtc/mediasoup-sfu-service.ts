import * as mediasoup from 'mediasoup';
import type { types as MediasoupTypes } from 'mediasoup';
import { logger } from '../logger/logger.js';
import {
  MEDIA_CODECS,
  SFU_NETWORK_SETTINGS,
  WEBRTC_TRANSPORT_BASE_OPTIONS,
  WORKER_SETTINGS,
  buildFallbackTransportListenInfos,
  buildWebRtcServerListenInfos,
  resolveNumWorkers,
  webRtcServerPortFor,
} from './mediasoup-config.js';
import type {
  ConsumerInfo,
  CreateTransportResult,
  RecordingTapInfo,
  RemoteProducerInfo,
  SfuMediaSource,
  SfuObserver,
  SfuRuntimeInfo,
  SfuService,
  SfuTransportDirection,
  SfuUsage,
} from '../../application/ports/sfu-service.js';

/** Lee el `source` guardado en el appData del producer (default 'camera' para producers viejos/audio). */
function producerSource(producer: MediasoupTypes.Producer): SfuMediaSource {
  const source = (producer.appData as { source?: unknown }).source;
  return source === 'screen' ? 'screen' : 'camera';
}

interface ParticipantMedia {
  sendTransport: MediasoupTypes.WebRtcTransport | null;
  recvTransport: MediasoupTypes.WebRtcTransport | null;
  producers: Map<string, MediasoupTypes.Producer>;
  consumers: Map<string, MediasoupTypes.Consumer>;
}

/** Un worker con su WebRtcServer (null = ese worker quedo en modo de respaldo por puerto ocupado). */
interface WorkerSlot {
  worker: MediasoupTypes.Worker;
  webRtcServer: MediasoupTypes.WebRtcServer | null;
  port: number;
}

/** F7: tap de grabacion = PlainTransport hacia el recorder + consumer del producer. */
interface RecordingTap {
  transport: MediasoupTypes.PlainTransport;
  consumer: MediasoupTypes.Consumer;
}

interface MeetingRoom {
  router: MediasoupTypes.Router;
  slot: WorkerSlot;
  participants: Map<string, ParticipantMedia>;
  taps: Map<string, RecordingTap>;
  tenantId: string | null;
  /** F4: muestreador de `getStats()` de la reunion (null = apagado por politica). */
  statsTimer: NodeJS.Timeout | null;
}

/** F4 (INV-03 §13.4): prioridad de entrega bajo congestion: audio > pantalla > spotlight > thumbnails. */
export const CONSUMER_PRIORITY = { audio: 255, screen: 200, spotlight: 150, thumbnail: 100 } as const;

/**
 * Implementacion mediasoup del puerto SfuService. Un Worker es un subproceso
 * nativo (mediasoup-worker) que puede hostear varios Routers; repartimos
 * routers entre workers round-robin para balancear CPU entre meetings.
 *
 * Un Router = un meeting. Cada participante tiene hasta 2 WebRtcTransport
 * (uno para subir su propio audio/video, otro para bajar el de los demas —
 * separar send/recv es la practica estandar de mediasoup, evita que un
 * problema en un lado bloquee al otro).
 *
 * F1 (D-05): cada worker tiene un WebRtcServer con UN puerto fijo (UDP y TCP) que
 * comparten todos sus transports. Son los unicos puertos que hay que publicar en Docker
 * y reenviar en el router. Si un puerto no se puede enlazar, ese worker sigue en modo de
 * respaldo (un puerto del rango por transport) y se registra: nunca se frena el SFU (D-06).
 */
export class MediasoupSfuService implements SfuService {
  private slots: WorkerSlot[] = [];
  private nextWorkerIndex = 0;
  private readonly rooms = new Map<string, MeetingRoom>();
  private observer: SfuObserver | null = null;

  async start(): Promise<void> {
    const numWorkers = resolveNumWorkers();
    for (let i = 0; i < numWorkers; i++) {
      const worker = await mediasoup.createWorker(WORKER_SETTINGS);
      worker.on('died', (err) => {
        logger.error({ err: err.message, pid: worker.pid }, 'mediasoup worker died — SFU degraded');
      });
      const port = webRtcServerPortFor(i, SFU_NETWORK_SETTINGS);
      const webRtcServer = SFU_NETWORK_SETTINGS.webRtcServerEnabled ? await this.createWebRtcServer(worker, i, port) : null;
      this.slots.push({ worker, webRtcServer, port });
    }
    logger.info(this.describe(), 'mediasoup SFU workers started');
  }

  /** Falla por puerto ocupado o IP invalida: se registra y el worker queda en modo de respaldo (D-06). */
  private async createWebRtcServer(
    worker: MediasoupTypes.Worker,
    index: number,
    port: number,
  ): Promise<MediasoupTypes.WebRtcServer | null> {
    try {
      return await worker.createWebRtcServer({
        listenInfos: buildWebRtcServerListenInfos(index, SFU_NETWORK_SETTINGS),
      });
    } catch (err) {
      logger.error(
        { err: (err as Error).message, workerIndex: index, port },
        'WebRtcServer could not bind its port — this worker falls back to one random RTC port per transport (unreachable behind Docker/NAT)',
      );
      return null;
    }
  }

  async stop(): Promise<void> {
    for (const room of this.rooms.values()) {
      if (room.statsTimer) clearInterval(room.statsTimer);
      room.router.close();
    }
    this.rooms.clear();
    for (const slot of this.slots) {
      slot.webRtcServer?.close();
      slot.worker.close();
    }
    this.slots = [];
  }

  setObserver(observer: SfuObserver | null): void {
    this.observer = observer;
  }

  async usage(): Promise<SfuUsage> {
    const rooms = [...this.rooms.values()];
    const workers = await Promise.all(
      this.slots.map(async (slot) => {
        // Un worker muerto rechaza la llamada: cuenta con ceros en vez de tumbar el latido.
        const ru = await slot.worker.getResourceUsage().catch(() => null);
        return {
          pid: slot.worker.pid,
          port: slot.webRtcServer ? slot.port : null,
          rooms: rooms.filter((room) => room.slot === slot).length,
          userCpuMs: ru?.ru_utime ?? 0,
          systemCpuMs: ru?.ru_stime ?? 0,
          maxRssKb: ru?.ru_maxrss ?? 0,
        };
      }),
    );
    return {
      rooms: rooms.length,
      participants: rooms.reduce((sum, room) => sum + room.participants.size, 0),
      workers,
    };
  }

  describe(): SfuRuntimeInfo {
    const bound = this.slots.filter((s) => s.webRtcServer !== null);
    return {
      numWorkers: this.slots.length,
      webRtcServerEnabled: SFU_NETWORK_SETTINGS.webRtcServerEnabled,
      webRtcServerPorts: bound.map((s) => s.port),
      fallbackWorkers: this.slots.length - bound.length,
      announcedAddress: SFU_NETWORK_SETTINGS.announcedAddress ?? null,
      tcpEnabled: SFU_NETWORK_SETTINGS.enableTcp,
    };
  }

  async getRouterRtpCapabilities(meetingId: string): Promise<MediasoupTypes.RtpCapabilities> {
    const room = await this.getOrCreateRoom(meetingId);
    return room.router.rtpCapabilities;
  }

  async createTransport(input: {
    tenantId: string;
    meetingId: string;
    userId: string;
    direction: SfuTransportDirection;
    maxIncomingBitrateKbps?: number | undefined;
    statsSampleSeconds?: number | undefined;
  }): Promise<CreateTransportResult> {
    const room = await this.getOrCreateRoom(input.meetingId);
    room.tenantId ??= input.tenantId;
    this.ensureStatsSampler(room, input.meetingId, input.statsSampleSeconds ?? 0);
    const participant = this.getOrCreateParticipant(room, input.userId);

    // Un transport nuevo reemplaza al anterior de la misma direccion (el cliente re-entro). Los
    // producers del send transport viejo mueren con el: se devuelven para avisar `producer_closed`.
    const existing = input.direction === 'send' ? participant.sendTransport : participant.recvTransport;
    const replacedProducerIds = existing && input.direction === 'send' ? [...participant.producers.keys()] : [];
    existing?.close();

    const transport = await room.router.createWebRtcTransport(this.transportOptions(room.slot));
    this.observeTransport(transport, input);
    if (input.direction === 'send') {
      participant.sendTransport = transport;
      // F4: tope de subida por participante (politica), para que un emisor no ahogue al resto.
      if (input.maxIncomingBitrateKbps && input.maxIncomingBitrateKbps > 0) {
        await transport.setMaxIncomingBitrate(input.maxIncomingBitrateKbps * 1000).catch((err: unknown) =>
          logger.warn({ err, transportId: transport.id }, 'setMaxIncomingBitrate failed'),
        );
      }
    } else {
      participant.recvTransport = transport;
    }

    return {
      id: transport.id,
      iceParameters: transport.iceParameters,
      iceCandidates: transport.iceCandidates,
      dtlsParameters: transport.dtlsParameters,
      replacedProducerIds,
    };
  }

  private transportOptions(slot: WorkerSlot): MediasoupTypes.WebRtcTransportOptions {
    if (slot.webRtcServer) {
      return { webRtcServer: slot.webRtcServer, ...WEBRTC_TRANSPORT_BASE_OPTIONS };
    }
    return { listenInfos: buildFallbackTransportListenInfos(SFU_NETWORK_SETTINGS), ...WEBRTC_TRANSPORT_BASE_OPTIONS };
  }

  /** ICE y DTLS del transport al observador (log, metricas, evento). mediasoup es ICE-lite: no hay 'failed' en ICE. */
  private observeTransport(
    transport: MediasoupTypes.WebRtcTransport,
    ctx: { tenantId: string; meetingId: string; userId: string; direction: SfuTransportDirection },
  ): void {
    transport.on('icestatechange', (state) => {
      this.observer?.onTransportStateChanged({
        ...ctx,
        transportId: transport.id,
        layer: 'ice',
        state,
        ...(transport.iceSelectedTuple ? { selectedProtocol: transport.iceSelectedTuple.protocol } : {}),
      });
    });
    transport.on('dtlsstatechange', (state) => {
      this.observer?.onTransportStateChanged({ ...ctx, transportId: transport.id, layer: 'dtls', state });
    });
  }

  async connectTransport(input: {
    meetingId: string;
    userId: string;
    transportId: string;
    dtlsParameters: MediasoupTypes.DtlsParameters;
  }): Promise<boolean> {
    const transport = this.findParticipantTransport(input.meetingId, input.userId, input.transportId);
    if (!transport) return false;
    await transport.connect({ dtlsParameters: input.dtlsParameters });
    return true;
  }

  async restartIce(input: {
    meetingId: string;
    userId: string;
    transportId: string;
  }): Promise<MediasoupTypes.IceParameters | null> {
    const transport = this.findParticipantTransport(input.meetingId, input.userId, input.transportId);
    if (!transport) return null;
    return transport.restartIce();
  }

  async produce(input: {
    meetingId: string;
    userId: string;
    transportId: string;
    kind: MediasoupTypes.MediaKind;
    rtpParameters: MediasoupTypes.RtpParameters;
    source?: SfuMediaSource;
  }): Promise<{ producerId: string } | null> {
    const room = this.rooms.get(input.meetingId);
    const participant = room?.participants.get(input.userId);
    const transport = participant?.sendTransport;
    if (!transport || transport.id !== input.transportId) return null;

    // El `source` viaja en el appData del producer para poder clasificar cámara vs pantalla en los
    // listados (un participante puede tener producers de video de AMBOS a la vez).
    const producer = await transport.produce({
      kind: input.kind,
      rtpParameters: input.rtpParameters,
      appData: { source: input.source ?? 'camera' },
    });
    participant.producers.set(producer.id, producer);
    producer.on('transportclose', () => participant.producers.delete(producer.id));
    // F7: cualquier cierre (transport, leave, stop de pantalla, router) avisa una sola vez a la captura.
    const lifecycle = {
      tenantId: room?.tenantId ?? '',
      meetingId: input.meetingId,
      userId: input.userId,
      producerId: producer.id,
      kind: producer.kind,
      source: producerSource(producer),
    };
    producer.observer.once('close', () => this.observer?.onProducerClosed?.(lifecycle));
    this.observer?.onProducerOpened?.(lifecycle);
    // F4: score por capa (0-10) → observador (agregado y MeetingEventLog al cruzar el umbral).
    producer.on('score', (scores) => {
      this.observer?.onProducerScore?.({
        tenantId: room?.tenantId ?? '',
        meetingId: input.meetingId,
        userId: input.userId,
        producerId: producer.id,
        kind: producer.kind,
        source: producerSource(producer),
        scores: scores.map((s) => ({ score: s.score, rid: s.rid })),
      });
    });
    return { producerId: producer.id };
  }

  async consume(input: {
    meetingId: string;
    userId: string;
    transportId: string;
    producerId: string;
    rtpCapabilities: MediasoupTypes.RtpCapabilities;
  }): Promise<ConsumerInfo | null> {
    const room = this.rooms.get(input.meetingId);
    if (!room) return null;
    if (!room.router.canConsume({ producerId: input.producerId, rtpCapabilities: input.rtpCapabilities })) {
      return null;
    }
    const participant = room.participants.get(input.userId);
    const transport = participant?.recvTransport;
    if (!transport || transport.id !== input.transportId) return null;

    // paused:true — el cliente resume() explicitamente cuando ya esta listo
    // para renderizar (patron recomendado por mediasoup, evita "flash" de
    // video negro pidiendo keyframe antes de que el consumidor este listo).
    const consumer = await transport.consume({
      producerId: input.producerId,
      rtpCapabilities: input.rtpCapabilities,
      paused: true,
    });
    participant.consumers.set(consumer.id, consumer);
    consumer.on('transportclose', () => participant.consumers.delete(consumer.id));
    consumer.on('producerclose', () => participant.consumers.delete(consumer.id));
    // F4: prioridad bajo congestion y telemetria de score/capas.
    const producerOfConsumer = this.findProducer(room, input.producerId);
    const priority =
      consumer.kind === 'audio'
        ? CONSUMER_PRIORITY.audio
        : producerOfConsumer && producerSource(producerOfConsumer) === 'screen'
          ? CONSUMER_PRIORITY.screen
          : CONSUMER_PRIORITY.thumbnail;
    await consumer.setPriority(priority).catch(() => undefined);
    const ctx = { tenantId: room.tenantId ?? '', meetingId: input.meetingId, userId: input.userId, consumerId: consumer.id };
    consumer.on('score', (score) => {
      this.observer?.onConsumerScore?.({ ...ctx, producerId: consumer.producerId, score: score.score, producerScore: score.producerScore });
    });
    consumer.on('layerschange', (layers) => {
      this.observer?.onConsumerLayersChanged?.({
        ...ctx,
        spatialLayer: layers?.spatialLayer ?? null,
        temporalLayer: layers?.temporalLayer ?? null,
      });
    });

    return {
      id: consumer.id,
      producerId: consumer.producerId,
      kind: consumer.kind,
      rtpParameters: consumer.rtpParameters,
    };
  }

  async resumeConsumer(input: { meetingId: string; userId: string; consumerId: string }): Promise<boolean> {
    const room = this.rooms.get(input.meetingId);
    const consumer = room?.participants.get(input.userId)?.consumers.get(input.consumerId);
    if (!consumer) return false;
    await consumer.resume();
    return true;
  }

  async setConsumerPreferredLayers(input: {
    meetingId: string;
    userId: string;
    consumerId: string;
    spatialLayer: number;
    temporalLayer?: number;
  }): Promise<boolean> {
    const room = this.rooms.get(input.meetingId);
    const consumer = room?.participants.get(input.userId)?.consumers.get(input.consumerId);
    if (!consumer) return false;
    // mediasoup ignora la petición si el consumer no es simulcast/SVC; clamp implícito a las capas reales.
    await consumer.setPreferredLayers({
      spatialLayer: input.spatialLayer,
      ...(input.temporalLayer !== undefined ? { temporalLayer: input.temporalLayer } : {}),
    });
    // F4: un tile grande (capa alta) se protege antes que los thumbnails; la pantalla y el audio no bajan.
    if (consumer.kind === 'video' && consumer.priority !== CONSUMER_PRIORITY.screen) {
      await consumer.setPriority(input.spatialLayer > 0 ? CONSUMER_PRIORITY.spotlight : CONSUMER_PRIORITY.thumbnail).catch(() => undefined);
    }
    return true;
  }

  listRemoteProducers(meetingId: string, excludeUserId: string): readonly RemoteProducerInfo[] {
    const room = this.rooms.get(meetingId);
    if (!room) return [];
    const result: RemoteProducerInfo[] = [];
    for (const [userId, participant] of room.participants) {
      if (userId === excludeUserId) continue;
      for (const producer of participant.producers.values()) {
        result.push({ userId, producerId: producer.id, kind: producer.kind, source: producerSource(producer) });
      }
    }
    return result;
  }

  listAllProducers(meetingId: string): readonly RemoteProducerInfo[] {
    const room = this.rooms.get(meetingId);
    if (!room) return [];
    const result: RemoteProducerInfo[] = [];
    for (const [userId, participant] of room.participants) {
      for (const producer of participant.producers.values()) {
        result.push({ userId, producerId: producer.id, kind: producer.kind, source: producerSource(producer) });
      }
    }
    return result;
  }

  async prepareRecordingTap(input: {
    meetingId: string;
    producerId: string;
    listenIp: string;
    preferredSpatialLayer?: number;
  }): Promise<RecordingTapInfo | null> {
    const room = this.rooms.get(input.meetingId);
    if (!room) return null;
    const producer = this.findProducer(room, input.producerId);
    if (!producer || producer.closed) return null;
    // Sin RTX: ffmpeg no lo entiende y en la red interna no hace falta retransmitir.
    const rtpCapabilities: MediasoupTypes.RtpCapabilities = {
      ...room.router.rtpCapabilities,
      codecs: (room.router.rtpCapabilities.codecs ?? []).filter((c) => c.mimeType.toLowerCase() !== 'video/rtx'),
    };
    const transport = await room.router.createPlainTransport({ listenInfo: { protocol: 'udp', ip: input.listenIp }, rtcpMux: false, comedia: false });
    try {
      const consumer = await transport.consume({ producerId: producer.id, rtpCapabilities, paused: true });
      if (consumer.kind === 'video' && producerSource(producer) === 'camera' && input.preferredSpatialLayer !== undefined) {
        await consumer.setPreferredLayers({ spatialLayer: input.preferredSpatialLayer }).catch(() => undefined);
      }
      const codec = consumer.rtpParameters.codecs[0];
      const ssrc = consumer.rtpParameters.encodings?.[0]?.ssrc;
      if (!codec || ssrc === undefined) {
        transport.close();
        return null;
      }
      const tapId = transport.id;
      room.taps.set(tapId, { transport, consumer });
      consumer.on('producerclose', () => this.dropTap(room, tapId));
      transport.observer.once('close', () => room.taps.delete(tapId));
      return {
        tapId,
        producerId: producer.id,
        kind: consumer.kind,
        codec: {
          mimeType: codec.mimeType,
          payloadType: codec.payloadType,
          clockRate: codec.clockRate,
          ...(codec.channels !== undefined ? { channels: codec.channels } : {}),
        },
        ssrc,
      };
    } catch (err) {
      transport.close();
      throw err;
    }
  }

  async connectRecordingTap(input: { meetingId: string; tapId: string; ip: string; port: number; rtcpPort: number }): Promise<boolean> {
    const tap = this.rooms.get(input.meetingId)?.taps.get(input.tapId);
    if (!tap) return false;
    await tap.transport.connect({ ip: input.ip, port: input.port, rtcpPort: input.rtcpPort });
    await tap.consumer.resume();
    if (tap.consumer.kind === 'video') await tap.consumer.requestKeyFrame().catch(() => undefined);
    return true;
  }

  async requestRecordingTapKeyFrame(input: { meetingId: string; tapId: string }): Promise<void> {
    const tap = this.rooms.get(input.meetingId)?.taps.get(input.tapId);
    if (tap && tap.consumer.kind === 'video' && !tap.consumer.closed) await tap.consumer.requestKeyFrame().catch(() => undefined);
  }

  async closeRecordingTap(input: { meetingId: string; tapId: string }): Promise<void> {
    const room = this.rooms.get(input.meetingId);
    if (room) this.dropTap(room, input.tapId);
  }

  private dropTap(room: MeetingRoom, tapId: string): void {
    const tap = room.taps.get(tapId);
    if (!tap) return;
    room.taps.delete(tapId);
    if (!tap.transport.closed) tap.transport.close();
  }

  listProducersForUser(meetingId: string, userId: string): readonly RemoteProducerInfo[] {
    const participant = this.rooms.get(meetingId)?.participants.get(userId);
    if (!participant) return [];
    return [...participant.producers.values()].map((p) => ({
      userId,
      producerId: p.id,
      kind: p.kind,
      source: producerSource(p),
    }));
  }

  async closeParticipant(meetingId: string, userId: string): Promise<void> {
    const room = this.rooms.get(meetingId);
    if (!room) return;
    const participant = room.participants.get(userId);
    if (!participant) return;
    participant.producers.forEach((p) => p.close());
    participant.consumers.forEach((c) => c.close());
    participant.sendTransport?.close();
    participant.recvTransport?.close();
    room.participants.delete(userId);
  }

  async closeMeeting(meetingId: string): Promise<void> {
    const room = this.rooms.get(meetingId);
    if (!room) return;
    if (room.statsTimer) clearInterval(room.statsTimer);
    room.router.close(); // cascada: cierra transports/producers/consumers de todos los participantes.
    this.rooms.delete(meetingId);
  }

  private async getOrCreateRoom(meetingId: string): Promise<MeetingRoom> {
    const existing = this.rooms.get(meetingId);
    if (existing) return existing;
    const slot = this.nextSlot();
    const router = await slot.worker.createRouter({ mediaCodecs: MEDIA_CODECS });
    const room: MeetingRoom = { router, slot, participants: new Map(), taps: new Map(), tenantId: null, statsTimer: null };
    this.rooms.set(meetingId, room);
    return room;
  }

  private getOrCreateParticipant(room: MeetingRoom, userId: string): ParticipantMedia {
    const existing = room.participants.get(userId);
    if (existing) return existing;
    const participant: ParticipantMedia = {
      sendTransport: null,
      recvTransport: null,
      producers: new Map(),
      consumers: new Map(),
    };
    room.participants.set(userId, participant);
    return participant;
  }

  private findParticipantTransport(
    meetingId: string,
    userId: string,
    transportId: string,
  ): MediasoupTypes.WebRtcTransport | null {
    const participant = this.rooms.get(meetingId)?.participants.get(userId);
    if (!participant) return null;
    if (participant.sendTransport?.id === transportId) return participant.sendTransport;
    if (participant.recvTransport?.id === transportId) return participant.recvTransport;
    return null;
  }

  private findProducer(room: MeetingRoom, producerId: string): MediasoupTypes.Producer | null {
    for (const participant of room.participants.values()) {
      const producer = participant.producers.get(producerId);
      if (producer) return producer;
    }
    return null;
  }

  /** F4: una sola muestra de `getStats()` por transport cada N s (politica); nunca por produce/consume. */
  private ensureStatsSampler(room: MeetingRoom, meetingId: string, statsSampleSeconds: number): void {
    if (room.statsTimer || statsSampleSeconds <= 0 || !this.observer?.onTransportStats) return;
    room.statsTimer = setInterval(() => {
      for (const [userId, participant] of room.participants) {
        for (const [direction, transport] of [['send', participant.sendTransport], ['recv', participant.recvTransport]] as const) {
          if (!transport || transport.closed) continue;
          void transport
            .getStats()
            .then((stats) => {
              const s = stats[0];
              if (!s) return;
              this.observer?.onTransportStats?.({
                tenantId: room.tenantId ?? '',
                meetingId,
                userId,
                direction,
                transportId: transport.id,
                bytesReceived: s.bytesReceived,
                bytesSent: s.bytesSent,
                // mediasoup no expone RTT por transport (lo mide el cliente); queda null por contrato.
                rtt: null,
                availableOutgoingBitrate: typeof s.availableOutgoingBitrate === 'number' ? s.availableOutgoingBitrate : null,
                selectedProtocol: transport.iceSelectedTuple?.protocol ?? null,
              });
            })
            .catch(() => undefined);
        }
      }
    }, statsSampleSeconds * 1000);
    room.statsTimer.unref?.();
  }

  private nextSlot(): WorkerSlot {
    const slot = this.slots[this.nextWorkerIndex];
    if (!slot) {
      throw new Error('SFU workers not started — call start() before creating rooms.');
    }
    this.nextWorkerIndex = (this.nextWorkerIndex + 1) % this.slots.length;
    return slot;
  }
}
