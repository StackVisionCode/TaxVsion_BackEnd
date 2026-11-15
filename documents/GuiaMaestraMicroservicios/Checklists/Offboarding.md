# Checklist — Offboarding (retiro de un empleado de la oficina)

> Propósito: decidir si el servicio participa en el retiro de un TenantEmployee y, si participa, incorporarlo con los 15 pasos del procedimiento real y su batería de pruebas.
> Regla de oro: *si tu servicio guarda un Guid que apunta a un empleado, decide hoy qué le pasa cuando la oficina lo retira.* El corte de acceso ya lo hizo Auth; el servicio deja los datos coherentes.

**Cuándo aplica:** todo servicio nuevo, y todo cambio que agregue a una entidad persistida una propiedad `Guid` que apunte a un usuario staff (`*UserId`, `*By`, `Assigned*Id`, `Owner*Id`, `Preparer*Id`, `Author*Id`, …).

**Formato:** *Verificación* = comando, prueba o archivo; *Origen* = investigación. Los ítems de nivel Auth están marcados `[nivel Auth]`: el servicio nuevo no los implementa, pero debe saber qué garantizan.

---

## 1. Decisión: ¿participa?

- [ ] **Inventario hecho** de toda propiedad `Guid`/`Guid?` que encaje con el patrón del fitness (`UserReferenceContractTests.cs:45-50`). En Node: campos del `schema.prisma`.
  - Verificación: `UserReferenceContractTests` (BB) en el gate falla si queda una sin clasificar.
  - Origen: [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) §10 paso 1.
- [ ] **Resultado de la decisión registrado**:
  - Solo `Provenance` y sin proyección de permisos → **no participa** (no se registra).
  - Con proyección de permisos → participa al menos con `UserLifecyclePermissionsProjectionConsumer` (patrón Billing).
  - Con `ActiveWork | Credential | Private` → participa con consumer propio **y** pre-flight.
  - Verificación: `OffboardingParticipantsContractTests` (BB) confirma que el registro coincide con los consumers reales.
  - Origen: [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) §10 paso 3.

## 2. Los 15 pasos de incorporación

- [ ] **1. Inventario de referencias** (ver §1).
  - Verificación: lista revisada en el PR. · Origen: [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) paso 1.
- [ ] **2. Clasificar cada referencia** con `[UserReference(UserReferenceKind.X, HandledBy = "...", Note = "...")]` (Prisma: `/// @userReference(...)`). `Provenance` sin `HandledBy`; el resto con `HandledBy` = clase del mismo servicio que maneja `UserOffboardedIntegrationEvent`; si lo corrige otro servicio, `HandledBy = "Servicio:Clase"` + `Via`. Falsos positivos a `NotUserReferences` con el porqué.
  - Verificación: `UserReferenceContractTests` (`Every_staff_user_reference_in_domain_declares_what_offboarding_does_with_it`). · Origen: paso 2.
- [ ] **3. Decidir si participa** (ver §1). · Origen: paso 3.
- [ ] **4. Invariantes por recurso**: quién puede heredarlo (activo, mismo tenant, permiso que exige la acción), qué pasa sin sucesor (desasignar, pausar, cancelar con aviso, pasar a quien retiró, rutar a la oficina) y qué es historial intocable.
  - Verificación: escrito en el PR y cubierto por las pruebas del paso 14. · Origen: paso 4.
- [ ] **5. Usar el evento existente** `UserOffboardedIntegrationEvent` (alias Node `auth.user.offboarded.v1`); no crear otro. **Retiro ⊇ suspensión**: si maneja `UserDeactivatedIntegrationEvent`, maneja también `UserOffboarded`.
  - Verificación: `UserReferenceContractTests.Every_service_that_handles_suspension_also_handles_offboarding`. · Origen: paso 5; [Offboarding](../Investigaciones/Offboarding.md) §5.
- [ ] **6. Consumer**: .NET `public static class <Recurso>UserOffboardedConsumer` con `Handle(UserOffboardedIntegrationEvent msg, ...)` en `Application/.../Consumers`; `correlation.Push(msg.CorrelationId ?? EventId)`; sucesor revalidado localmente; lotes con keyset; guardar antes de publicar derivados. Node: `register('auth.user.offboarded.v1', handler)`.
  - Verificación: prueba del consumer (paso 14); `HandlerNamingContractTests` (nombre singular). · Origen: paso 6.
- [ ] **7. Eventos derivados** [si el cambio interesa a otro servicio]: contrato en `src/BuildingBlocks/Messaging/<Servicio>IntegrationEvents/` con `[MessageIdentity]` y `PublishMessage<T>().ToRabbitExchange("taxvision-events")`. Nunca un campo `required` nuevo en un evento vivo.
  - Verificación: `MessageIdentityTests`, `EventRoutingContractTests` (BB). · Origen: paso 7.
- [ ] **8. Wolverine/RabbitMQ**: `UseTaxVisionRabbitMq(uri)` (enciende el acuse automático), `PersistMessagesWithSqlServer`, `UseDurableOutboxOnAllSendingEndpoints`, `UseEntityFrameworkCoreTransactions`, `AutoApplyTransactions`, `ApplyStandardFailurePolicies`, cola `<servicio>-events` enlazada con `UseDurableInbox()` e `IntegrationEventTenantMiddleware`. Si el consumer solo reenvía un comando local: `[AcknowledgesOffboardingLater]` en el consumer y `OffboardingStepNames.CompletedBy(...)` publicado por el handler que termina el trabajo, en su misma unidad con outbox.
  - Verificación: `Program.cs`; `OffboardingStepAcknowledgementPolicyTests` (BB). · Origen: paso 8; [Offboarding](../Investigaciones/Offboarding.md) §4.1.
- [ ] **9. Idempotencia por estado**: consultas que solo devuelven lo que todavía apunta al retirado; operaciones de dominio sin efecto si ya están hechas. Todos los handlers del mensaje corren en una cadena: si uno falla, los demás se repiten.
  - Verificación: prueba de reentrega (paso 14). · Origen: paso 9.
- [ ] **10. Histórico intacto**: no tocar `Provenance`, auditoría, documentos firmados/sellados, cadenas HMAC, runs enviados, `CreatedByUserId`.
  - Verificación: prueba "historial intacto". · Origen: paso 10.
- [ ] **11. Sin orden asumido**: respecto a otros servicios y a otros eventos de ciclo de vida; proyecciones con estado usan `OccurredOn` o versión.
  - Verificación: prueba de evento fuera de orden. · Origen: paso 11.
- [ ] **12. Fallos**: dependencia externa caída → lanzar (Wolverine reintenta 1 s/5 s/15 s y DLQ). Nada a medias no retomable. Llamadas irreversibles a proveedores después del commit y best-effort.
  - Verificación: prueba "si el handler falla no sale acuse" (patrón `Does_not_acknowledge_when_a_handler_of_the_chain_fails`). · Origen: paso 12.
- [ ] **13. Registro y pre-flight**: entrada en `src/Services/Auth/Application/Offboarding/OffboardingParticipants.cs` (formato fijo de literales). [si hay `ActiveWork | Credential | Private`] `GET /<svc>/offboarding-impact/{userId}` con `[HasPermission(UserManagementPermissions.UsersManage)]`, `[AllowActorTypes]`, `[RateLimit]`; si identifica por email, `POST` con `{ "email" }` en el cuerpo y `AcceptsEmail: true`.
  - Verificación: `OffboardingParticipantsContractTests` (`The_registry_lists_exactly_the_real_offboarding_consumers`, `..._offboarding_impact_endpoints`, `Every_service_with_active_work_credentials_or_private_data_exposes_offboarding_impact`). · Origen: paso 13.
- [ ] **14. Pruebas del servicio** (ver batería §3) + los tres fitness de `deploy/tests/TaxVision.BuildingBlocks.Tests/Offboarding/`. Consultas EF nuevas: prueba de traducción en `.Integration.` contra la base local (patrón `Tasks.Tests/Integration/OffboardingQueriesTranslationTests.cs`).
  - Verificación: gate exacto + `dotnet test deploy/tests/TaxVision.<Svc>.Tests/TaxVision.<Svc>.Tests.csproj --nologo --filter "FullyQualifiedName~.Integration."`. · Origen: paso 14.
- [ ] **15. Verificación manual en vivo** (el E2E automatizado lo descartó el dueño): sembrar trabajo del empleado, retirarlo desde el CRM y comprobar en "Handover status" que el paso nuevo acusa (N/N) y que el recurso quedó en el sucesor o cerrado; revisar DLQ y los logs `Offboarding step ... rejected` / `has no offboarding run`.
  - Verificación: CRM `/company/users?handover=<userId>`. · Origen: paso 15.

## 3. Batería de pruebas

Estado actual según la investigación: *Cubierto*, *Parcial* o *Hueco*. Para un servicio nuevo, cada fila marcada "servicio" es obligatoria en su proyecto de pruebas.

- [ ] **Admin autorizado** `[nivel Auth]` — retiro válido corta acceso, guarda una vez y anuncia después de guardar.
  - Verificación: `OffboardUserHandlerPathsTests.A_valid_offboarding_cuts_access_saves_once_and_announces_after_saving`. Estado: Cubierto (unitario). · Origen: [Offboarding](../Investigaciones/Offboarding.md) §9.2.
- [ ] **Actor no autorizado** `[nivel Auth]` — empleado con `users.manage` o cliente de portal como actor → 403 en el endpoint.
  - Verificación: hoy solo `AuthActorTypeArchitectureTests` (el atributo existe). Estado: **Hueco** (falta prueba de 403). En el servicio: el `offboarding-impact` exige `users.manage`. · Origen: §9.2.
- [ ] **Otro tenant** — objetivo o sucesor de otra oficina. Servicio: sucesor de otro tenant → no se asigna.
  - Verificación: Auth `A_user_of_another_office_does_not_exist` (Cubierto); servicio: prueba "sucesor inválido (otro tenant)". · Origen: §9.2; [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) paso 14.
- [ ] **Ya desvinculado** `[nivel Auth]` — segundo retiro no republica.
  - Verificación: `Offboarding_twice_publishes_only_once`, `Offboard_is_idempotent`. Estado: Cubierto. · Origen: §9.2.
- [ ] **Command repetido / simultáneo** `[nivel Auth]` — dos comandos del mismo usuario.
  - Verificación: índice único `(TenantId, UserId)` de `OffboardingRun`. Estado: **Hueco** (respuesta HTTP del segundo no probada). · Origen: §9.2.
- [ ] **Evento repetido** (servicio) — reentrega no repite efectos ni avisos.
  - Verificación: prueba `Redelivery_*` / "a second run changes nothing" en el consumer. Estado: Cubierto en la mayoría, **no uniforme**. · Origen: §9.2; [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) §6.
- [ ] **Consumer caído** — mensajes esperan en la cola durable; a los 30 min sin acuse, `UserOffboardingIncompleteIntegrationEvent` y aviso a admins.
  - Verificación: `OffboardingRunTrackingTests` (barrido, unitario). Estado: **Hueco de integración**. · Origen: §4.1 y §9.2.
- [ ] **Reintento tras fallo** (servicio) — si un handler de la cadena lanza, no sale acuse.
  - Verificación: `OffboardingStepAcknowledgementPolicyTests.Does_not_acknowledge_when_a_handler_of_the_chain_fails`. Estado: Cubierto. · Origen: §9.2.
- [ ] **Fuera de orden** (servicio) — un evento más viejo no reactiva; `UserRolesChanged` posterior al retiro no reactiva la proyección.
  - Verificación: patrón `Replayed_registered_after_offboarding_does_not_reactivate` (Notification). Estado: Parcial; **Hueco** para `UserRolesChanged` posterior (V5). · Origen: §3.3 V5 y §9.2.
- [ ] **Sesión activa** — tras retirar, el socket queda expulsado (Communication) y los .NET responden 401.
  - Verificación: `SessionAccessCutoffTests` (dobles) y `SessionDenylistMiddlewareTests`. Estado: **Hueco** de prueba encadenada "retirar → socket expulsado". · Origen: §9.2; [Session_Denylist](../Investigaciones/Session_Denylist.md).
- [ ] **JWT vigente** — request con el token viejo tras retirar → 401 `Auth.SessionRevoked`.
  - Verificación: middleware probado aislado. Estado: **Hueco** de prueba encadenada. · Origen: §9.2.
- [ ] **Acceso tras desvincular** `[nivel Auth]` — refresh/login fallan (`RefreshAccessToken.cs:132`, `user is null || !user.IsActive`).
  - Verificación: lógica compartida con suspensión. Estado: **Hueco específico de retiro**. · Origen: §3.1 y §9.2.
- [ ] **Proyección actualizada** (servicio) — `IsActive=false` en la proyección de permisos.
  - Verificación: `UserLifecyclePermissionsProjectionConsumerTests` del servicio. Estado: Cubierto en todos los servicios con proyección de permisos. · Origen: §9.2.
- [ ] **Histórico conservado** (servicio) — `Provenance` intacto.
  - Verificación: patrón `A_preparer_signature_already_given_is_never_touched` (Signature). Estado: Parcial. · Origen: §9.2.
- [ ] **Reconciliation** — reconciliaciones de directorio solo marcan inactivo; no existe reconciliación de handover.
  - Verificación: patrón `EmployeeDirectoryReconciliationTests` (Customer). Estado: Parcial. · Origen: §4.1 y §9.2.
- [ ] **Aislamiento** (servicio) — el consumer revalida el tenant; nada de otra oficina se toca.
  - Verificación: patrón `CustomerUserOffboardedConsumer.cs:81-92`. Estado: Parcial (no todos tienen caso). · Origen: §9.2.
- [ ] **Casos propios del servicio**: con sucesor, sin sucesor, sucesor inválido (otro tenant, inactivo, sin permiso), conteo del pre-flight.
  - Verificación: proyecto de pruebas del servicio. · Origen: [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) paso 14; [Offboarding](../Investigaciones/Offboarding.md) §13.8.

## 4. Diferencias que el desarrollador debe tener claras

- [ ] Suspender (reversible, `UserDeactivated`) ≠ retirar (terminal, `UserOffboarded`; Auth no emite también `UserDeactivated`) ≠ anonimizar (no existe) ≠ borrar (prohibido).
  - Verificación: tabla de operaciones. · Origen: [Offboarding](../Investigaciones/Offboarding.md) §5.
- [ ] Documentar en el servicio qué sigue pasando "en nombre" del retirado hasta que su consumer corre (ventana V3).
  - Verificación: nota en la documentación del servicio. · Origen: [Offboarding](../Investigaciones/Offboarding.md) §3.3 y §13.10.

## Decisiones pendientes

- `FailClosed` de la denylist en servicios con datos sensibles ([Offboarding](../Investigaciones/Offboarding.md) §10.6).
- Re-ejecución del handover y escalado del aviso de incompleto ([Offboarding](../Investigaciones/Offboarding.md) §10).
- Actores del pre-flight de Tenant ([Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) §6).
