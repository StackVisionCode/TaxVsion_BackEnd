# Campaigns Suite — Remediación del review externo (2026-09-17)

Índice único de los 29 hallazgos del review, su **disposición**, y los documentos tocados. Autoridad de alcance: **ADR-CAMP-001 D1–D7** (Campaign sin dinero; PEP+Wallet externos/diferidos) + **ADR-CAMP-C-009/010** (modelo v2 de ejecución; autz en bus).

## Decisiones que fijaron la corrección (usuario, 2026-09-17)

1. **Semántica de entrega:** estados distintos `Accepted` (proveedor aceptó) → `Delivered` (webhook) → o `Unknown` (timeout, reconciliable). Un timeout **no** es `Failed`.
2. **Multicanal:** envío a **todos los canales seleccionados** → unidad de trabajo = **destinatario/canal**; una persona en Email+SMS = 2 unidades.
3. **Dedupe de audiencia (default, confirmar):** identidad por `(tenant, channel, destino_normalizado)`; la supresión (opt-out) prevalece; no se fusionan personas por coincidencias legítimas.

## Matriz hallazgo → disposición → documentos

| # | Prioridad | Disposición | Documentos tocados |
|---|---|---|---|
| 01 predicado de cierre con Skipped | P0 | **Corregido** — cierre por total congelado de unidades | State_Machines, Transactional_Protocol, Concurrency_Spec, Data_Model, Domain_Design |
| 02 contrato no transporta SMS | P0 | **Corregido** — `ContentRef` + `Payload` tipado por canal | Commands_And_Events, API_Contracts, sms/ (subagente) |
| 03 Accepted vs Delivered | P0 | **Decidido + corregido** — estados distintos | State_Machines, Commands_And_Events, Idempotency_Spec, email/·sms/ |
| 04 timeout→Failed | P0 | **Corregido** — `Unknown` reconciliable | State_Machines, Concurrency_Spec, Transactional_Protocol, Idempotency_Spec |
| 05 promesa de no-duplicar sobredimensionada | P0 | **Delegado (email)** — límite POST-ambiguo integrado | email-smtp2go/ (subagente) |
| 06 firma de webhook no respaldada | P0 | **Delegado (email)** — usar el mecanismo real del proveedor | email-smtp2go/ (subagente) |
| 07 estrategia de contadores contradictoria | P1 | **Corregido** — canónica: unidades=verdad, counter=caché/rollup | Concurrency_Spec, Data_Model, Domain_Design |
| 08 falta despertar durable T1→T2 | P0 | **Corregido** — `DispatchRun` en la misma tx (outbox) | Transactional_Protocol, Commands_And_Events |
| 09 identidad de intento/recipient | P1 | **Corregido** — entidad `CampaignDispatchAttempt`, ids estables | Data_Model, State_Machines, Domain_Design, Commands_And_Events |
| 10 estados/operaciones incompatibles | P1 | **Corregido** — sin `Sending`; cancel agenda vs run; `Cancelling` | State_Machines, Domain_Design, API_Contracts, Commands_And_Events, Data_Model |
| 11 cancelación no revoca lo en vuelo | P1 | **Documentado el límite** (§3.1) | Transactional_Protocol |
| 12 volumen > cola durable | P1 | **Corregido** — materialización/fan-out paginados + cuotas | Transactional_Protocol, Data_Model, Deployment |
| 13 contratos Email con otro protocolo | P0 (integración) | **Corregido** — email usa nombres canónicos; push/inapp/whatsapp aclarados como routing-key/alias del tipo canónico único | email-smtp2go/, push/, whatsapp/ |
| 14 opt-out/dedupe entre fuentes | P1 | **Confirmado (usuario)** — dedupe por identidad de destino + supresión prevalece; + política de envío confirmada: frequency cap configurable, preferencia de canal respetada, quiet hours = diferir, solape entre campañas permitido sujeto a cap | Data_Model §3b/§1.5b, Domain_Design §7.5, State_Machines, Transactional_Protocol, Deployment |
| 15 reuse Notification amplía impacto | P1 | **Delegado (email)** — aislar bulk + presupuesto de cuota | email-smtp2go/ (subagente) |
| 16 authz/multitenancy en bus | P0 | **Corregido** — authz en broker + validación al aplicar/escribir | Security, ADR (C-010) |
| 17 entitlement enforce vs log-only | P0 | **Corregido** — enforce en producción | Security, API_Contracts, State_Machines |
| 18 snapshot no reproduce contenido | P1 | **Corregido** — revisión/hash inmutable de contenido | Domain_Design, Commands_And_Events, API_Contracts |
| 19 ciclo de vida de remitentes | P1 | **Corregido** — evento `sender.status_changed`, revalidación, sin fallback | Domain_Design, Commands_And_Events |
| 20 PEP/Wallet transparente | P1 (futuro) | **Corregido (doc)** — manifiesto inmutable + identidad opaca | Transactional_Protocol §7, 05_Master_ADR D7 |
| 21 docs vigentes vs obsoletas | P1 | **Corregido** — quitada contradicción; **este doc = índice de decisiones** | Domain_Design, este doc |
| 22 persistencia incompleta | P1 | **Corregido** — `dispatch_deadline`, cursores, flags, `finished_at`, `Cancelling`, tabla de intentos | Data_Model |
| 23 idempotencia de API / Processing | P1 | **Corregido** — recuperación de Processing + alcance por endpoint | Idempotency_Spec §6.1, API_Contracts |
| 24 privacidad en mensajes/webhooks | P1 | **Corregido** — retención por superficie; redacción del crudo | Security; email-smtp2go/ (webhook) |
| 25 observabilidad: límites y SLO | P2 | **Corregido** — cardinalidad acotada, SLO por canal, auditoría actor+versión | Observability |
| 26 readiness/continuidad/despliegue | P2 | **Corregido** — degradación, DLQ/replay, reconciliación tras restore | Deployment |
| 27 orden del producto (UI temprana) | P2 | **Recomendación** — adelantar UI mínima Email/SMS | 08_Implementation_Plan (nota) |
| 28 WhatsApp: precio por conversación | P2 | **Corregido** — hoy es por mensaje entregado | 09_Open_Questions OQ-2 |
| 29 decisiones funcionales multicanal | P1 | **Corregido** — multicanal (todos los canales) + **scheduler**: misfire=coalesce, solapamiento=omitir+alertar, DST explícito, edición versionada, lease-expirado | campaigns/(Data_Model,State_Machines) + scheduler/(Domain,State_Machines,Concurrency,Commands,Data_Model,Observability,ADR-SCHED-005). Pendiente solo: preferencia/frecuencia-máx por persona (política de negocio) |

**Verificación contra el repo (2026-09-17):** hecha — ver `VERIFICATION_REPORT.md`. Todas las citas del **sistema nuevo** (acceso `campaigns.manage`/gate log-only, canales SMS/Email/Push, mensajería `PostmasterEmailEvents`/`ProcessedBusinessMessage`/`IIntegrationEvent.TenantId`/exchange `taxvision-events`, realtime Communication/Socket.IO, `Money` VO) **CONFIRMADAS** contra el código. Las citas del **CRM legado** quedan **no verificables** (el repo legado no está en el workspace, solo `campaigns.rar`) — no refutadas; describen el anti-patrón, no el contrato reusado. Scheduler/policies ya resueltas (arriba). Pendiente real restante: extraer/verificar el legado si se desea, y los contratos de Customer/Scribe (no citados con línea aún).

## Escenarios de aceptación (checklist para cuando exista código)

Propuestos por el review; **no** ejecutados contra TaxVision (solo se validaron aritméticamente los contraejemplos de #01). Deben medir comportamiento observable (aceptado/entregado/recuperado), no repetir la fórmula de implementación.

- [ ] 10 destinos, 2 opt-out, 8 entregados → cierra; Total=10, Delivered=8, Skipped=2, sin pendientes.
- [ ] Toda la audiencia omitida → cierre determinista sin esperar callbacks.
- [ ] Persona en Customer+lista+manual → la política de identidad/dedupe da un resultado reproducible.
- [ ] Misma occurrence a 2 réplicas → un run; misma respuesta de identidad.
- [ ] Solicitud manual repetida con misma clave → mismo run; clave distinta = otra intención.
- [ ] Misma clave con otro payload → `409`; no se mezcla ni re-envía.
- [ ] Crash tras confirmar `Created` → la continuación durable retoma el run.
- [ ] Crash entre 2 páginas de fan-out → reanuda el checkpoint, sin perder/recrear unidades.
- [ ] Proveedor acepta y se pierde la respuesta → evidencia `Unknown`; reconciliación antes de reenviar.
- [ ] `Delivered` tras el timeout → conserva evidencia real; reconcilia sin doble conteo.
- [ ] Dos últimos results en tx concurrentes → un único cierre tras ambos commits.
- [ ] Proveedor devuelve HTTP 200 con `failed` en el cuerpo → el adaptador lo interpreta bien.
- [ ] Webhook legítimo/repetido/falsificado → acepta autenticado, dedup, rechaza falso.
- [ ] Webhook antes de persistir `providerRef` → se retiene y reconcilia.
- [ ] Opt-out tras crear el run, antes de su envío → se aplica al trabajo aún cancelable.
- [ ] Result con tenant/identidad incorrectos → sin cambios; evento rechazado/auditado.
- [ ] Entitlement deshabilitado al ejecutar → no sale fan-out (gate enforce).
- [ ] Cancelación durante envío grande → detiene lo admisible, drena, reporta el límite real.
- [ ] Campaña grande junto a notificaciones urgentes → capacidad reservada; no supera cuota compartida.
- [ ] Restore tras mensajes ya enviados → reconciliación evita reenvíos.
