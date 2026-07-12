import * as mediasoup from 'mediasoup';
import type { types as MediasoupTypes } from 'mediasoup';
import { logger } from '../logger/logger.js';
import {
  MEDIA_CODECS,
  WEBRTC_TRANSPORT_OPTIONS,
  WORKER_SETTINGS,
  resolveNumWorkers,
} from './mediasoup-config.js';
import type {
  ConsumerInfo,
  RemoteProducerInfo,
  SfuService,
  TransportInfo,
} from '../../application/ports/sfu-service.js';

interface ParticipantMedia {
  sendTransport: MediasoupTypes.WebRtcTransport | null;
  recvTransport: MediasoupTypes.WebRtcTransport | null;
  producers: Map<string, MediasoupTypes.Producer>;
  consumers: Map<string, MediasoupTypes.Consumer>;
}

interface MeetingRoom {
  router: MediasoupTypes.Router;
  participants: Map<string, ParticipantMedia>;
}

/**
 * Implementacion mediasoup del puerto SfuService. Un Worker es un subproceso
 * nativo (mediasoup-worker) que puede hostear varios Routers; repartimos
 * routers entre workers round-robin para balancear CPU entre meetings.
 *
 * Un Router = un meeting. Cada participante tiene hasta 2 WebRtcTransport
 * (uno para subir su propio audio/video, otro para bajar el de los demas —
 * separar send/recv es la practica estandar de mediasoup, evita que un
 * problema en un lado bloquee al otro).
 */
export class MediasoupSfuService implements SfuService {
  private workers: MediasoupTypes.Worker[] = [];
  private nextWorkerIndex = 0;
  private readonly rooms = new Map<string, MeetingRoom>();

  async start(): Promise<void> {
    const numWorkers = resolveNumWorkers();
    for (let i = 0; i < numWorkers; i++) {
      const worker = await mediasoup.createWorker(WORKER_SETTINGS);
      worker.on('died', (err) => {
        logger.error({ err: err.message, pid: worker.pid }, 'mediasoup worker died — SFU degraded');
      });
      this.workers.push(worker);
    }
    logger.info({ numWorkers }, 'mediasoup SFU workers started');
  }

  async stop(): Promise<void> {
    for (const room of this.rooms.values()) {
      room.router.close();
    }
    this.rooms.clear();
    for (const worker of this.workers) {
      worker.close();
    }
    this.workers = [];
  }

  async getRouterRtpCapabilities(meetingId: string): Promise<MediasoupTypes.RtpCapabilities> {
    const room = await this.getOrCreateRoom(meetingId);
    return room.router.rtpCapabilities;
  }

  async createTransport(input: {
    meetingId: string;
    userId: string;
    direction: 'send' | 'recv';
  }): Promise<TransportInfo> {
    const room = await this.getOrCreateRoom(input.meetingId);
    const participant = this.getOrCreateParticipant(room, input.userId);

    // Un transport nuevo reemplaza al anterior de la misma direccion (ej. el
    // cliente reconecta) — cerramos el viejo para no filtrar recursos.
    const existing = input.direction === 'send' ? participant.sendTransport : participant.recvTransport;
    existing?.close();

    const transport = await room.router.createWebRtcTransport(WEBRTC_TRANSPORT_OPTIONS);
    if (input.direction === 'send') {
      participant.sendTransport = transport;
    } else {
      participant.recvTransport = transport;
    }

    return {
      id: transport.id,
      iceParameters: transport.iceParameters,
      iceCandidates: transport.iceCandidates,
      dtlsParameters: transport.dtlsParameters,
    };
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

  async produce(input: {
    meetingId: string;
    userId: string;
    transportId: string;
    kind: MediasoupTypes.MediaKind;
    rtpParameters: MediasoupTypes.RtpParameters;
  }): Promise<{ producerId: string } | null> {
    const room = this.rooms.get(input.meetingId);
    const participant = room?.participants.get(input.userId);
    const transport = participant?.sendTransport;
    if (!transport || transport.id !== input.transportId) return null;

    const producer = await transport.produce({ kind: input.kind, rtpParameters: input.rtpParameters });
    participant.producers.set(producer.id, producer);
    producer.on('transportclose', () => participant.producers.delete(producer.id));
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

  listRemoteProducers(meetingId: string, excludeUserId: string): readonly RemoteProducerInfo[] {
    const room = this.rooms.get(meetingId);
    if (!room) return [];
    const result: RemoteProducerInfo[] = [];
    for (const [userId, participant] of room.participants) {
      if (userId === excludeUserId) continue;
      for (const producer of participant.producers.values()) {
        result.push({ userId, producerId: producer.id, kind: producer.kind });
      }
    }
    return result;
  }

  listProducersForUser(meetingId: string, userId: string): readonly RemoteProducerInfo[] {
    const participant = this.rooms.get(meetingId)?.participants.get(userId);
    if (!participant) return [];
    return [...participant.producers.values()].map((p) => ({ userId, producerId: p.id, kind: p.kind }));
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
    room.router.close(); // cascada: cierra transports/producers/consumers de todos los participantes.
    this.rooms.delete(meetingId);
  }

  private async getOrCreateRoom(meetingId: string): Promise<MeetingRoom> {
    const existing = this.rooms.get(meetingId);
    if (existing) return existing;
    const worker = this.nextWorker();
    const router = await worker.createRouter({ mediaCodecs: MEDIA_CODECS });
    const room: MeetingRoom = { router, participants: new Map() };
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

  private nextWorker(): MediasoupTypes.Worker {
    const worker = this.workers[this.nextWorkerIndex];
    if (!worker) {
      throw new Error('SFU workers not started — call start() before creating rooms.');
    }
    this.nextWorkerIndex = (this.nextWorkerIndex + 1) % this.workers.length;
    return worker;
  }
}
