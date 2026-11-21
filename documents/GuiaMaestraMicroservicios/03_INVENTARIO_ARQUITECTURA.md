# 03 — Inventario y arquitectura de TaxVision Backend

> Fecha de corte: 2026-11-15. Lectura estática de `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd` (sin compilar ni ejecutar).
> Rutas relativas a esa raíz salvo que se indique otra.
> Etiquetas: **Código verificado** · **Documentación histórica** · **Recomendación** · **Decisión pendiente**.
> No se copian secretos, cadenas de conexión ni tokens: las variables se nombran sin valor.

## 1. Cifras de la flota (Código verificado)

| Pieza | Cantidad | Evidencia |
|---|---|---|
| Servicios .NET de negocio con 4 capas y base propia | 27 (Auth, Billing, Calendar, Campaigns, Catalog, CloudStorage, Connectors, Correspondence, Customer, Documents, FinancialInsights, Growth, Inventory, Mobile, Notes, Notification, PaymentApp, PaymentClient, Postmaster, Reminder, Scribe, Signature, Sms, Subscription, Tasks, Tenant, Wallet) | 27 `*DbContext` en `src/Services/*/…/Persistence/`; 27 `*DbContextFactory.cs`; 27 `*ModelSnapshot.cs` |
| Workers .NET sin API pública | 2 (`TaxVision.Connectors.MailboxListener`, `TaxVision.ContentScanner`) | `TaxVision.slnx`; `deploy/docker/docker-compose.yml` (`connectors-mailbox-listener`, `content-scanner`) |
| Servicios Node/TypeScript | 3 (Communication, CommunicationRecorder, CommunicationTranscriptWorker) | `src/Services/*/package.json` |
| Gateway | 1 (YARP) | `src/Gateway/TaxVision.Gateway/TaxVision.Gateway.csproj` |
| Proyectos de BuildingBlocks (csproj) | 10 (núcleo `BuildingBlocks.csproj` + Authorization, Infrastructure, Web, CustomerVisibility, EfCore, Imaging, Jobs, Messaging, TenantTimeZones) | `find src/BuildingBlocks -name *.csproj` |
| Proyectos de pruebas .NET en la solución | 32 (uno por servicio .NET + BuildingBlocks.Tests, BuildingBlocks.Testing, DataCollection.Engine.Tests, Gateway.Tests, ContentScanner.Tests) | `TaxVision.slnx:225-257` |
| Bases EF aplicadas por el deploy | 26 + FinancialInsights condicional | `deploy/docker/migrations/apply-migrations.sh` (`if [ -n "${FINANCIAL_INSIGHTS_DB_CONNECTION:-}" ]` en `:171`; Mobile sin condición en `:179-183`) |
| Base Prisma | 1 (Communication, SQL Server, 56 migraciones) | `src/Services/Communication/prisma/schema.prisma:23-24` |

**Documentación histórica (superada):** `docs/audit/01-System-Architecture.md` (2026-08-07) habla de "18 áreas" y de SQL Server, Loki y Tempo dentro del compose; hoy SQL Server es externo (`db-ready` solo espera el puerto, `deploy/docker/docker-compose.yml:160-185`) y Loki/Tempo están comentados. `DEVS/Guia_Creacion_Microservicio.md:32` dice "2 servicios Node"; hoy son 3. `src/Services/Communication/README.md` dice "Único servicio en Node.js … Node ≥ 20.11"; hoy `engines.node >= 24.0.0` y la imagen es `node:24.22.2-bookworm-slim`.

## 2. Matriz de servicios — identidad, stack, datos, pruebas

Columnas: puerto dev = destino del cluster en `src/Gateway/TaxVision.Gateway/appsettings.json` (Development); en compose todos escuchan `8080` interno.

| Servicio | Responsabilidad (resumen de `DomainDrivenDesign/<Servicio>.md` y ADR) | Stack | Ruta | Proyectos | BD / DbContext / variable | Puerto dev | Tests |
|---|---|---|---|---|---|---|---|
| Auth | Identidad, login, emisión JWT RS256, roles y `PermissionCatalog`, onboarding comercial (subcontexto), retiro de empleados | .NET 10, Wolverine 6.14.0, EF 10.0.9; Minio, Imaging, Jobs | `src/Services/Auth` | 7: `Api`, `Application`, `Domain`, `Infrastructure`, `Onboarding/{Application,Domain,Infrastructure}` | SQL Server propia · `AuthDbContext` · `AUTH_DB_CONNECTION` | 5124 | `TaxVision.Auth.Tests` |
| Billing | Factura oficina→cliente (`INV-…`) y factura de onboarding (`ONB-…`) | .NET 10; CustomerVisibility, TenantTimeZones | `src/Services/Billing` | 4 | `BillingDbContext` · `BILLING_DB_CONNECTION` | 5460 | `TaxVision.Billing.Tests` |
| Calendar | Citas, disponibilidad, reserva pública, feed `.ics` (ADR-023) | .NET 10; Ical.Net 5.1.0; Jobs, CustomerVisibility, TenantTimeZones | `src/Services/Calendar` | 4 | `CalendarDbContext` · `CALENDAR_DB_CONNECTION` | 5520 | `TaxVision.Calendar.Tests` |
| Campaigns | Campañas email/SMS, contactos, listas, remitentes, plantillas | .NET 10; CustomerVisibility, TenantTimeZones | `src/Services/Campaigns` | 4 | `CampaignsDbContext` · `CAMPAIGNS_DB_CONNECTION` | 5530 | `TaxVision.Campaigns.Tests` |
| Catalog | Catálogo de productos/servicios por tenant | .NET 10 | `src/Services/Catalog` | 4 | `CatalogDbContext` · `CATALOG_DB_CONNECTION` | 5480 | `TaxVision.Catalog.Tests` |
| CloudStorage | Archivos (MinIO), escaneo ClamAV, moderación, cuotas, papelera, compartir | .NET 10; Minio 7.0.0, AWSSDK.S3 4.0.101; CustomerVisibility | `src/Services/CloudStorage` | 4 | `CloudStorageDbContext` · `CLOUDSTORAGE_DB_CONNECTION` | 5330 | `TaxVision.CloudStorage.Tests` |
| Connectors | Buzones Gmail/Outlook (OAuth) e IMAP/SMTP, tokens, webhooks, sync incremental | .NET 10; Google.Apis.Auth 1.68.0, MailKit 4.17.0; Jobs | `src/Services/Connectors` | 5 (+ `TaxVision.Connectors.MailboxListener`) | `ConnectorsDbContext` · `CONNECTORS_DB_CONNECTION` (compartida con el listener) | 5390 | `TaxVision.Connectors.Tests` |
| Correspondence | Bandeja de correo de la oficina: hilos, borradores, envío vía Postmaster | .NET 10; Minio; Jobs, CustomerVisibility, TenantTimeZones | `src/Services/Correspondence` | 4 | `CorrespondenceDbContext` · `CORRESPONDENCE_DB_CONNECTION` | 5400 | `TaxVision.Correspondence.Tests` |
| Customer | Ficha del cliente, asignaciones de preparador, importación, acceso al portal | .NET 10; ClosedXML 0.105.0, CsvHelper 33.1.0, Minio; Imaging, Jobs, TenantTimeZones | `src/Services/Customer` | 4 | `CustomerDbContext` · `CUSTOMER_DB_CONNECTION` | 5263 | `TaxVision.Customer.Tests` |
| Documents | Generación documental HTML→PDF, ZIP, marca de agua | .NET 10; Microsoft.Playwright 1.49.0, PdfSharp 6.2.0, QRCoder 1.8.0, Fluid.Core 2.31.0, Minio; TenantTimeZones | `src/Services/Documents` | 4 | `DocumentsDbContext` · `DOCUMENTS_DB_CONNECTION` | 5450 | `TaxVision.Documents.Tests` |
| FinancialInsights | Insights financieros de la oficina (ver `documents/Financial_Insights.md`) | .NET 10; ClosedXML; CustomerVisibility, TenantTimeZones | `src/Services/FinancialInsights` | 4 | `FinancialInsightsDbContext` · `FINANCIAL_INSIGHTS_DB_CONNECTION` (opcional en migraciones) | 5276 | `TaxVision.FinancialInsights.Tests` |
| Growth | Dos contextos en un despliegue: Codes (promociones/regalos) y Referrals | .NET 10; Jobs | `src/Services/Growth` | 6: `Codes.{Application,Domain}`, `Referrals.{Application,Domain}`, `Growth.Api`, `Growth.Infrastructure` | `GrowthDbContext` · `GROWTH_DB_CONNECTION` | 5410 | `TaxVision.Growth.Tests` |
| Inventory | Stock, ledger de movimientos, proveedores; API M2M `internal/stock/commit-sale` | .NET 10 | `src/Services/Inventory` | 4 | `InventoryDbContext` · `INVENTORY_DB_CONNECTION` | 5490 | `TaxVision.Inventory.Tests` |
| Mobile | Vehículos y viajes con GPS para la app móvil de los clientes del portal: sincronización idempotente, estados, millaje, estadísticas y tarifas de millaje por oficina; purga del recorrido por plazo (`src/Services/Mobile/README.md:5`, `TRACKING_API.md:5-33`) | .NET 10; TenantTimeZones; lock en Redis para la retención | `src/Services/Mobile` | 4 | `MobileDbContext` · `MOBILE_DB_CONNECTION` | 5280 | `TaxVision.Mobile.Tests` |
| Notes | Notas transversales sobre Customer/Task/Appointment (ADR-018) | .NET 10; HtmlSanitizer 9.1.982; CustomerVisibility | `src/Services/Notes` | 4 | `NotesDbContext` · `NOTES_DB_CONNECTION` | 5440 | `TaxVision.Notes.Tests` |
| Notification | Orquestador de avisos: email (Scribe+Postmaster), SMS, push FCM, in-app | .NET 10; FirebaseAdmin 3.1.0, MailKit, Fluid.Core **2.14.0**; CustomerVisibility, TenantTimeZones | `src/Services/Notification` | 4 | `NotificationDbContext` · `NOTIFICATION_DB_CONNECTION` | 5320 | `TaxVision.Notification.Tests` |
| PaymentApp | Cobros de la plataforma a las oficinas (Stripe/PayPal), dunning, webhooks | .NET 10; Stripe.net 47.4.0; Jobs | `src/Services/PaymentApp` | 4 | `PaymentAppDbContext` · `PAYMENTAPP_DB_CONNECTION` | 5430 | `TaxVision.PaymentApp.Tests` |
| PaymentClient | Cobros de la oficina a sus clientes (links, recurrentes, Connect, payouts) | .NET 10; Stripe.net 47.4.0; Jobs | `src/Services/PaymentClient` | 4 | `PaymentClientDbContext` · `PAYMENTCLIENT_DB_CONNECTION` | 5420 | `TaxVision.PaymentClient.Tests` |
| Postmaster | Envío real de correo (cadena buzón→proveedor tenant→sistema), entregas | .NET 10; MailKit 4.17.0 | `src/Services/Postmaster` | 4 | `PostmasterDbContext` · `POSTMASTER_DB_CONNECTION` | 5370 | `TaxVision.Postmaster.Tests` |
| Reminder | Recordatorios de usuario con máquina de estados propia (ADR-021) | .NET 10; Quartz 3.15.1 (AdoJobStore) | `src/Services/Reminder` | 4 | `ReminderDbContext` · `REMINDER_DB_CONNECTION` | 5500 | `TaxVision.Reminder.Tests` |
| Scribe | Plantillas de correo, versiones, render Liquid | .NET 10; Fluid.Core 2.31.0, AngleSharp 1.7.0, Minio; TenantTimeZones | `src/Services/Scribe` | 4 | `ScribeDbContext` · `SCRIBE_DB_CONNECTION` | 5380 | `TaxVision.Scribe.Tests` |
| Signature | Firma electrónica, sellado PAdES/CMS, TSA | .NET 10; BouncyCastle 2.6.2, PdfSharp 6.2.0, SkiaSharp 3.119.0, Minio; Jobs, CustomerVisibility, TenantTimeZones | `src/Services/Signature` | 4 | `SignatureDbContext` · `SIGNATURE_DB_CONNECTION` | 5340 | `TaxVision.Signature.Tests` |
| Sms | SMS saliente y webhooks de proveedor | .NET 10; CustomerVisibility | `src/Services/Sms` | 4 | `SmsDbContext` · `SMS_DB_CONNECTION` | 5470 | `TaxVision.Sms.Tests` |
| Subscription | Planes, módulos, add-ons, suscripción, asientos, entitlements | .NET 10; Jobs, TenantTimeZones | `src/Services/Subscription` | 4 | `SubscriptionDbContext` · `SUBSCRIPTION_DB_CONNECTION` | 5360 | `TaxVision.Subscription.Tests` |
| Tasks | Tareas, Client Forms y motor `DataCollection.Engine` (ADR-022) | .NET 10; Ical.Net 5.1.0; Jobs, CustomerVisibility, TenantTimeZones | `src/Services/Tasks` | 7: `Api`, `Application`, `Domain`, `Infrastructure`, `ClientForms.{Application,Domain}`, `DataCollection.Engine` | `TasksDbContext` · `TASKS_DB_CONNECTION` | 5510 | `TaxVision.Tasks.Tests`, `TaxVision.DataCollection.Engine.Tests` |
| Tenant | Registro de oficinas, perfil público, zona horaria, branding, `platform/settings` | .NET 10; Minio; Jobs | `src/Services/Tenant` | 4 | `TenantDbContext` · `TENANT_DB_CONNECTION` | 5217 | `TaxVision.Tenant.Tests` |
| Wallet | Monedero prepago por oficina y PEP de gasto (reservar/liquidar) | .NET 10 | `src/Services/Wallet` | 4 | `WalletDbContext` · `WALLET_DB_CONNECTION` | 5270 | `TaxVision.Wallet.Tests` |
| Connectors.MailboxListener | Worker IMAP IDLE de Connectors | .NET 10; Wolverine; `BuildingBlocks.Web` | `src/Services/Connectors/TaxVision.Connectors.MailboxListener` | 1 | Usa `ConnectorsDbContext` | — | (en `TaxVision.Connectors.Tests`) |
| ContentScanner | Puntúa contenido (NSFW) con ONNX + ffmpeg; `POST /v1/scan` con cabecera `X-Scanner-Key` | .NET 10 `Microsoft.NET.Sdk.Web`; Microsoft.ML.OnnxRuntime 1.31.0; `InvariantGlobalization=true` | `src/Services/ContentScanner/TaxVision.ContentScanner` | 1 | Sin base | — (5540 local según memoria) | `TaxVision.ContentScanner.Tests` |
| Communication | Chat, llamadas 1:1, meetings (SFU mediasoup), in-app, soporte | Node ≥24 (imagen 24.22.2), TypeScript 5.6.3, Fastify 5.12.7, Socket.IO 4.8.1, Prisma 5.22.0, amqplib 0.10.4, ioredis 5.4.1, jose 5.9.6, zod 4.6.7, mediasoup `^3.21.0` | `src/Services/Communication` | 1 (npm) | SQL Server propia vía Prisma · `COMMUNICATION_DB_CONNECTION` | 5350 | Vitest 5.0.3 en `src/Services/Communication/tests/{unit,integration}` (fuera de `.slnx`) |
| CommunicationRecorder | Captura RTP de grabaciones y composición ffmpeg; sube a MinIO | Node ≥24, Fastify 5.12.7, minio 8.0.7, amqplib, ioredis | `src/Services/CommunicationRecorder` | 1 | Sin base (Redis + spool) | — | Vitest (`tests/{unit,integration}`) |
| CommunicationTranscriptWorker | Transcripción con whisper.cpp | Node ≥24, minio 8.0.7, amqplib, ioredis; whisper.cpp compilado en la imagen | `src/Services/CommunicationTranscriptWorker` | 1 | Sin base | — | Vitest (`tests/unit`) |
| Gateway | Entrada única: rutas por prefijo, JWT, rate limit pre-auth, load shedding, guardas de superficie interna y MFA, asiento | .NET 10; Yarp.ReverseProxy 2.3.0 | `src/Gateway/TaxVision.Gateway` | 1 | Lee la base de Auth para el gate de asiento (`ConnectionStrings__Auth`) | 5047 (host) | `TaxVision.Gateway.Tests` |

## 3. Matriz de servicios — mensajería, proyecciones, seguridad, despliegue

- **Contratos propios** = número de `record` en `src/BuildingBlocks/Messaging/<X>IntegrationEvents/` (aprox.; incluye contratos que publica otro servicio).
- **PublishMessage** = líneas `PublishMessage<` en el `Program.cs` del Api (rutas de salida declaradas).
- **Consumidores** = archivos con un `Handle(XIntegrationEvent …)` (aprox., por patrón de nombre de parámetro; puede subcontar).
- **Gate de módulo** = `AddModuleGate` en `Program.cs`.

| Servicio | Contratos propios | PublishMessage | Colas que escucha | Consumidores (aprox.) | Proyecciones locales (entidades) | Seguridad | Gateway (prefijos) | Compose |
|---|---|---|---|---|---|---|---|---|
| Auth | Auth 46 | 42 | `auth-tenant-events` | ~19 | `TenantPlanCodeProjection`, `TenantSeatSlotProjection` | Emite JWT; denylist propio; sin gate de módulo | `/auth`, `/onboarding` | `auth-api` |
| Billing | Billing 11 | 6 | `billing-events` | ~6 | `AuthzUser/RolePermissionsProjection`, `TenantPlanCodeProjection`, zona horaria, asignaciones | JWT+denylist+permisos; sin gate | `/billing` | `billing-api` |
| Calendar | Calendar 24 | 22 | `calendar-events` | ~15 | permisos, plan, `CustomerDirectoryEntry`, `OfficeScheduleProjection`, zona, asignaciones | JWT+denylist+permisos+gate | `/calendar`, `/calendar/public` | `calendar-api` |
| Campaigns | Campaigns 6 | 6 | `campaigns-events` | ~11 | permisos, plan, directorio, zona, asignaciones | +gate | `/campaigns`, `/contacts`, `/contact-lists`, `/sender-profiles`, `/campaign-settings`, `/campaign-templates` | `campaigns-api` |
| Catalog | Catalog 5 | 5 | `catalog-events` | ~3 | permisos, plan | sin gate | `/catalog` | `catalog-api` |
| CloudStorage | CloudStorage 30 | 26 | `cloudstorage-events`, `cloudstorage-external-uploads`, `cloudstorage-file-deletions` | ~8 | permisos, plan, asignaciones | +gate | `/storage` | `cloudstorage-api` |
| Connectors | Connectors 8 | 14 | `connectors-events` | ~5 | permisos, plan | +gate | `/connectors` | `connectors-api`, `connectors-mailbox-listener` |
| Correspondence | Correspondence 3 + Email | 6 | `correspondence-events` | ~16 | permisos, plan, zona | +gate | `/correspondence` | `correspondence-api` |
| Customer | Customer 11 | 17 | `customer-events`, `customer-client-form-commands` | (subcontado) | permisos, plan, `TenantEmployeeDirectoryEntry`, zona | +gate | `/customers` | `customer-api` |
| Documents | Documents 11 | 7 | `documents-events`, `documents-client-form-summary` | ~9 | permisos, plan, zona | +gate | `/documents` | `documents-api` |
| FinancialInsights | FinancialInsights 1 | 1 | `financial-insights-events` | ~3 | permisos, `CustomerDirectoryProjection`, `TenantDirectoryProjection`, `TenantEntitlementsProjection` | +gate | `/financial` | `financial-insights-api` |
| Growth | Growth 11 | 4 | `growth-events` | ~6 | permisos, plan | sin gate | `/growth` | `growth-api` |
| Inventory | — | 0 | `inventory-events` | ~4 | permisos, plan | sin gate | `/inventory` | `inventory-api` |
| Mobile | — | 0 | `mobile-events` | ~5 | permisos, plan, zona | JWT+denylist+permisos (`CustomerPortal`; tarifas solo admin) +gate `mileage` (`MOBILE_MODULE_GATE_ENFORCE`, apagado por defecto) | `/mobile` (rutas `mobile-tracking`/`mobile-complete` a 256 KiB) | `mobile-api` |
| Notes | Notes 4 | 4 | `notes-events` | ~12 | permisos, plan, `CustomerDirectoryEntry`, `OffboardedStaffProjection`, asignaciones | +gate | `/notes` | `notes-api` |
| Notification | Notification 1 | 8 | `notification-events`, `notification-client-form-commands` | ~105 | permisos de destinatarios, plan, directorios de email, políticas de contacto, zona, asignaciones | sin gate | `/notifications` | `notification-api` |
| PaymentApp | PaymentApp 22 + Payment 5 | 26 | `payment-app-events` | ~22 | permisos, plan, `TenantBillingPreferenceProjection` | sin gate | `/payments-app` | `payment-app-api` |
| PaymentClient | PaymentClient 9 | 5 | `payment-client-events` | ~10 | permisos, plan | sin gate | `/payments-client` | `payment-client-api` |
| Postmaster | (Email 19, compartido) | 9 | `postmaster-events` | ~15 | permisos, plan, `TenantDirectoryEntry` | +gate | `/postmaster` | `postmaster-api` |
| Reminder | Reminder 4 | 2 | `reminder-events` | ~7 | permisos, plan | +gate | `/reminders` | `reminder-api` |
| Scribe | — | 1 | `scribe-events` | ~10 | permisos, plan, perfil de oficina, zona | +gate | `/scribe` | `scribe-api` |
| Signature | Signature 41 | 36 | `signature-events`, `signature-client-form-commands` | ~32 | permisos, plan, `CustomerEmailProjection`, `RejectedFileProjection`, zona, asignaciones | +gate | `/signature` | `signature-api` |
| Sms | Sms 5 | 5 | `sms-events` | ~6 | permisos, plan, políticas de contacto, asignaciones | sin gate | `/sms` | `sms-api` |
| Subscription | Subscription 25 | 26 | `subscription-events` | ~20 | permisos, plan, `TenantEntitlementSnapshot`, zona | sin gate | `/plans`, `/subscriptions`, `/seats`, `/addons`, `/entitlements`, `/audit` | `subscription-api` |
| Tasks | Tasks 15 + ClientForms 53 | 37 | `tasks-events` | ~33 | permisos, plan, `CustomerDirectoryEntry`, zona, asignaciones | +gate | `/tasks` | `tasks-api` |
| Tenant | Tenant 11 | 13 | `tenant-events` | ~6 | permisos, plan | sin gate | `/tenants`, `/platform/branding`, `/platform/settings`, `/platform/tenants` | `tenant-api` |
| Wallet | Wallet 4 | 4 | `wallet-events` | ~5 | permisos, plan | +gate | `/wallet` | `wallet-api` |
| Communication | Communication 12 | (Node, outbox Prisma) | `COMMUNICATION_RABBITMQ_QUEUE` + DLQ | (Node) | propias en Prisma | JWT por JWKS de Auth (`jose`), denylist por Redis | `/communication` | `communication-api` |
| CommunicationRecorder | — | — | `RECORDER_*` | — | — | JWKS de Auth + `RECORDER_ALLOWED_CLIENT_IDS` | **no** | `communication-recorder` |
| CommunicationTranscriptWorker | — | — | `TRANSCRIPT_WORKER_RABBITMQ_QUEUE` | — | — | Token M2M (`TRANSCRIPT_WORKER_SERVICE_AUTH_CLIENT_*`) | **no** | `communication-transcript-worker` |
| ContentScanner | — | — | — | — | — | API key compartida (`ContentScanner__ApiKey`) | **no** | `content-scanner` |
| Gateway | — | — | — | — | — | JWT, CORS por settings de plataforma, `InternalSurfaceGuardMiddleware`, `MfaEnrollmentGuardMiddleware`, `SeatSlotGateMiddleware`, rate limit, load shedding | — | `gateway` (127.0.0.1:5047→8080) |

**Notas verificadas:**
- Todos los eventos se publican a `taxvision-events` (fanout). Excepciones punto a punto para **comandos**: `ToRabbitQueue("cloudstorage-file-deletions")` (`src/Services/Customer/TaxVision.Customer.Api/Program.cs:210`) y colas `*-client-form-commands` sin `BindExchange`.
- "permisos" = `UserPermissionsProjection`/`RolePermissionsProjection` (o `Authz*` en Billing, Documents, Notification, Signature); "plan" = `TenantPlanCodeProjection`; "zona" = `TenantTimeZoneProjection` del kit `BuildingBlocks.TenantTimeZones`; "asignaciones" = `CustomerAssignmentProjection` del kit `BuildingBlocks.CustomerVisibility`.
- Gate de módulo: 16 `Program.cs` llaman `AddModuleGate` (Mobile incluido) y la fitness `The_fourteen_gated_services_are_still_wired` afirma 16 servicios con `AddCachedTenantEntitlementModulesReader` (`deploy/tests/TaxVision.BuildingBlocks.Tests/ActorTypeAuthorization/ModuleGateWiringFitnessTests.cs:109-120`): el número coincide; solo el nombre de la prueba quedó viejo. La guía antigua dice "thirteen" (`DEVS/Guia_Creacion_Microservicio.md:904`).
- Superficie interna: `internal/*` sin prefijo de servicio no se enruta en el Gateway (ADR-019, `documents/architecture/ADR_019_Internal_Surface_Not_Exposed_At_Gateway.md:33-64`).

## 4. Adopción de piezas transversales por servicio (Código verificado, `Program.cs` de cada Api)

`1` = presente, `0` = ausente. Todas las columnas no listadas (`UseTaxVisionCodeGeneration`, `UseTaxVisionSerilog`, `AddTaxVisionOpenTelemetry`, `AddTaxVisionJwtAuthentication`, `UseTaxVisionConflictPolicy`, `ApplyStandardFailurePolicies`, `UseTaxVisionRabbitMq`, `AddTimeZoneSelfCheck`, `UseDurableOutboxOnAllSendingEndpoints`, `/health/live` + `/health/ready`, carpeta `Internal/Generated`, `PreGeneratedHandlerCodeTests`, `LayeringRulesBase`) están en **los 27**.

| Servicio | DomainEvents (BB-1) | Colas locales durables (BB-2) | SessionDenylist | Gate módulo | `UseUtcDateTimes` | `UtcDateTimesTests` |
|---|---|---|---|---|---|---|
| Auth | 1 | 1 | propio | 0 | 1 | 1 |
| Billing | 1 | 1 | 1 | 0 | 1 | 1 |
| Calendar | 1 | 1 | 1 | 1 | **0 (conversor propio)** | **0** |
| Campaigns | 1 | 1 | 1 | 1 | 1 | 1 |
| Catalog | 0 | 0 | 1 | 0 | 1 | 1 |
| CloudStorage | 1 | 1 | 1 | 1 | 1 | 1 |
| Connectors | 1 | 1 | 1 | 1 | 1 | 1 |
| Correspondence | 1 | 1 | 1 | 1 | 1 | 1 |
| Customer | 1 | 1 | 1 | 1 | 1 | 1 |
| Documents | 0 | 1 | 1 | 1 | 1 | 1 |
| FinancialInsights | 1 | 0 | 1 | 1 | 1 | 1 |
| Growth | 1 | 0 | 1 | 0 | 1 | 1 |
| Inventory | 0 | 0 | 1 | 0 | 1 | 1 |
| Mobile | 1 | 0 | 1 | 1 | 1 | 1 |
| Notes | 0 | 1 | 1 | 1 | 1 | 1 |
| Notification | 0 | 1 | 1 | 0 | 1 | 1 |
| PaymentApp | 1 | 1 | 1 | 0 | 1 | 1 |
| PaymentClient | 1 | 1 | 1 | 0 | 1 | 1 |
| Postmaster | 1 | 1 | 1 | 1 | 1 | 1 |
| Reminder | 0 | 0 | 1 | 1 | 1 | 1 |
| Scribe | 0 | 0 | 1 | 1 | 1 | 1 |
| Signature | 0 | 1 | 1 | 1 | **0 (conversor propio)** | **0** |
| Sms | 0 | 1 | 1 | 0 | 1 | 1 |
| Subscription | 1 | 1 | 1 | 0 | 1 | 1 |
| Tasks | 1 | 1 | 1 | 1 | **0 (conversor propio)** | **0** |
| Tenant | 1 | 1 | 1 | 0 | 1 | 1 |
| Wallet | 1 | 0 | 1 | 1 | 1 | 1 |

Evidencia de los conversores propios: `src/Services/Calendar/TaxVision.Calendar.Infrastructure/Persistence/CalendarDbContext.cs:85-104` (`ConfigureConventions` + `UtcDateTimeConverter`), `src/Services/Signature/…/SignatureDbContext.cs:112-116`. Contradice el guardrail de zonas "Cada `DbContext` llama `modelBuilder.UseUtcDateTimes()` … Lo prueba `Architecture/UtcDateTimesTests` en cada servicio" (`Implementaciones/Reminder/04_Guardrails_Checklist_Y_Verificacion.md:117`): son 24 de 27.

## 5. Servicios de referencia

Criterios exigidos: `Result`/`Error`, capa Application separada, codegen Static, fitness tests. Se compararon candidatos con la tabla anterior y con los veredictos de `DomainDrivenDesign/<Servicio>.md`.

| Práctica | **Notes** (referencia principal) | **Customer** (referencia de eventos de dominio) | **Reminder** (referencia de agregado rico) |
|---|---|---|---|
| `Result`/`Error` en dominio y handlers | Sí (`Note.Create` → `Result<Note>`; `NoteErrors`) | Sí | Sí (veredicto "Bien", `DomainDrivenDesign/Reminder.md`) |
| Application separada + `AssemblyMarker` | Sí (`options.Discovery.IncludeAssembly(typeof(AssemblyMarker).Assembly)`, `Notes/Program.cs:122`) | Sí (`IncludeAssembly(typeof(CreateCustomerHandler).Assembly)`) | Sí |
| Codegen Static | Sí (`UseTaxVisionCodeGeneration`, `Notes/Program.cs:121`; `TaxVision.Notes.Api/Internal/Generated/WolverineHandlers`) | Sí | Sí |
| `LayeringRulesBase` + `PreGeneratedHandlerCodeTests` + `UtcDateTimesTests` | Sí (`deploy/tests/TaxVision.Notes.Tests/Architecture/`) | Sí | Sí |
| Fitness de controladores (`AllowActorTypes`, `RateLimit`) | Sí (`NotesArchitectureTests`) | Sí (`CustomerActorTypeArchitectureTests`) | Sí (`ReminderArchitectureTests`) |
| `LocalQueueDurabilityRules` | Sí | Sí | **No** |
| Despacho de eventos de dominio (BB-1) | No (no emite eventos de dominio; publica integración tras guardar) | **Sí** (`Customer/Program.cs:164-165`) | No |
| Colas locales durables (BB-2) | Sí (`Notes/Program.cs:144`) | Sí (`Customer/Program.cs:168`) | **No** |
| Kits opcionales | CustomerVisibility | TenantTimeZones, Imaging, Jobs | Quartz |
| Tamaño | 113 `.cs` sin generados; 4 proyectos | grande (~40 carpetas de comandos) | mediano |

**Justificación (Recomendación):**
1. **Notes** es la referencia principal: tamaño manejable, ya era la referencia de la guía antigua (`DEVS/Guia_Creacion_Microservicio.md:76`), cumple todas las piezas obligatorias y ejemplifica consumers con guarda de tenant, proyecciones RBAC/plan/directorio, `IgnoreQueryFilters` justificado y filtro fail-closed.
2. **Customer** complementa lo que Notes no tiene: `AggregateRoot` con `AddDomainEvent`, `UseTaxVisionDomainEvents()` + cola de dominio secuencial, `TenantTimeZones`, colas punto a punto.
3. **Reminder** solo como referencia de modelado de agregado (transición = método, `RowVersion`, índice único de idempotencia) y de Quartz; **no** para cableado de Wolverine (sin BB-1/BB-2).

**Deudas de las referencias (no copiar):**
- Comentarios con números de fase y narrativa (`NotesDbContext.cs:19-28`, `Notes/Program.cs:60-172`, `NotesController.cs:23-49`) — contra el guardrail #36 y `AGENTS.md`.
- Mensajes de `NoteErrors` en español (`src/Services/Notes/TaxVision.Notes.Domain/Notes/NoteErrors.cs:11-23`) mientras la regla dice "copy visible de la UI en inglés". **Decisión pendiente:** si `Error.Message` llega a la UI (hoy el controlador lo devuelve en el cuerpo: `StatusCode(result.Error.ToHttpStatusCode(), result.Error)`).
- `Customer` usa `DateTime.UtcNow` dentro del agregado (`Customer.cs:139,165,174`) en lugar de recibir la hora (Notes recibe `nowUtc` desde `TimeProvider`).
- Los 4 contratos de Notes en un solo archivo (`NotesIntegrationEvents.cs`) y con `[MessageIdentity]` = nombre CLR (correcto para contratos ya en el cable, no para contratos nuevos; ver BB-4).

## 6. Stack tecnológico con versiones reales

No existe `Directory.Packages.props` ni `Directory.Build.props`: **cada `.csproj` fija su versión** (Código verificado: búsqueda sin resultados; 59 referencias a `WolverineFx 6.14.0` repetidas).

### 6.1 Obligatorio para un servicio .NET nuevo

| Componente | Versión | Dónde se fija |
|---|---|---|
| .NET SDK | 10.0.300 | `global.json` |
| TargetFramework | `net10.0` | cada `.csproj` |
| Imagen runtime / build | `mcr.microsoft.com/dotnet/aspnet:10.0.9` / `sdk:10.0.300` | `src/Services/*/…Api/Dockerfile` |
| WolverineFx, `.EntityFrameworkCore`, `.RabbitMQ`, `.SqlServer`, `.RuntimeCompilation` | 6.14.0 | `.csproj` del Api (y `WolverineFx` en Application) |
| Microsoft.EntityFrameworkCore.SqlServer / `.Design` | 10.0.9 | Infrastructure / Api |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.9 | Api |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | 10.0.9 | Api |
| Microsoft.AspNetCore.OpenApi / Swashbuckle.AspNetCore | 10.0.9 / 10.2.2 | Api |
| Serilog.AspNetCore / Formatting.Compact / Sinks.File / Sinks.OpenTelemetry | 10.0.0 / 3.0.0 / 7.0.0 / 4.2.0 | transitivo vía `BuildingBlocks.Web` |
| OpenTelemetry (Exporter OTLP, Extensions.Hosting, Instrumentation.Http) / Instrumentation.AspNetCore / Runtime | 1.16.0 / 1.15.2 / 1.15.1 | vía `BuildingBlocks.Web` |
| StackExchange.Redis / Microsoft.Extensions.Caching.StackExchangeRedis | 2.8.16 / 10.0.9 | vía `BuildingBlocks.Infrastructure` |
| Polly | 8.5.0 | vía `BuildingBlocks.Infrastructure` |
| System.Security.Cryptography.Pkcs | 10.0.13 | Infrastructure (fijado en 27 proyectos) |
| Infra: RabbitMQ / Redis / SQL Server externo | `rabbitmq:4.3.2-management` / `redis:7.2.12-alpine` / externo (`SQLSERVER_HOST`) | `deploy/docker/docker-compose.yml` |
| Pruebas | xunit 2.9.3, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 17.14.1, EF InMemory 10.0.9, NetArchTest.Rules 1.3.2, Mvc.Testing 10.0.9, System.IdentityModel.Tokens.Jwt 8.19.1 | `deploy/tests/TaxVision.Notes.Tests/TaxVision.Notes.Tests.csproj` |
| Herramienta EF | `dotnet-ef` por manifiesto | `dotnet-tools.json` |

### 6.2 Condicional (según capacidad)

| Necesidad | Paquete / versión | Quién lo usa hoy |
|---|---|---|
| Subir/bajar objetos | Minio 7.0.0 (AWSSDK.S3 4.0.101 solo CloudStorage) | 8 servicios |
| Cobros | Stripe.net 47.4.0 | PaymentApp, PaymentClient |
| Correo | MailKit / MimeKit 4.17.0 | Connectors, Notification, Postmaster |
| Programación persistente | Quartz 3.15.1 (+ `Quartz.Extensions.Hosting`, `Quartz.Serialization.SystemTextJson`) | Reminder |
| PDF | PdfSharp 6.2.0; Microsoft.Playwright 1.49.0 | Signature, Documents |
| Imágenes | SkiaSharp 3.119.0 (+ NativeAssets) | `BuildingBlocks.Imaging`, Signature |
| Plantillas Liquid | Fluid.Core 2.31.0 (**Notification en 2.14.0**) | Documents, Scribe, Notification |
| Calendario | Ical.Net 5.1.0 | Calendar, Tasks |
| Hojas de cálculo / CSV | ClosedXML 0.105.0; CsvHelper 33.1.0 | Customer, FinancialInsights |
| HTML seguro | HtmlSanitizer 9.1.982 | Notes |
| Criptografía/PKI | BouncyCastle.Cryptography 2.6.2 | Signature |
| Push | FirebaseAdmin 3.1.0 | Notification |
| OAuth Google | Google.Apis.Auth 1.68.0 | Connectors |
| ML | Microsoft.ML.OnnxRuntime 1.31.0 | ContentScanner |
| Proxy | Yarp.ReverseProxy 2.3.0 | Gateway |
| Node (servicio en tiempo real) | Node 24.22.2, TypeScript 5.6.3, Fastify 5.12.7, Prisma 5.22.0, Socket.IO 4.8.1, amqplib 0.10.4, ioredis 5.4.1, jose 5.9.6, zod 4.6.x, pino 9.5.0, Vitest 5.0.3 | Communication y sus workers |

### 6.3 Opcional

| Componente | Versión | Nota |
|---|---|---|
| Observabilidad local | `otel/opentelemetry-collector-contrib:0.153.0` (siempre); `prom/prometheus:v3.5.3`, `grafana/grafana:13.0.3`, `cadvisor:v0.49.1`, `node-exporter:v1.8.2` | Perfil `observability`, apagado por defecto (`docker-compose.yml:188-191`) |
| Escáner de archivos | `clamav/clamav:1.4.3` | Solo CloudStorage lo usa |

### 6.4 Histórico (no usar en servicios nuevos)

| Elemento | Estado | Evidencia |
|---|---|---|
| `UseRabbitMq(uri).AutoProvision()` directo | Reemplazado por `UseTaxVisionRabbitMq` (confirmaciones de publicación) | `src/BuildingBlocks/BuildingBlocks.Web/Hosting/RabbitMqTransportExtensions.cs:33-45`; ningún `Program.cs` usa `UseRabbitMq(` directo |
| JWT HS256 (`Jwt:Secret`) | Soportado en código como alternativa, pero compose usa RS256 con `Jwt__PublicKeyPath` | `src/BuildingBlocks/BuildingBlocks.Web/Security/JwtAuthenticationRegistration.cs:16-18,103-111`; `docker-compose.yml` (`notes-api`) |
| `OnException<Exception>().RetryWithCooldown` por servicio | Reemplazado por `ApplyStandardFailurePolicies()` | `src/BuildingBlocks/Messaging/WolverineFailurePolicies.cs:4-25` |
| Loki, Tempo, SQL Server dentro del compose | Comentados / externo | `docker-compose.yml:188-218`, `:160-185` |
| MediatR, Mapster | No se usan (el libro usa Mapster) | ausentes en todos los `.csproj` |
| Imagen `minio/mc:latest` | Sin versión fija | `docker-compose.yml:2843` |

## 7. Evidencia clave

- Solución y proyectos: `TaxVision.slnx:1-257`.
- Paquetes: agregación de todos los `.csproj` de `src/` y `deploy/tests/`.
- Compose: `deploy/docker/docker-compose.yml` (servicios en líneas 368-3029).
- Gateway: `src/Gateway/TaxVision.Gateway/Program.cs:22-194`, `appsettings.json` (`ReverseProxy`).
- Migraciones en producción: `.github/workflows/deploy.yml:766-770`, `deploy/docker/migrations/apply-migrations.sh`.

## 8. Problemas detectados

1. Sin gestión central de versiones (`Directory.Packages.props`): versiones repetidas en ~60 archivos; ya existe una divergencia (Fluid.Core 2.14.0 vs 2.31.0).
2. Dependencias Node con rango (`mediasoup ^3.21.0`, `nanoid ^5.1.16`) y `minio/mc:latest`: no reproducibles.
3. Tres servicios sin `UseUtcDateTimes`/`UtcDateTimesTests` (Calendar, Signature, Tasks).
4. Ocho servicios sin colas locales durables (Catalog, FinancialInsights, Growth, Inventory, Mobile, Reminder, Scribe, Wallet) y nueve sin despacho de eventos de dominio BB-1 (Catalog, Documents, Inventory, Notes, Notification, Reminder, Scribe, Signature, Sms).
5. Comentario del compose "como los servicios migran al arrancar" (`docker-compose.yml:160-163`) no coincide con el código: ningún servicio llama `Database.Migrate`; el esquema lo aplica el contenedor `migrations` (ver `Investigaciones/Migraciones.md`).
6. Nombre de la fitness del gate de módulo (`The_fourteen_gated_services_are_still_wired`) distinto del número que fija (16).

## 9. Riesgos

- Copiar a ciegas Notes arrastra comentarios con fases y mensajes de error en español.
- Un servicio nuevo que no se agregue a `apply-migrations.sh`, al servicio `migrations` del compose y al `.env` de `deploy.yml` despliega sin esquema (precedente: Billing/Documents, comentado en `docker-compose.yml`, servicio `migrations`).

## 10. Huecos de evidencia

- Conteo de consumidores aproximado por patrón; Customer subcontado.
- No se verificó en vivo ninguna ruta (trabajo estático).
- Responsabilidad de FinancialInsights tomada de su nombre y `documents/Financial_Insights.md` (no leído completo).
