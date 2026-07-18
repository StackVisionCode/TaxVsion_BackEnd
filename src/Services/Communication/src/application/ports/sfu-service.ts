import type { types as MediasoupTypes } from 'mediasoup';

/**
 * Puerto del SFU (Selective Forwarding Unit) para meetings con mas de 4
 * participantes (`Meeting.strategy === 'Sfu'`). Reutiliza tipos de mediasoup
 * para las estructuras de datos (RtpCapabilities, DtlsParameters, etc.) —
 * son formas de datos del protocolo WebRTC, no comportamiento, mismo
 * criterio que `AuthenticatedPrincipal` reusando `JWTPayload` de `jose`.
 *
 * Estado en memoria por proceso — un meeting SFU vive en el pod que lo creo.
 * Si ese pod muere a mitad de un meeting SFU, los participantes deben
 * reconectar (limitacion documentada, no silenciosa — ver README).
 */
export interface TransportInfo {
  readonly id: string;
  readonly iceParameters: MediasoupTypes.IceParameters;
  readonly iceCandidates: readonly MediasoupTypes.IceCandidate[];
  readonly dtlsParameters: MediasoupTypes.DtlsParameters;
}

export interface ConsumerInfo {
  readonly id: string;
  readonly producerId: string;
  readonly kind: MediasoupTypes.MediaKind;
  readonly rtpParameters: MediasoupTypes.RtpParameters;
}

export interface RemoteProducerInfo {
  readonly userId: string;
  readonly producerId: string;
  readonly kind: MediasoupTypes.MediaKind;
}

export interface SfuService {
  start(): Promise<void>;
  stop(): Promise<void>;

  getRouterRtpCapabilities(meetingId: string): Promise<MediasoupTypes.RtpCapabilities>;

  createTransport(input: {
    meetingId: string;
    userId: string;
    direction: 'send' | 'recv';
  }): Promise<TransportInfo>;

  connectTransport(input: {
    meetingId: string;
    userId: string;
    transportId: string;
    dtlsParameters: MediasoupTypes.DtlsParameters;
  }): Promise<boolean>;

  produce(input: {
    meetingId: string;
    userId: string;
    transportId: string;
    kind: MediasoupTypes.MediaKind;
    rtpParameters: MediasoupTypes.RtpParameters;
  }): Promise<{ producerId: string } | null>;

  consume(input: {
    meetingId: string;
    userId: string;
    transportId: string;
    producerId: string;
    rtpCapabilities: MediasoupTypes.RtpCapabilities;
  }): Promise<ConsumerInfo | null>;

  resumeConsumer(input: { meetingId: string; userId: string; consumerId: string }): Promise<boolean>;

  /** Producers de otros participantes ya activos en el meeting — para que un recien llegado sepa a que consumir. */
  listRemoteProducers(meetingId: string, excludeUserId: string): readonly RemoteProducerInfo[];

  /** Producers propios de un participante — usado para avisar al resto antes de cerrarlos (leave/disconnect). */
  listProducersForUser(meetingId: string, userId: string): readonly RemoteProducerInfo[];

  /** Cierra transports/producers/consumers de un participante (leave/disconnect). No cierra el router. */
  closeParticipant(meetingId: string, userId: string): Promise<void>;

  /** Cierra el router completo del meeting — libera todos los recursos. Llamar al terminar el meeting. */
  closeMeeting(meetingId: string): Promise<void>;
}
