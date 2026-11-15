# Estado de la investigación

> Tablero de avance de la Guía Maestra. Se actualiza al cerrar cada frente. Investigación de **solo lectura**: no se modificó código, Docker, bases de datos ni Cloudflare.

## Frentes

| # | Frente | Investigaciones | Estado |
|---|---|---|---|
| 1 | Arquitectura, inventario, BuildingBlocks, migraciones, guardrails, comparativa | `03_INVENTARIO_ARQUITECTURA.md`, `02_COMPARATIVA_GUIAS.md`, `Investigaciones/Arquitectura.md`, `Investigaciones/BuildingBlocks.md`, `Investigaciones/Migraciones.md`, `Referencias/Guardrails.md` | Hecho (2026-11-15) |
| 2 | Seguridad y datos compartidos | `Auth_RBAC.md`, `Session_Denylist.md`, `M2M_ServiceAuth.md`, `Proyecciones.md`, `Reconciliation.md`, `RateLimit_Cuotas_Gates.md` | Hecho (2026-11-15) |
| 3 | Offboarding de empleados | `Offboarding.md`, `Offboarding_Integraciones.md` | Hecho (2026-11-15) |
| 4 | Comercialización, mensajería, compilación en producción | `Modulos_Planes_Addons.md`, `Wolverine_RabbitMQ.md`, `Roslyn_Produccion.md` | Hecho (2026-11-15) |
| 5 | Tenants, enlaces y notificaciones | `Tenant_Subdomains.md`, `Tenant_Links.md`, `Scribe_Templates.md`, `Notification_Email.md`, `Notification_SMS.md`, `Notification_InApp_Push.md` | Hecho (2026-11-15) |
| 6 | Operaciones y testing | `Docker_Deploy.md`, `Scripts_Locales.md`, `Gateway_YARP.md`, `Cloudflare.md`, `CI_CD_Secrets.md`, `Postman.md`, `Observabilidad.md`, `Testing_*.md` | Hecho (2026-11-15) |

## Etapas

| Etapa | Qué | Estado |
|---|---|---|
| A | Inventario de proyectos, guías y documentación | Hecho (2026-11-15) |
| B–C | Investigación por tema con evidencia | Hecho (2026-11-15) |
| D | Consolidación de contradicciones | Hecho: 58 contradicciones resueltas y ~85 decisiones pendientes (`Referencias/Decisiones_Arquitectonicas.md`) |
| E | Superguía | Hecho: 37 capítulos + anexo |
| F | Diagramas SVG | Hecho: 23 SVG, XML válido, sin recursos externos |
| G | Checklists | Hecho: 10 checklists, 322 ítems |
| H | Validación final | Hecho: 48 Markdown y 23 SVG; 0 enlaces rotos, 0 secretos o IPs, 0 archivos vacíos; backend sin cambios |

## Inventario inicial (etapa A)

- Backend: `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`, rama `Develop`, árbol limpio al empezar.
- 30 carpetas en `src/Services` (27 .NET con tests + 3 Node: Communication, CommunicationRecorder, CommunicationTranscriptWorker), Gateway en `src/Gateway`, 23 BuildingBlocks.
- 31 proyectos de tests en `deploy/tests`, más `Shared` y `TaxVision.BuildingBlocks.Testing`.
- Un solo workflow: `.github/workflows/deploy.yml`.
- Scripts: `start-fleet.ps1`, `stop-fleet.ps1`, `restart-down.ps1`, `migrate-local.ps1`, `codegen/regenerate-wolverine-code.ps1`.
- Guía antigua: `Implementaciones/DEVS/Guia_Creacion_Microservicio.md` (sigue en su lugar).
- `documents/`: 3 guías (CloudStorage, Notificaciones Email, IgnoreQueryFilters/TenantContext), `RBAC_Endurecimiento_Defensa_Senior.md`, `RateLimit/`, `architecture/` (ADR 017, 018).
- `docs/`: `audit/` (17 documentos), `seats/`, guías de proveedores de almacenamiento.
- Libro: `Implementaciones/DomainDrivenDesign/Guia-DDD-EDA-NET10-Wolverine-RabbitMQ-Docker.pdf`.
- Vault Obsidian vigente: `Implementaciones/CommunicationCorrecciones` (proyectos Calendar, PublicDemoScheduling, TenantTimeZones).
- Postman: `postman/` y `Postman_Collection/`.
