# Arquitectura real del sistema

## Inventario ejecutable

El compose levanta RabbitMQ, Redis, MinIO, ClamAV, SQL Server, Loki, Tempo, OpenTelemetry, Prometheus, Grafana, Caddy, gateway y APIs de Tenant, Auth, Customer, Subscription, Notification, Postmaster, Scribe, Connectors, Correspondence, Notes, CloudStorage, Signature, PaymentApp, PaymentClient, Growth, Billing, Documents y Communication, además de CommunicationTranscriptWorker y un contenedor de migraciones.

`src/Services` contiene 18 áreas: Auth, Billing, CloudStorage, Communication (TypeScript), CommunicationTranscriptWorker, Connectors, Correspondence, Customer, Documents, Growth, Notification, PaymentApp, PaymentClient, Postmaster, Scribe, Signature, Subscription y Tenant. `src/BuildingBlocks` contiene contratos de mensajería, tenancy, autorización, persistencia, seguridad, caching, rate limits y resiliencia. No hay frontend de producto en este repositorio; solo backend/gateway y componentes Node de comunicación.

## Mapa E2E real de onboarding comercial

```text
Cliente → Gateway/Caddy → Auth.Onboarding
                         ├→ Subscription (precio/plan)
                         ├→ Growth (quote → reserve → commit)
                         └→ PaymentApp → Stripe
                                      ↓ webhook/evento durable
                         Auth.OnboardingSuccessCompleter
                         ├→ registro/token/saga de tenant
                         └→ OnboardingFinalizer → evento RabbitMQ
                                                ↓
                                             Billing → Documents → MinIO
                         Tenant/Auth/Subscription → provisioning/owner/activation
```

## Datos y dependencias principales

| Componente | Datos propios | Consume/llama | Publica |
|---|---|---|---|
| Auth | usuarios, dominios, onboarding, reservas referenciadas, saga | Growth, Subscription, PaymentApp, Tenant, Documents | eventos de onboarding/identity e invoice request |
| Growth | definitions, rules, quotes, reservations, redemptions, referrals, counters | Auth M2M para token/identidad indirecta | lifecycle de códigos/referrals |
| PaymentApp | SaaS payments, attempts, refunds, provider refs | Subscription, Stripe, Growth en otros flujos | payment succeeded/failed/refunded |
| Billing | invoices, lines, adjustments, issuer/customer snapshots, sequences | PaymentApp/PaymentClient, Documents | invoice/payment-link events y comandos PDF |
| Documents | metadatos/render de documentos y objetos | almacenamiento | completion events |
| Subscription | catálogo, planes, suscripciones, seats/add-ons | PaymentApp | lifecycle/due events |
| Tenant | tenant y provisioning | Auth/servicios dependientes | tenant lifecycle |
| Notification/Postmaster | preferencias/log/campañas y entrega | SMTP/providers/Templates | delivery callbacks |

## Infraestructura y CI/CD

Docker Compose es la topología operacional local. `.github` contiene workflows; `deploy/tests` contiene proyectos xUnit y composición de integración; `deploy/loadtests` contiene k6. Observabilidad está preconfigurada. No se halló manifiesto Kubernetes en el inventario. La solución usa SQL Server por servicio lógico, RabbitMQ/Wolverine durable, Redis y MinIO.

## Evaluación

La separación de dominios es visible, pero Auth funciona como orquestador central y conserva conocimiento de pricing, códigos, payment y provisioning. El flujo es una saga de facto repartida entre aggregate, consumers y llamadas HTTP; no existe un único estado persistido que pruebe que todos los hitos financieros terminaron.

