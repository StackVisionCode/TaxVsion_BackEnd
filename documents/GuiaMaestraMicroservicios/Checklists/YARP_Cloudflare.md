# Checklist — Gateway YARP, Caddy y Cloudflare

> Propósito: publicar un servicio por la única entrada de la plataforma (Cloudflare → Tunnel → Caddy → Gateway YARP → servicio) sin exponer puertos ni superficie interna, y saber cuándo hace falta un cambio manual en Cloudflare.
> Cloudflare no vive en el repo: lo cambia **solo el senior, a mano, con aprobación del dueño**. Ni el desarrollador ni un agente lo tocan.

**Cuándo aplica:** servicio nuevo con API, prefijo nuevo, endpoint público abierto desde el navegador, hostname nuevo, puerto no HTTP o producto de Cloudflare (Turnstile).

**Formato:** *Verificación* = comando, prueba o archivo; *Origen* = investigación. Condicionales entre corchetes.

---

## 1. Ruta y cluster en el Gateway

- [ ] **Una ruta por prefijo y un cluster por servicio** en `src/Gateway/TaxVision.Gateway/appsettings.json`: `Match.Path: /<prefijo>/{**catch-all}`, destino local `http://localhost:<puerto>/`, health check activo idéntico (`/health/live`, 15 s, timeout 5 s, `ConsecutiveFailures`, umbral 3), pasivo apagado, `HealthyOrPanic`.
  - Verificación: `GatewayHealthConfigurationTests` en el gate.
  - Origen: [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §9.1 y §2.6.
- [ ] **Criticidad obligatoria** en `LoadShedding:Criticality` (`Critical`, `Standard` o `Background`) con la clave = primer segmento del path.
  - Verificación: `LoadSheddingCriticalityCoverageTests` (falla si una ruta no tiene entrada o sobra una).
  - Origen: [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §9.2.
- [ ] **Prefijo sin choque** con prefijos existentes ni con los buckets que Caddy manda a MinIO (`/taxvision-storage`, `/taxvision-temp`, `/taxvision-quarantine`).
  - Verificación: revisar `ReverseProxy.Routes` y el snippet `(taxvision_api)` del Caddyfile.
  - Origen: [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §6; [Cloudflare](../Investigaciones/Cloudflare.md) §5.
- [ ] **Override de destino en compose** (bloque `gateway`): `ReverseProxy__Clusters__<svc>__Destinations__<svc>1__Address: http://<svc>-api:8080/` y el servicio en `depends_on`; repetir en `deploy/docker/docker-compose.local.yml:39-66` si corre en local. No hay prueba que lo exija: sin él, 502 en producción.
  - Verificación: revisión del compose (hueco conocido).
  - Origen: [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §3.1 y §9.3; [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §9.3.
- [ ] **Nada bajo `internal`** en el Gateway: los endpoints M2M se llaman por DNS de Docker.
  - Verificación: `InternalRouteConventionFitnessTests` (BB).
  - Origen: [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §9.5.
- [ ] [si el cuerpo es grande o debe ser pequeño] `MaxRequestBodySize` por ruta; [si hay streaming o archivos] `HttpRequest.ActivityTimeout` en el cluster.
  - Verificación: patrón `calendar-public` / `cloudstorage`.
  - Origen: [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §5 y §9.1.
- [ ] [si hay endpoint anónimo con riesgo de abuso] Regla en `GatewayRateLimiting:PreAuthByIp:Rules`.
  - Verificación: patrón `GatewayPublicBookingRouteTests`.
  - Origen: [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §9.4.
- [ ] **Verificación local**: `http://localhost:5047/<prefijo>/...` sin token → 401 del servicio (no 404 del Gateway); `GET /health/dependencies` muestra `Degraded` si el cluster cae.
  - Verificación: llamada manual contra la flota local.
  - Origen: [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §9.6.

## 2. Caddy (origen, en el repo)

- [ ] [si el usuario abre el endpoint en el navegador desde un host de oficina: link de correo, QR] **Regla dentro de `(taxvision_tenant)`**, después de `@minio` y antes de `@spa_assets`/`@spa_nav`, y el prefijo en los dos `[InlineData]` de `CaddyPublicNavigationRouteTests`.
  - Verificación: `CaddyPublicNavigationRouteTests` en el gate.
  - Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §9.1.
- [ ] **Sin dominios literales** en Caddy ni en compose (todo de `TAXVISION_BASE_DOMAIN` / `TAXVISION_DOMAIN`).
  - Verificación: `PlatformDomainSingleSourceDeployTests.Caddyfile_has_no_literal_hosts_outside_the_domain_variables`.
  - Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §4 y §9.3.
- [ ] **Ninguna API publica `ports:`**; el Gateway es la única entrada (excepción declarada: SFU de Communication).
  - Verificación: `DockerComposeNoPublicApiPortsFitnessTests` (Auth.Tests).
  - Origen: [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §2.4 y §6.
- [ ] **IP real**: `AddTaxVisionClientIpForwarding` + `UseTaxVisionClientIp` en el `Program.cs`.
  - Verificación: `ClientIpForwardingFitnessTests` (BB).
  - Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §2.2.

## 3. ¿Hace falta Cloudflare?

| Cambio | ¿Cloudflare? | ¿Repo? |
|---|---|---|
| Path nuevo bajo `api.<dominio>` o subdominio de oficina consumido por XHR | No (confirmar que no hay reglas WAF/caché por path) | Ruta YARP + compose |
| Endpoint público abierto en el navegador en host de oficina | No | Regla Caddy + `CaddyPublicNavigationRouteTests` |
| Prefijo que choca con buckets de MinIO | No | Elegir otro prefijo |
| Hostname nuevo (otra app, subdominio fijo, TURN, webhook dedicado) | **Sí** (public hostname en el túnel o registro DNS) | Posible bloque en Caddy y CORS |
| Puerto no HTTP expuesto (UDP, TURN, SFU) | **Sí** (DNS only, nube gris, y reenvío en router) | `ports:` justificado + excepción en `DockerComposeNoPublicApiPortsFitnessTests` |
| Turnstile u otro producto de Cloudflare | **Sí** (widget/claves) | Secrets en GitHub |

Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §5.

## 4. Procedimiento manual de Cloudflare (11 pasos)

- [ ] **1. Confirmar la necesidad** con la tabla §3: si basta con YARP o Caddy, no se pide nada a Cloudflare.
  - Verificación: fila aplicada escrita en el PR. · Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §5.
- [ ] **2. Descartar choques**: hostname/prefijo libre, sin dominios literales, sin reutilizar rutas de MinIO.
  - Verificación: `PlatformDomainSingleSourceDeployTests`; revisión del Caddyfile. · Origen: §5 y §9.3.
- [ ] **3. Elegir modo**: proxied (nube naranja) solo para HTTP/HTTPS; **DNS only** (nube gris) para protocolos no HTTP (un registro proxied para UDP/TURN nunca llega al servidor, sin error visible).
  - Verificación: decisión en la solicitud. · Origen: §2.3 y §6.
- [ ] **4. Preparar los cambios del repo** en el mismo cambio: bloque de Caddy o CORS si hace falta, `ports:` justificado con su excepción en la fitness, gate verde.
  - Verificación: gate exacto verde. · Origen: §5.
- [ ] **5. Secrets** [si el producto los usa, p. ej. Turnstile]: site key y secret como secrets de GitHub en `production-extra`, escritos en el job `deploy` y listados en `ProductionExtraSecretsDeployTests`. No reutilizar el token DNS de Caddy (`CLOUDFLARE_DNS_API_TOKEN`) para otros usos.
  - Verificación: `ProductionExtraSecretsDeployTests`. · Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §2.3 y §9.3; [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §5.
- [ ] **6. Redactar la solicitud al senior** con: hostname, servicio destino (`http://localhost:<puerto>`), proxied o DNS only, fecha y cómo se verifica (`dig`, `curl -I https://<host>/health/live`). (La plantilla está marcada como decisión pendiente en la investigación: usar estos campos mínimos.)
  - Verificación: solicitud escrita con los cinco campos. · Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §5.
- [ ] **7. Aprobación del dueño** antes de que el senior toque el panel.
  - Verificación: aprobación registrada. · Origen: reglas consolidadas de la guía; [Cloudflare](../Investigaciones/Cloudflare.md) §1.
- [ ] **8. El senior aplica el cambio** en el panel (túnel, DNS, Turnstile). Nadie más lo toca.
  - Verificación: confirmación del senior. · Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §1 y §2.3.
- [ ] **9. Verificar DNS**: `dig +short <host>` → proxied devuelve IPs de Cloudflare; DNS only devuelve la IP del servidor.
  - Verificación: salida del comando. · Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §9.2.
- [ ] **10. Verificar HTTP**: `curl -I https://<host>/health/live` → 200 del Gateway; para la API, el paso final de `deploy.yml` consulta `https://${TAXVISION_DOMAIN}/health/ready` (prueba solo Caddy → Gateway; hacer además una petición real).
  - Verificación: salida de `curl` y una request real por el Gateway. · Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §9.2; [Observabilidad](../Investigaciones/Observabilidad.md) §2.4.
- [ ] **11. Dejar constancia** de la solicitud (campos del paso 6, fecha y resultado de 9-10) y confirmar con el senior que ninguna regla WAF/caché/Page Rule afecta el path (sin evidencia escrita hoy).
  - Verificación: constancia guardada. · Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §3.2-3.3.

## 5. Qué NO hacer

- [ ] No publicar un servicio directo a internet "para probar": todo entra por Caddy → Gateway.
  - Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §9.3.
- [ ] No quitar el transform de `CF-Connecting-IP` del Gateway ni `UseWebSockets()` (socket.io en long-polling se rompe con el buffering de Cloudflare).
  - Verificación: `GatewayWebSocketPipelineTests`. · Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §6.
- [ ] No tocar Cloudflare, Docker ni el túnel desde una sesión de agente.
  - Origen: [Cloudflare](../Investigaciones/Cloudflare.md) encabezado; reglas del proyecto.

## Decisiones pendientes

- Dónde corre `cloudflared` (VM frontend vs backend) ([Cloudflare](../Investigaciones/Cloudflare.md) §3.1).
- Plantilla y canal formal de solicitud al senior ([Cloudflare](../Investigaciones/Cloudflare.md) §3.2).
- Prueba que compare clusters de `appsettings.json` contra overrides del compose ([Gateway_YARP](../Investigaciones/Gateway_YARP.md) §5).
