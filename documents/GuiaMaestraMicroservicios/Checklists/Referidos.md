# Checklist — Referidos

Guía: [cap. 17, Descuentos, códigos y referidos](../GUIA_MAESTRA_CREACION_MICROSERVICIOS_TAXVISION.md#cap-17) y [cap. 19, Integraciones](../GUIA_MAESTRA_CREACION_MICROSERVICIOS_TAXVISION.md#cap-19).

## Reglas de negocio
- [ ] El referido no recibe beneficio por serlo; si se quiere, es un código normal.
- [ ] El premio es del referidor (crédito de Wallet) tras el pago calificado y el plazo de espera.
- [ ] Una oficina no gana premio por referirse a sí misma.

## Flujo por eventos
- [ ] La atribución se crea con `OnboardingReferralDeclared`; un pago que llega antes reintenta (`DependencyNotReadyException`), nunca se descarta.
- [ ] `TenantOnboardingCompleted` enlaza la oficina real (y el usuario que completó el alta) y emite el código de referido de la oficina nueva.
- [ ] `TenantCreated` guarda la identidad del dueño **solo** como HMAC del email normalizado y la marca de desechable.
- [ ] Reembolso o contracargo perdido revierte el premio; disputa abierta lo retiene; disputa ganada lo devuelve.
- [ ] Cada evento publicado nuevo tiene ruta `PublishMessage` y entrada en `ignored-event-types.ts` de Communication si no lo consume.

## Antifraude
- [ ] Se evalúa al enlazar la oficina y al conocerse el email del dueño (lo que llegue último decide; idempotente).
- [ ] Mismo usuario → atribución rechazada, premio cancelado y aviso al referidor.
- [ ] Mismo email normalizado o email desechable → revisión abierta y premio en `ManualReview` (el worker no lo paga).
- [ ] Revisión por Postman: `GET growth/referrals/admin/fraud-reviews`, `approve` libera, `reject` cancela y avisa.
- [ ] Ningún email ni huella de pago en claro en las tablas de Growth.

## Avisos
- [ ] Calificado, acreditado, rechazado y revertido llegan al referidor (in-app + correo Scribe).
- [ ] El `EventId` de los avisos es un UUID válido (RFC 4122; Communication descarta otros).

## Pruebas
- [ ] Dominio: atribución, calificación, premio y antifraude.
- [ ] Aplicación: consumers en orden y fuera de orden.
- [ ] `.Integration.` en base temporal para el recorrido enlace → revisión (guardado entre oficinas).
- [ ] T-VIVO: alta referida pagada → premio acreditado; autorreferido → revisión o bloqueo.
