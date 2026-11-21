# Offboarding — integraciones por microservicio

> Frente 3 de la Guía Maestra. Investigación de **solo lectura** (2026-11-15). Complementa `Offboarding.md`.
> Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`. Pruebas en `deploy/tests/TaxVision.<Servicio>.Tests/` salvo Communication (`src/Services/Communication/tests/unit/`).
> Etiquetas: **Código verificado**, **Documentación histórica**, **Recomendación**, **Decisión pendiente**.

---

## 1. Estado actual

**Código verificado.**
- Evento único de entrada: `UserOffboardedIntegrationEvent` (`src/BuildingBlocks/Messaging/AuthIntegrationEvents/UserOffboardedIntegrationEvent.cs`), fanout `taxvision-events`, una cola durable por servicio (`<servicio>-events`; Auth `auth-tenant-events`; Communication `communication-events`, alias `auth.user.offboarded.v1`).
- Registro de participantes: `src/Services/Auth/Application/Offboarding/OffboardingParticipants.cs:25-144` → **28 servicios, 52 pasos**. Cada paso = clase handler .NET (o archivo `.ts` en Node) que debe acusar.
- Consumidor transversal en 25 servicios: `UserLifecyclePermissionsProjectionConsumer` (Notification: `AuthzUserLifecyclePermissionsProjectionConsumer` + `UserOffboardedPermissionsProjectionConsumer`). Mueve `IsActive=false` en la proyección de permisos; `ProjectionPermissionsSource` falla cerrado si no encuentra proyección activa (`src/BuildingBlocks/BuildingBlocks.Web/ActorTypeAuthorization/ProjectionPermissionsSource.cs:99-112`). Patrón: `Billing.Application/Permissions/Consumers/UserLifecyclePermissionsProjectionConsumer.cs:15-70`.
- Pre-flight: 10 rutas `offboarding-impact`, todas con `users.manage` (Calendar, Campaigns, CloudStorage, Communication, Connectors, Correspondence, Customer, Signature, Tasks, Tenant).
- Anotaciones `[UserReference]` (conteo aproximado por grep de `UserReferenceKind.*` / `@userReference(`): ActiveWork en Auth 1, Calendar 4, Campaigns 2, Communication 14, Correspondence 1, Customer 3, Signature 2, Tasks 8, Tenant 1; Credential en Auth 11, Calendar 1, CloudStorage 4, Connectors 2, Notification 1; Private en Calendar 2, Notification 1, Reminder 1, Signature 1, Tasks 1. Lista de servicios pendientes de anotar: vacía (`UserReferenceContractTests.cs:35`).

---

## 2. Matriz por microservicio

Leyenda de Estado: **Participa** · **No participa** (con justificación) · **Participación incompleta** · **Pendiente de confirmar**.
"Seguridad" = qué acceso o exposición corta el paso. "Proy." = solo proyección de permisos.

| Microservicio | Recursos del empleado | Evento consumido | Acción | Seguridad | Idempotencia | Test | Estado |
|---|---|---|---|---|---|---|---|
| **Auth** (dueño) | Usuario, sesiones, refresh, MFA, invitaciones enviadas, avatar, asiento (prioridad de slot), `OffboardingRun` | Origina `UserOffboarded`; consume el suyo con `RecomputeTenantSeatSlotsConsumer` (`Application/SlotGating/Consumers/RecomputeTenantSeatSlotsConsumer.cs:22`); consume acuses (`OffboardingStepCompletedConsumer`) | Estado terminal, corte de acceso, higiene, ficha, recálculo de asientos del tenant | Total e inmediata (denylist, Pub/Sub, BD, login/refresh) | `Offboard` idempotente; run único `(TenantId,UserId)`; acuses por `Service+Step` | `OffboardUserHandlerPathsTests`, `UserManagementCommandsTests`, `UserLifecycleTests`, `OffboardingRunTrackingTests`, `OffboardedEmailReuseTests`, `LastAdminGuardConcurrencyIntegrationTests` | Participa |
| **Billing** | Proy.; procedencia (facturas, recibos) | `UserOffboarded` | `IsActive=false` en proyección | Fail-closed de permisos | No-op si ya inactiva o no existe | `Permissions/UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Calendar** | Citas que organiza (ActiveWork), asistente interno, `CalendarFeedToken` (Credential), `AvailabilityRule`/`BlockedTime` (Private), anfitrión de demos públicas (`BookingPageHost`, `PublicBooking`) | `UserOffboarded` | `CalendarUserOffboardedConsumer`: revoca feed token (+ caché), reasigna citas vigentes al sucesor o las cancela (publica `AppointmentCancelled` + `ReminderTargetClosed`), pide recordatorio del sucesor, lo saca como asistente, desactiva reglas y borra bloqueos. `PublicBookingHostOffboardedConsumer`: lo saca de sus páginas (la página sin anfitriones se apaga), pasa cada demo futura a otro anfitrión libre o la cancela con correo al visitante | Feed iCal secreto revocado; deja de ser agendable | Solo vigentes/no canceladas entran a la consulta | `Application/CalendarUserOffboardedConsumerTests.cs` (13), `PublicBookingPlatformTests.cs` (casos de retiro), `UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Campaigns** | `CampaignSchedule` que dispara en su nombre (RunAs, ActiveWork); procedencia `CreatedByUserId` y runs | `UserOffboarded` | `CampaignSchedulesUserOffboardedConsumer`: pasa el agendado al sucesor (activo + `campaigns.send`) con la visibilidad del sucesor; sin sucesor lo pausa y publica `CampaignSchedulesPausedOnOffboardingIntegrationEvent` | Deja de enviar con la visibilidad del retirado | Lo traspasado deja de ser suyo; lo pausado queda en la constancia | `CampaignSchedulesUserOffboardedConsumerTests.cs` (incluye `A_redelivered_event_does_not_pause_record_or_notify_twice`), `GetCampaignsOffboardingImpactHandlerTests.cs` | Participa |
| **Catalog** | Proy.; procedencia | `UserOffboarded` | Proyección inactiva | Permisos | Sí | `UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **CloudStorage** | Share links que creó (Credential), destinatario de links de otros, `TenantCloudConnection` de la oficina conectada con su cuenta, `state` OAuth pendientes; archivos/carpetas `OwnerType.User` (procedencia, no se mueven) | `UserOffboarded` | `CloudStorageUserOffboardedConsumer`: revoca links activos que creó (audita `share.revoke`, publica `ShareLinkRevokedIntegrationEvent`), lo quita de destinatarios (link sin destinatarios → revocado). `CloudConnectionsUserOffboardedConsumer`: marca la conexión de oficina para revisión (no la revoca) y publica `CloudConnectionReviewRequiredIntegrationEvent`; descarta sus `state` OAuth | Links públicos del retirado dejan de servir | Lo revocado/marcado no reentra; reconexión posterior no se toca | `CloudStorageUserOffboardedConsumerTests.cs`, `CloudConnectionsUserOffboardedConsumerTests.cs`, `Domain/TenantCloudConnectionTests.cs` | Participa (archivos personales sin reasignar: decisión histórica "solo admin los ve") |
| **Communication** (Node) | Reuniones que hospeda, participación/invitaciones en reuniones de otros, grupos (propietario/miembro), chats Direct con clientes y staff, tickets de soporte (dueño o agente), grabaciones Client que capturaba, 3 proyecciones (permisos, directorio, cuentas portal) | `auth.user.offboarded.v1` (`src/application/event-handlers/offboarding-consumers.ts:72`) | Inactiva proyecciones; reuniones activas → sucesor (`reassignHostBySystem`, publica `MeetingHostTransferred`) o cancela/termina; sale de reuniones e invitaciones de otros; sale de grupos con sucesión Owner→Admin→staff (archivados incluidos); chats Direct con clientes se cierran (solo lectura, mensaje de sistema) y sigue el sucesor o la oficina; DM staff queda solo lectura; tickets al sucesor o al admin; tickets de agente de plataforma vuelven a la cola; grabaciones Client se cierran por sistema; acuse a mano | Expulsión de sockets vía Pub/Sub (`main.ts:513`); HTTP y handshake fail-closed con denylist (`jwt-verifier.ts:94-114`) | Inbox `ProcessedEvent` + cada paso actúa solo sobre lo que sigue apuntando al retirado | `offboarding-consumers.test.ts` (11), `offboarding-handover.test.ts` (22), `meeting-offboard-handover.test.ts` (8), `group-administration.test.ts` | Participa (riesgo V4: caída del proceso deja el inbox marcado) |
| **CommunicationRecorder** (Node) | Ninguno persistido propio (compone grabaciones) | — | — | — | — | — | No participa: worker sin datos de empleado propios. Su cola en Redis solo guarda `jobId`, `sessionId` y `mode`; la sesión de grabación vive en Communication y los archivos los registra CloudStorage (`ownerId` del evento de guardado) |
| **CommunicationTranscriptWorker** (Node) | Ninguno persistido propio | — | — | — | — | — | No participa: worker de transcripción. En Redis solo guarda el `eventId` de idempotencia con vencimiento; los transcripts se guardan vía CloudStorage/Communication |
| **Connectors** | Buzones PERSONALES (`TenantEmailAccount.OwnerUserId`, Credential) con OAuth/IMAP/SMTP; buzones de OFICINA ligados a él (su dirección, los creó o conectó) | `UserOffboarded` | `ConnectorsUserOffboardedConsumer` (`[AcknowledgesOffboardingLater]`): reenvía `PurgeOffboardedMailboxesCommand`; `PurgeOffboardedMailboxesHandler` (`[NonTransactional]`, unidad corta con outbox): desconecta, purga OAuth/IMAP/SMTP, para renovaciones de push, publica `ConnectorsTenantEmailAccountDisconnected` y el acuse del paso en la misma unidad (también sin buzones personales), y revoca en el proveedor best-effort después del commit. `OfficeMailboxesUserOffboardedConsumer`: marca para revisión y publica `ConnectorsOfficeMailboxReviewRequiredIntegrationEvent` | Corta polling/envío con sus tokens | Credenciales ya borradas se saltan; buzón ya marcado no se re-avisa | `ConnectorsUserOffboardedConsumerTests.cs`, `PurgeOffboardedMailboxesHandlerTests.cs` (9), `OfficeMailboxesUserOffboardedConsumerTests.cs`, `Integration/OffboardingPurgeTransactionIntegrationTests.cs` | Participa |
| **ContentScanner** | Ninguno | — | — | — | — | — | No participa: escáner sin bus ni datos de usuario (`Program.cs` sin outbox ni Rabbit) |
| **Correspondence** | Borradores abiertos (`Draft.CreatedByUserId`, ActiveWork); `MailboxOwnership` (proyección corregida por Connectors) | `UserOffboarded` | `CorrespondenceUserOffboardedConsumer`: borrador → sucesor; si salía de un buzón personal purgado → buzón de oficina por defecto o se descarta; sin sucesor → descarta. Enviados intactos. Lanza si Connectors no responde (reintento) | — | Al reprocesar ya no quedan borradores abiertos del autor | `Compose/CorrespondenceUserOffboardedConsumerTests.cs`, `Projections/UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Customer** | Preparador primario (`AssignedPreparerUserId`), accesos adicionales (`CustomerAssignment`), clientes archivados, directorio de empleados, imports en curso (aviso de fin) | `UserOffboarded` | `CustomerUserOffboardedConsumer`: directorio `MarkOffboarded` (terminal); primarios → sucesor elegible (`HandOverPreparer`, publica `CustomerPreparerAssigned`) o desasigna (`CustomerPreparerUnassigned`); revoca accesos adicionales; suelta archivados; visibilidad ABAC por `CustomerAssignmentsChangedIntegrationEvent`. `CustomerImportsUserOffboardedConsumer`: el aviso de fin del import pasa a quien retiró o al sucesor activo | Pierde visibilidad ABAC sobre clientes | Keyset por `Id` en lotes de 200; lo resuelto sale del filtro | `CustomerUserOffboardedConsumerTests.cs` (11, incluye `Redelivery_after_the_archived_sweep_changes_nothing`), `CustomerImportsUserOffboardedConsumerTests.cs`, `OffboardingImpactHandlerTests.cs`, `Domain/CustomerOffboardedStaffReleaseTests.cs`, `EmployeeDirectoryReconciliationTests.cs` | Participa |
| **Documents** | Proy. | `UserOffboarded` | Proyección inactiva | Permisos | Sí | `Permissions/UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **FinancialInsights** | Proy.; procedencia | `UserOffboarded` | Proyección inactiva | Permisos | Sí | `Application/ProjectionConsumerTests.cs` | Participa |
| **Growth** | Proy.; procedencia (referidos) | `UserOffboarded` | Proyección inactiva | Permisos | Sí | `Permissions/UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Inventory** | Proy.; procedencia | `UserOffboarded` | Proyección inactiva | Permisos | Sí | `UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Mobile** | Proy. (vehículos, viajes y tarifas son de clientes del portal y de la oficina; sin referencias a staff ni trabajo que reasignar: `src/Services/Mobile/README.md:15`) | `UserOffboarded` | `UserLifecyclePermissionsProjectionConsumer` (`Mobile.Application/Projections/Consumers/UserLifecyclePermissionsProjectionConsumer.cs:30-47`): proyección inactiva | Permisos | Sí (`SetActive` con `OccurredOn`) | Sin prueba propia del consumer; lo cubren los fitness de offboarding | Participa (`OffboardingParticipants.cs:28`) |
| **Notes** | Notas propias (autoría = procedencia), `OffboardedStaffProjection` | `UserOffboarded` | `NotesUserOffboardedConsumer`: registra al retirado; habilita override de edición para quien tiene `notes.view_all` (`OffboardedAuthorNoteEditHandler`, capa ASP.NET aditiva). No transfiere autoría | — (habilita gobierno, no corta) | Ya registrado = no-op | `NotesUserOffboardedConsumerTests.cs` (`Is_idempotent_when_the_user_is_already_registered`), `Api/OffboardedAuthorNoteEditHandlerTests.cs`, `NoteVisibilityPolicyTests.cs`, `UpdateNoteContentHandlerTests.cs` | Participa |
| **Notification** | Directorio de correo, preferencias (Private), `PushDeviceToken` (Credential), proyección de audiencias por permiso, proyección de autorización, proyecciones dueño/portal | `UserOffboarded` (5 handlers); además consume los avisos derivados (`UserOffboardingIncomplete`, `OfficeSecurityCoordinatorVacated`, `SignaturePreparerChanged/AttentionRequired`, `CampaignSchedulesPausedOnOffboarding`, `CloudConnectionReviewRequired`, `ConnectorsOfficeMailboxReviewRequired`) | Directorio inactivo con marca de tiempo (corta correo, SMS, push e in-app); revoca todos los dispositivos push; sale de audiencias ByPermission; proyección authz inactiva; dueño/portal inactivo. Envía los avisos a admins (`Consumers/Offboarding/*`) | Deja de recibir datos de clientes por correo/push | `SetActive(false, OccurredOn)` ignora eventos más viejos; push redelivery no-op | `Lifecycle/UserEmailDirectoryOffboardingConsumerTests.cs`, `UserEmailDirectoryConsumersTests.cs` (`Replayed_registered_after_offboarding_does_not_reactivate`), `UserLifecyclePushDeviceConsumerTests.cs`, `UserOffboardedPermissionsProjectionConsumerTests.cs`, `Authorization/AuthzUserLifecyclePermissionsProjectionConsumerTests.cs`, `Offboarding/*ConsumerTests.cs` (7) | Participa |
| **PaymentApp** | Proy.; procedencia | `UserOffboarded` | Proyección inactiva | Permisos | Sí | `UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **PaymentClient** | Proy.; procedencia | `UserOffboarded` | Proyección inactiva | Permisos | Sí | `UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Postmaster** | Proy.; envíos por buzón (vía evento de Connectors) | `UserOffboarded` (+ `ConnectorsTenantEmailAccountDisconnected`, Documentación histórica) | Proyección inactiva | Permisos | Sí | `Consumers/UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Reminder** | Recordatorios propios (General, Task, Calendar, Note; Private) | `UserOffboarded` | `ReminderUserOffboardedConsumer`: cancela y desagenda (Quartz) **todos** sus pendientes; persiste primero, desagenda después; el job de reconciliación cubre el hueco | Deja de recibir avisos con datos | Cancelado = no-op | `Reminders/ReminderUserOffboardedConsumerTests.cs` (3), `Permissions/...ConsumerTests.cs` | Participa |
| **Scribe** | Proy.; procedencia (plantillas) | `UserOffboarded` | Proyección inactiva | Permisos | Sí | `Projections/UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Signature** | Perfiles de firma personales (Private), solicitudes sin enviar que escribió, preparador de borradores y de enviadas sin firmar (`Preparer.UserId`, ActiveWork); cadena HMAC e identidad 8879 firmada (procedencia) | `UserOffboarded` | `SignatureUserOffboardedConsumer`: archiva perfiles personales. `SignatureRequestsUserOffboardedConsumer`: borradores → sucesor o se descartan (programados, legal hold o externos se conservan con aviso); preparador en borradores se quita y avisa al autor; en enviadas sin firma del preparador → sucesor con identidad 8879 propia (auditado, `SignaturePreparerChangedIntegrationEvent`) o aviso a admins (`SignaturePreparerAttentionRequiredIntegrationEvent`) | Su firma deja de ser seleccionable | Lo resuelto deja de coincidir; sucesor validado contra proyección local | `SignatureUserOffboardedConsumerTests.cs`, `Offboarding/SignatureRequestsUserOffboardedConsumerTests.cs` (incluye `Redelivery_does_not_repeat_what_was_already_resolved`, `A_preparer_signature_already_given_is_never_touched`), `ChangePreparerHandlerTests.cs`, `PreparerReassignerTests.cs`, `Domain/SignatureRequestPreparerChangeTests.cs` | Participa (Decisión pendiente histórica D-2: confirmar con criterio contable si el documento firmado muestra nombre/PTIN del preparador) |
| **Sms** | Proy. | `UserOffboarded` | Proyección inactiva | Permisos | Sí | `UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Subscription** | Proy.; procedencia | `UserOffboarded` | Proyección inactiva. El asiento lo recalcula Auth (`RecomputeTenantSeatSlotsConsumer`); el antiguo `UserLifecycleSeatConsumer` ya no existe | Permisos | Sí | `UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Tasks** (incluye Client Forms) | Tareas abiertas asignadas, series (asignado y gestor), timers en marcha, `ClientRequest` (avisos), `FormIntakeLink.ResponsibleUserId`, `FormRequest.ResponsibleUserId` | `UserOffboarded` | `TasksUserOffboardedConsumer`: tareas → sucesor (pide recordatorio) o desasigna; series → sucesor o pausa; gestor → sucesor o quien retiró. `TaskTimersUserOffboardedConsumer`: cierra timers a la hora del retiro. `ClientRequestsUserOffboardedConsumer`: avisos → asignado de la tarea, sucesor o quien retiró (activo). `FormIntakeLinksUserOffboardedConsumer`: enlace → sucesor elegible o pausa con auditoría. `FormRequestsUserOffboardedConsumer`: requests abiertas → sucesor o quien retiró (activo + `tasks.form_requests.review`); si nadie es elegible, aviso | Enlace público deja de crear leads a nombre del retirado | Lo reasignado sale del filtro; lo pausado no se repite | `Tasks/TasksUserOffboardedConsumerTests.cs` (7), `Tasks/TaskTimersUserOffboardedConsumerTests.cs`, `ClientRequests/ClientRequestsUserOffboardedConsumerTests.cs`, `ClientForms/FormIntakeLinksUserOffboardedConsumerTests.cs` (`Redelivery_does_not_flag_an_already_paused_link_again`), `ClientForms/FormRequestsUserOffboardedConsumerTests.cs`, `Integration/OffboardingQueriesTranslationTests.cs`, `Tasks/OffboardingImpactHandlerTests.cs`, `ClientForms/ClientFormsOffboardingImpactHandlerTests.cs` | Participa |
| **Tenant** | Coordinador de seguridad WISP (`OfficeCompliance`, ActiveWork) | `UserOffboarded` | `SecurityCoordinatorOffboardedConsumer`: deja la designación vacante (no pasa al sucesor) y publica `OfficeSecurityCoordinatorVacatedIntegrationEvent` | Cumplimiento (FTC Safeguards) | Si ya no es el coordinador, no-op; conflicto con el job del WISP → reintento | `SecurityCoordinatorOffboardedConsumerTests.cs`, `GetTenantOffboardingImpactHandlerTests.cs`, `Domain/OfficeComplianceTests.cs` | Participa (su pre-flight admite actor `TenantEmployee` con `users.manage`, a diferencia del retiro, que exige admin: `TenantOffboardingImpactController.cs:21-23`) |
| **Wallet** | Proy.; procedencia | `UserOffboarded` | Proyección inactiva | Permisos | Sí | `UserLifecyclePermissionsProjectionConsumerTests.cs` | Participa |
| **Gateway** (`src/Gateway/TaxVision.Gateway`) | Ninguno | — | Solo enruta; la denylist la aplica cada servicio | — | — | — | No participa (por diseño) |

Notas:
- Los 28 servicios del registro están cableados y su paso aparece en `OffboardingParticipants`; `OffboardingParticipantsContractTests.The_registry_lists_exactly_the_real_offboarding_consumers` falla si sobra o falta uno.
- "Sin huecos" según la revisión del 2026-11-03 para Sms, Postmaster, Scribe, Growth, Payment*, Billing, Catalog, Inventory, Wallet, ContentScanner, Recorder/TranscriptWorker (Documentación histórica, §1 de `Offboard-Revision-2026-11-03.md`). Workflow, AI y Settings→Connections no tenían backend: entran cuando lo tengan.

---

## 3. Evidencia

- Consumers .NET (lista completa): `grep "UserOffboardedIntegrationEvent"` en `src/Services/**/Application` (54 archivos: los pasos del registro más el publicador de Auth y el propio registro, `ScheduleOffboardingResolver` de Campaigns y `OffboardingSupport` de Client Forms; Notification agrupa varios pasos en `UserEmailDirectoryConsumers.cs` y `PermissionsProjectionConsumers.cs`). Rutas por servicio en la matriz; clases principales:
  `Calendar.Application/Appointments/Consumers/CalendarUserOffboardedConsumer.cs`, `Calendar.Application/PublicBookings/Consumers/PublicBookingHostOffboardedConsumer.cs`, `Campaigns.Application/Scheduling/Consumers/CampaignSchedulesUserOffboardedConsumer.cs`, `CloudStorage.Application/Sharing/CloudStorageUserOffboardedConsumer.cs`, `CloudStorage.Application/Providers/CloudConnectionsUserOffboardedConsumer.cs`, `Connectors.Application/Accounts/Consumers/{ConnectorsUserOffboardedConsumer, OfficeMailboxesUserOffboardedConsumer}.cs`, `Connectors.Application/Accounts/PurgeOffboardedMailboxesHandler.cs`, `Correspondence.Application/Compose/CorrespondenceUserOffboardedConsumer.cs`, `Customer.Application/Employees/Consumers/CustomerUserOffboardedConsumer.cs`, `Customer.Application/Imports/Consumers/CustomerImportsUserOffboardedConsumer.cs`, `Notes.Application/Projections/AuthEvents/NotesUserOffboardedConsumer.cs`, `Notification.Application/Consumers/{RecipientProjectionUserOffboardedConsumer, UserEmailDirectoryConsumers, PermissionsProjectionConsumers}.cs`, `Notification.Application/Push/Consumers/UserLifecyclePushDeviceConsumer.cs`, `Notification.Application/Authorization/Consumers/AuthzUserLifecyclePermissionsProjectionConsumer.cs`, `Reminder.Application/Reminders/Consumers/ReminderUserOffboardedConsumer.cs`, `Signature.Application/Projections/AuthEvents/SignatureUserOffboardedConsumer.cs`, `Signature.Application/Requests/Offboarding/SignatureRequestsUserOffboardedConsumer.cs`, `Tasks.Application/{Tasks,ClientRequests,Timers}/Consumers/*UserOffboardedConsumer.cs`, `Tasks.ClientForms.Application/Offboarding/{FormIntakeLinks,FormRequests}UserOffboardedConsumer.cs`, `Tenant.Application/Compliance/Consumers/SecurityCoordinatorOffboardedConsumer.cs`.
- Node: `src/Services/Communication/src/application/event-handlers/offboarding-consumers.ts`, `src/application/use-cases/{offboarding-handover, offboarding-impact, group-administration}.ts`, `src/contracts/events/offboarding-events.ts`, `src/infrastructure/rabbit/consumer-runtime.ts:71`.
- Pre-flight: `Calendar.Api/Controllers/AppointmentsController.cs:106`, `Campaigns.Api/Controllers/CampaignsController.cs:409`, `CloudStorage.Api/Controllers/ShareLinksController.cs:251,261`, `Connectors.Api/Controllers/AccountsController.cs:201,211`, `Correspondence.Api/Controllers/DraftsController.cs:90`, `Customer.Api/Controllers/CustomerController.cs:221`, `Signature.Api/Controllers/SignatureOffboardingController.cs:25`, `Tasks.Api/Controllers/TasksController.cs:122`, `Tenant.Api/Controllers/TenantOffboardingImpactController.cs:17`, Communication `src/api/http/routes/meetings.route.ts:259-260`.
- Respuestas del pre-flight: Customer `{AssignedClients, PrimaryClients, AdditionalAccess, ArchivedClients}`; Tasks `{OpenTasks, OpenClientRequests, RunningTimers, IntakeLinks, OpenFormRequests}`; Calendar `{FutureAppointments}`; Correspondence `{OpenDrafts}`; CloudStorage `{ActiveShareLinks, OfficeCloudConnections}`; Connectors `{PersonalMailboxes, OfficeMailboxes}`; Signature `{PendingPreparerSignatures, PreparerOnDrafts, DraftRequests}`; Campaigns `{ActiveSchedules}`; Tenant `{IsSecurityCoordinator}`; Communication `{activeMeetings, clientChats, groupsOwned, openSupportTickets, scheduledMeetingsAsParticipant}`.

## 4. Problemas

1. **Communication**: inbox marcado antes del trabajo; caída del proceso = paso perdido (V4).
2. **Proyecciones de permisos** reactivables por `UserRolesChanged` posterior (`ApplyIfNewer` → `IsActive = true`), en todos los servicios con el patrón (V5).
3. **Tenant**: pre-flight con actor más amplio que el retiro (lectura de un booleano; impacto bajo).

## 5. Buenas prácticas

- Un consumer por responsabilidad (Calendar separa citas y demos públicas; Tasks separa tareas, timers, pedidos y formularios): cada uno acusa por separado y el "Handover status" dice cuál falta.
- Sucesor revalidado con la regla de la acción (permiso + activo + tenant), no solo "existe".
- Fallback explícito por tipo de recurso y **aviso a la oficina** cuando no se puede resolver.
- Lo de oficina conectado con la cuenta del retirado **no se corta a ciegas**: se marca para revisión (CloudStorage, Connectors).
- Nunca tocar `Provenance`.

## 6. Recomendación

- Uniformar un caso de "reentrega" en todos los consumers (hoy no todos lo tienen explícito).
- Alinear actores del pre-flight de Tenant con los demás (**Decisión pendiente**: si un empleado con `users.manage` debe ver impactos).

## 7. Riesgos

- Un servicio caído retrasa la limpieza de su parte; el resto avanza (coreografía). El aviso llega a los 30 min.
- Un servicio que reenvía comandos puede marcar "completo" sin terminar.
- Los servicios "solo proyección" dependen de que no lleguen eventos de roles posteriores.

## 8. Pruebas

Ver columna "Test" y la tabla de huecos de `Offboarding.md` §9.2. Fitness obligatorios para todo servicio: `UserReferenceContractTests`, `OffboardingParticipantsContractTests`, `OffboardingStepAcknowledgementPolicyTests` (`deploy/tests/TaxVision.BuildingBlocks.Tests/Offboarding/`).

## 9. Dependencias

Auth (registro y run), BuildingBlocks (`UserReferenceAttribute`, `OffboardingStepAcknowledgements`, contratos), RabbitMQ fanout, Notification + Scribe (avisos), CRM (`offboard-impact.service.ts` lee `GET /auth/offboarding/participants`).

---

## 10. Procedimiento de incorporación para un servicio NUEVO

> **Código verificado** en cuanto a los mecanismos (atributo, registro, tests, política). Los pasos son la lectura ordenada de esos mecanismos más el DoD ya escrito en `DEVS/Guia_Creacion_Microservicio.md:1252-1256` y guardarraíl #38 (`:1334`).

### Paso 1 — Inventario de referencias
Listar toda propiedad `Guid`/`Guid?` de entidades persistidas cuyo nombre encaje con el patrón del fitness test (`UserReferenceContractTests.cs:45-50`): `*UserId`, `*By`, `*ByActorId`, `ActorId`, `SenderId`, `Owner*Id`, `Assigned*Id`, `Assignee*Id`, `Responsible*Id`, `Host*Id`, `Organizer*Id`, `Preparer*Id`, `RunAs*Id`, `Successor*Id`, `Coordinator*Id`, `Caller*Id`, `Callee*Id`, `Agent*Id`, `Invitee*Id`, `Author*Id` (excluye los que terminan en `Tenant|File|Customer|PortalUser|Participant|Signer|Session|Version|Template|Message|Folder` + `Id`). En Node: campos del `schema.prisma`.

### Paso 2 — Clasificar cada una
`[UserReference(UserReferenceKind.X, HandledBy = "...", Note = "...")]` (`src/BuildingBlocks/Domain/UserReferenceAttribute.cs`). En Prisma: `/// @userReference(Kind, handledBy: archivo-o-funcion, note: "...")`.
- `Provenance`: sin `HandledBy`.
- `ActiveWork | Credential | Private | Projection`: `HandledBy` obligatorio = clase del mismo servicio que maneja `UserOffboardedIntegrationEvent`.
- Si lo corrige otro servicio por evento derivado: `HandledBy = "Servicio:Clase"` + `Via = "EventoA|EventoB"` (el servicio local debe consumir esos eventos).
- Falso positivo (no es un usuario): agregarlo a `NotUserReferences` con el porqué.

### Paso 3 — ¿Participa?
- Solo `Provenance` y sin proyección de permisos → **no participa** (no se registra; el fitness no exige nada más).
- Tiene proyección de permisos (`UserPermissionsProjection`) → participa al menos con `UserLifecyclePermissionsProjectionConsumer` (copiar el patrón de Billing).
- Tiene `ActiveWork | Credential | Private` → participa con consumer propio **y** pre-flight.

### Paso 4 — Invariantes del negocio
Para cada recurso: ¿quién puede heredarlo? (activo, mismo tenant, permiso que exige la acción). ¿Qué pasa sin sucesor? (desasignar, pausar, cancelar con aviso al afectado, pasar a quien retiró, rutar a la oficina). ¿Qué es historial intocable?

### Paso 5 — Usar el evento existente
Consumir `UserOffboardedIntegrationEvent` (no crear otro). Campos útiles: `TenantId`, `UserId`, `SuccessorUserId?`, `OffboardedByUserId?`, `RemovedAtUtc`, `OccurredOn`, `CorrelationId`, `Email` (solo si de verdad se identifica por email).
Regla **retiro ⊇ suspensión**: si el servicio maneja `UserDeactivatedIntegrationEvent`, también `UserOffboarded` (lo exige `Every_service_that_handles_suspension_also_handles_offboarding`).

### Paso 6 — Consumer
- .NET: clase estática `<Recurso>UserOffboardedConsumer` con `public static async Task Handle(UserOffboardedIntegrationEvent msg, ...)` en `Application/.../Consumers`. Wolverine lo descubre solo.
- Node: `register('auth.user.offboarded.v1', handler)` y mapa CLR→alias en `consumer-runtime.ts`.
- Empujar el `CorrelationId` (`correlation.Push(msg.CorrelationId ?? EventId)`), revalidar el sucesor localmente, procesar por lotes con keyset, guardar antes de publicar eventos derivados.

### Paso 7 — Contrato de eventos derivados
Si el cambio interesa a otro servicio, publicar un evento propio en `src/BuildingBlocks/Messaging/<Servicio>IntegrationEvents/` con `[MessageIdentity]` y ruta `PublishMessage<T>().ToRabbitExchange("taxvision-events")`. Nunca agregar un campo `required` a un evento vivo (Documentación histórica: rompe la deserialización → DLQ).

### Paso 8 — Wolverine/RabbitMQ
- `options.UseTaxVisionRabbitMq(uri)` (enciende el acuse automático y las confirmaciones del broker), `PersistMessagesWithSqlServer`, `UseDurableOutboxOnAllSendingEndpoints`, `UseEntityFrameworkCoreTransactions`, `AutoApplyTransactions`, `ApplyStandardFailurePolicies`.
- `ListenToRabbitQueue("<servicio>-events", q => q.BindExchange("taxvision-events", string.Empty)).UseDurableInbox()`.
- Middleware `IntegrationEventTenantMiddleware` para que el filtro de tenant vea el `TenantId` del evento.
- Si el consumer solo reenvía un comando local, márcalo con `[AcknowledgesOffboardingLater]` y publica el acuse desde el handler que termina el trabajo (`OffboardingStepNames.CompletedBy(...)`, misma unidad con outbox), como Connectors.

### Paso 9 — Idempotencia
Consultas que solo devuelven lo que todavía apunta al retirado; operaciones de dominio idempotentes (`Result` sin efecto si ya está hecho). Recordar que en .NET todos los handlers del mensaje de un servicio corren en una cadena: si uno falla, los demás se repiten.

### Paso 10 — Histórico
No tocar `Provenance`, auditoría, documentos firmados/sellados, cadenas HMAC, runs ya enviados, `CreatedByUserId`. Si el recurso tiene retención legal, conservar y anotar con `Note`.

### Paso 11 — Duplicados y orden
No asumir orden respecto a otros servicios ni a otros eventos de ciclo de vida. Para proyecciones con estado, usar marca de tiempo (`OccurredOn`) o versión para ignorar lo viejo.

### Paso 12 — Fallos
Dependencia externa caída → lanzar (Wolverine reintenta 1s/5s/15s y DLQ). Nada a medias que no sea retomable. Llamadas a proveedores irreversibles (revocar tokens) después del commit y best-effort.

### Paso 13 — Registro y pre-flight
- Agregar el servicio y sus pasos a `src/Services/Auth/Application/Offboarding/OffboardingParticipants.cs` (formato fijo: una entrada `new(...)` con literales; el test lo lee del fuente). Lo exige `The_registry_lists_exactly_the_real_offboarding_consumers`.
- Si hay `ActiveWork | Credential | Private`: exponer `GET /<svc>/offboarding-impact/{userId}` con `[HasPermission(UserManagementPermissions.UsersManage)]`, `[AllowActorTypes(...)]`, `[RateLimit(...)]`; si se identifica por email, además `POST` con `{ "email" }` en el cuerpo (nunca PII en la URL) y `AcceptsEmail: true`. Lo exigen `The_registry_lists_exactly_the_real_offboarding_impact_endpoints` y `Every_service_with_active_work_credentials_or_private_data_exposes_offboarding_impact` (excepciones solo en `ImpactNotRequired`, lista que solo se achica).
- Opcional en el CRM: texto propio en `offboard-impact.service.ts`; si no, sale el renglón genérico con `Module`.

### Paso 14 — Pruebas del servicio
Mínimo: con sucesor; sin sucesor; sucesor inválido (otro tenant, inactivo, sin permiso); reentrega no repite efectos ni avisos; historial intacto; conteo del pre-flight; proyección inactiva (y que un `UserReactivated` no aplica a un retirado si el servicio lo distingue). Consultas EF nuevas: prueba de traducción en `.Integration.` contra la base local (ejemplo: `Tasks.Tests/Integration/OffboardingQueriesTranslationTests.cs`). Correr también los tres fitness de `TaxVision.BuildingBlocks.Tests/Offboarding/`.

### Paso 15 — Verificación de punta a punta
Sin E2E automatizado (descartado por el dueño). Verificación manual: sembrar trabajo del empleado en el módulo nuevo, retirarlo desde el CRM y comprobar en "Handover status" que el paso nuevo acusa (N/N) y que el recurso quedó en el sucesor/cerrado. Revisar la DLQ y los logs `Offboarding step ... rejected` / `has no offboarding run`.

### Checklist corto
- [ ] Todas las referencias anotadas (`[UserReference]` / `@userReference`).
- [ ] Consumer idempotente, sin orden asumido, con sucesor revalidado y fallback con aviso.
- [ ] Retiro ⊇ suspensión.
- [ ] Paso(s) en `OffboardingParticipants`; `ImpactPath` si corresponde.
- [ ] Pre-flight con `users.manage`, sin PII en la URL.
- [ ] Acuse refleja el trabajo terminado (no un reenvío).
- [ ] Pruebas del servicio + 3 fitness verdes.
- [ ] Prueba manual en vivo con "Handover status".
