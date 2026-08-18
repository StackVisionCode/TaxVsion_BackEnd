/**
 * Espejo estatico del catalogo de politicas de rate limit definido en .NET
 * (BuildingBlocks.RateLimiting.RateLimitPolicyCatalog.cs) para las politicas que
 * aplican a Communication — mismo criterio de espejo que domain/shared/permissions.ts
 * para CommunicationPermissions. Cualquier cambio de nombre en el catalogo .NET debe
 * reflejarse aca; las cuotas numericas NO se duplican aca (siguen viviendo,
 * env-configurables, en infrastructure/config.ts `rateLimit.*` — este archivo solo
 * fija el nombre canonico usado como componente de la Redis key y en headers/logs,
 * para poder correlacionar con dashboards .NET.
 *
 * RateLimit Fase 7 — categorias representadas:
 *  - O: sockets realtime, particion (tenant, user). Los 6 scopes ya estaban
 *    atomicos desde Fase 0.4 (ver socket-rate-limiter.ts) y desde la auditoria
 *    post-Fase-9 (hallazgo #9) los 6 call sites (chat-handlers.ts,
 *    call-handlers.ts, meeting-handlers.ts) pasan estas constantes como
 *    `scope` — antes usaban strings sueltos (`'chat.send'`, etc.) que
 *    rompian la correlacion con el panel de categorias del dashboard de
 *    Grafana .NET (la key Redis y la etiqueta de metrica usan `scope`
 *    directamente, ver socket-rate-limiter.ts).
 *  - D: publico con token/codigo corto, particion primaria por el propio
 *    token/codigo (anti-enumeracion). El catalogo .NET define ademas un overlay
 *    por IP para `meeting_join_by_token` — no replicado aca: el limiter HTTP
 *    global (ver http-rate-limiter.ts, sin nombre de politica formal, sin
 *    equivalente .NET) ya cubre esa funcion de forma mas simple.
 */
export const CommunicationRateLimitPolicyNames = {
  ChatSend: 'communication.o.chat_send',
  ChatEdit: 'communication.o.chat_edit',
  ChatTyping: 'communication.o.chat_typing',
  CallInitiate: 'communication.o.call_initiate',
  CallSignal: 'communication.o.call_signal',
  MeetingChatSend: 'communication.o.meeting_chat_send',
  MeetingJoinByToken: 'communication.d.meeting_join_by_token',
  MeetingJoinByCode: 'communication.d.meeting_join_by_code',
} as const;

export type CommunicationRateLimitPolicyName =
  (typeof CommunicationRateLimitPolicyNames)[keyof typeof CommunicationRateLimitPolicyNames];
