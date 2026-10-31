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

export interface CreateTransportResult extends TransportInfo {
  /** Producers del transport anterior de la misma direccion que se cerraron al reemplazarlo (re-join). */
  readonly replacedProducerIds: readonly string[];
}

export interface ConsumerInfo {
  readonly id: string;
  readonly producerId: string;
  readonly kind: MediasoupTypes.MediaKind;
  readonly rtpParameters: MediasoupTypes.RtpParameters;
}

export type SfuMediaSource = 'camera' | 'screen';

export type SfuTransportDirection = 'send' | 'recv';

export interface RemoteProducerInfo {
  readonly userId: string;
  readonly producerId: string;
  readonly kind: MediasoupTypes.MediaKind;
  /** 'camera' (default) o 'screen' — un participante puede producir DOS videos simultáneos. */
  readonly source: SfuMediaSource;
}

/** Cambio de estado ICE o DTLS de un transport, con el contexto para correlacionarlo. */
export interface SfuTransportStateChange {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  readonly direction: SfuTransportDirection;
  readonly transportId: string;
  readonly layer: 'ice' | 'dtls';
  readonly state: string;
  /** Tupla seleccionada (protocolo/IP remota) cuando ICE ya conecto; util para saber si entro por TCP. */
  readonly selectedProtocol?: MediasoupTypes.TransportProtocol;
}

/** F4 (INV-03 §13.1): score 0-10 que mediasoup calcula por capa del producer. */
export interface SfuProducerScore {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  readonly producerId: string;
  readonly kind: MediasoupTypes.MediaKind;
  readonly source: SfuMediaSource;
  readonly scores: ReadonlyArray<{ readonly score: number; readonly rid?: string | undefined }>;
}

export interface SfuConsumerScore {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  readonly consumerId: string;
  readonly producerId: string;
  readonly score: number;
  readonly producerScore: number;
}

export interface SfuConsumerLayersChange {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  readonly consumerId: string;
  readonly spatialLayer: number | null;
  readonly temporalLayer: number | null;
}

/** Muestra de `transport.getStats()` (una fila por transport cada `statsSampleSeconds`). */
export interface SfuTransportStatsSample {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  readonly direction: SfuTransportDirection;
  readonly transportId: string;
  readonly bytesReceived: number;
  readonly bytesSent: number;
  readonly rtt: number | null;
  readonly availableOutgoingBitrate: number | null;
  readonly selectedProtocol: MediasoupTypes.TransportProtocol | null;
}

/** F7: un producer nacio o murio (cualquier causa); la captura server-side engancha o suelta su pista. */
export interface SfuProducerLifecycle {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  readonly producerId: string;
  readonly kind: MediasoupTypes.MediaKind;
  readonly source: SfuMediaSource;
}

/** F7: lo que el recorder necesita para escribir el SDP de una pista (consumer del tap, aun pausado). */
export interface RecordingTapInfo {
  readonly tapId: string;
  readonly producerId: string;
  readonly kind: MediasoupTypes.MediaKind;
  readonly codec: { readonly mimeType: string; readonly payloadType: number; readonly clockRate: number; readonly channels?: number };
  readonly ssrc: number;
}

/** Observador que la aplicacion registra para loguear/publicar lo que pasa dentro del SFU. */
export interface SfuObserver {
  onTransportStateChanged(change: SfuTransportStateChange): void;
  onProducerOpened?(event: SfuProducerLifecycle): void;
  onProducerClosed?(event: SfuProducerLifecycle): void;
  onProducerScore?(score: SfuProducerScore): void;
  onConsumerScore?(score: SfuConsumerScore): void;
  onConsumerLayersChanged?(change: SfuConsumerLayersChange): void;
  onTransportStats?(sample: SfuTransportStatsSample): void;
}

/** Como quedo el SFU tras `start()`: lo que el guard de arranque registra y lo que el compose debe publicar. */
export interface SfuRuntimeInfo {
  readonly numWorkers: number;
  readonly webRtcServerEnabled: boolean;
  /** Puertos fijos (UDP y TCP si aplica) realmente enlazados; vacio en modo de respaldo. */
  readonly webRtcServerPorts: readonly number[];
  /** true cuando algun worker no pudo crear su WebRtcServer y usa el rango de puertos por transport. */
  readonly fallbackWorkers: number;
  readonly announcedAddress: string | null;
  readonly tcpEnabled: boolean;
}

export interface SfuService {
  start(): Promise<void>;
  stop(): Promise<void>;

  /** Registra (o reemplaza) el observador de transports. Debe llamarse antes de crear rooms. */
  setObserver(observer: SfuObserver | null): void;

  describe(): SfuRuntimeInfo;

  getRouterRtpCapabilities(meetingId: string): Promise<MediasoupTypes.RtpCapabilities>;

  createTransport(input: {
    /** Viaja al observador para que los eventos de transporte salgan con tenant sin consultar la BD. */
    tenantId: string;
    meetingId: string;
    userId: string;
    direction: SfuTransportDirection;
    /** F4: `setMaxIncomingBitrate` del transport de subida (politica); 0/undefined = sin tope. */
    maxIncomingBitrateKbps?: number | undefined;
    /** F4: cada cuantos segundos muestrear `getStats()` de los transports de la reunion; 0 = apagado. */
    statsSampleSeconds?: number | undefined;
  }): Promise<CreateTransportResult>;

  connectTransport(input: {
    meetingId: string;
    userId: string;
    transportId: string;
    dtlsParameters: MediasoupTypes.DtlsParameters;
  }): Promise<boolean>;

  /** ICE restart del lado servidor: devuelve los nuevos iceParameters que el cliente aplica con `transport.restartIce`. */
  restartIce(input: {
    meetingId: string;
    userId: string;
    transportId: string;
  }): Promise<MediasoupTypes.IceParameters | null>;

  produce(input: {
    meetingId: string;
    userId: string;
    transportId: string;
    kind: MediasoupTypes.MediaKind;
    rtpParameters: MediasoupTypes.RtpParameters;
    source?: SfuMediaSource;
  }): Promise<{ producerId: string } | null>;

  consume(input: {
    meetingId: string;
    userId: string;
    transportId: string;
    producerId: string;
    rtpCapabilities: MediasoupTypes.RtpCapabilities;
  }): Promise<ConsumerInfo | null>;

  resumeConsumer(input: { meetingId: string; userId: string; consumerId: string }): Promise<boolean>;

  /** Fija la capa de simulcast preferida de un consumidor (spotlight = alta, thumbnail = baja). */
  setConsumerPreferredLayers(input: {
    meetingId: string;
    userId: string;
    consumerId: string;
    spatialLayer: number;
    temporalLayer?: number;
  }): Promise<boolean>;

  /** Producers de otros participantes ya activos en el meeting — para que un recien llegado sepa a que consumir. */
  listRemoteProducers(meetingId: string, excludeUserId: string): readonly RemoteProducerInfo[];

  /** F7: todos los producers vivos del router (captura server-side al empezar a grabar). */
  listAllProducers(meetingId: string): readonly RemoteProducerInfo[];

  /**
   * F7 (D-28): PlainTransport + consumer PAUSADO del producer, sin conectar todavia: devuelve codec y
   * ssrc para que el recorder prepare su SDP. null si el router o el producer ya no existen.
   */
  prepareRecordingTap(input: {
    meetingId: string;
    producerId: string;
    listenIp: string;
    /** Capa simulcast que se graba (camaras); se ignora en audio y pantalla. */
    preferredSpatialLayer?: number;
  }): Promise<RecordingTapInfo | null>;

  /** F7: conecta el tap al puerto del recorder, reanuda el consumer y pide keyframe. */
  connectRecordingTap(input: { meetingId: string; tapId: string; ip: string; port: number; rtcpPort: number }): Promise<boolean>;

  requestRecordingTapKeyFrame(input: { meetingId: string; tapId: string }): Promise<void>;

  closeRecordingTap(input: { meetingId: string; tapId: string }): Promise<void>;

  /** Producers propios de un participante — usado para avisar al resto antes de cerrarlos (leave/disconnect). */
  listProducersForUser(meetingId: string, userId: string): readonly RemoteProducerInfo[];

  /** Cierra transports/producers/consumers de un participante (leave/disconnect). No cierra el router. */
  closeParticipant(meetingId: string, userId: string): Promise<void>;

  /** Cierra el router completo del meeting — libera todos los recursos. Llamar al terminar el meeting. */
  closeMeeting(meetingId: string): Promise<void>;
}
