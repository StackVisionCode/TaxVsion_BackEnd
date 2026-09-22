# Guía DEVS — Directorio de clientes compartido (cache reutilizable)

Backlog **5.1**. Cómo consultar/buscar clientes (`/customers`) desde cualquier módulo del frontend
sin re-descargar el directorio en cada uno, sin cargar miles de filas en memoria y sin servir datos
obsoletos.

Servicios: **Customer.Api** (maestro `/customers`), **Communication** (emit realtime), frontend
`core/customers`. Módulos consumidores hoy: **mail, task, signature, billing, documents, sms,
dashboard** (+ Clients como dueño/mutador).

---

## 1. El problema y la decisión

Antes cada módulo tenía su propio service contra `GET /customers` y su propia réplica de DTO, y
cargaba lotes grandes (200/500) que se redescargaban al cambiar de sección. La decisión **no** fue
"cargar todo el directorio una vez y tenerlo en memoria" (mala UX con ~10k clientes: latencia,
memoria, staleness), sino un **punto común de acceso** con cache de consultas + búsqueda server-side
+ invalidación fina.

Aislamiento de tenant: cada tenant vive en su propio subdominio (origen), así que el cache del front
es naturalmente por-tenant; no hace falta el tenantId en las claves.

---

## 2. Frontend — `CustomerDirectoryStore` (`core/customers`)

`@Injectable({ providedIn: 'root' })`. **Única puerta** a `/customers` desde las features.

| Miembro | Qué hace |
|---|---|
| `search(params)` | Búsqueda server-side cacheada por clave (`status\|term\|page\|size`). TTL 60s + **dedup de peticiones en vuelo** (dos módulos que piden lo mismo → una sola llamada). Devuelve `PagedResult<CustomerSummary>`. |
| `byId(ids[])` | Resuelve clientes por id (nombres en tarjetas/listados). Se sirve del cache que **puebla `search`**; solo pide a la red los faltantes. |
| `recent` (signal) + `addRecent(c)` | Últimos clientes elegidos (localStorage), para pickers sin teclear. |
| `invalidate()` | Descarta TODO el cache (tras una mutación en Clients, o al reconectar el socket). |
| `evict(customerId)` | Desaloja un cliente puntual (evento realtime). |

`CustomerSummary`, `CustomerStatusFilter`, `PagedResult`, `CustomerSearchParams`, `MonthlyNewCustomers`,
`CustomerDirectoryOverview` viven en `core/customers/customer-summary.model.ts` — **DTO canónico**
compartido (no crear réplicas por-feature).

### Cómo consumir (patrón typeahead)

```ts
private readonly directory = inject(CustomerDirectoryStore);

// typeahead: debounce + switchMap, cancela la anterior
this.search$.pipe(
  debounceTime(250), distinctUntilChanged(),
  switchMap(term => this.directory.search({ term, status: 'NotArchived', size: 20 })),
).subscribe(page => this.results.set(page.items));

// resolver nombres de ids ya presentes (tarjetas):
this.directory.byId(customerIds).subscribe(map => /* map.get(id)?.displayName */);
```

### Reglas
- **No** crear un service/`searchCustomers` por feature ni una réplica de `CustomerSummary`: usar el store.
- **No** cargar el directorio completo ni "traer N y filtrar en cliente": la búsqueda es server-side.
- Para agregados (contadores, altas por mes) usar el endpoint de overview (§4), no traer filas.

---

## 3. Frescura (cómo el cache no queda viejo)

Tres capas, de menor a mayor precisión:
1. **TTL 60s** por consulta (SWR barato).
2. **Mutaciones en Clients**: `ClientsStore.afterMutation()` llama `directory.invalidate()` → tras
   crear/editar/archivar/bulk, los pickers se refrescan al toque.
3. **Realtime (F4)**: Communication emite `customer.changed {customerId, changeType}` al room del
   tenant al aplicar un `customer.*.v1`; el store hace `evict(customerId)`. Al reconectar el socket
   (`reconnected$`) hace `invalidate()` por si se perdieron eventos.

El realtime usa el socket compartido `CommunicationRealtimeService` (`on('customer.changed')`).

---

## 4. Backend — guardrails y agregación (Customer.Api)

- **Cap de `size`**: `GET /customers` clampa `size` a **[1,100]** (`CustomerReadService.MaxPageSize`).
  Evita barrer el directorio en una request. El `totalCount` exacto no se capa. Cubre también
  `/internal/customers/list` (mismo handler).
- **ETag / 304**: `GET /customers` devuelve un weak ETag (SHA-256 del contenido) + `Cache-Control:
  private, no-cache`; ante `If-None-Match` coincidente responde **304** sin cuerpo. El navegador
  revalida solo (Angular HttpClient pasa por el cache HTTP) — sin código en el front.
- **Overview**: `GET /customers/overview?months=6` → `{ totalCount, monthly:[{year,month,count}],
  recent:[summary×3] }`. El dashboard lo usa en vez de traer cientos de filas para agrupar.

---

## 5. Fuera de alcance (a propósito)

- **chat / meetings** NO usan este store: consultan `GET /communication/directory/customers` (la
  proyección propia de Communication, con `portalUserId`, para iniciar chat/meeting). Es otro
  directorio, otro propósito.
- **Clients** (módulo dueño) mantiene su propio `ClientsStore`/`ClientsService` (CRUD, detalle,
  fiscal, bulk, counts) — no es un consumidor de picker; solo **invalida** el cache compartido al mutar.
- Servicios backend (Correspondence/Notification/Signature/Notes/Tasks/Calendar/Communication) ya
  tienen su **proyección local** del cliente vía eventos `customer.*.v1` + reconciliación M2M
  (`GET /internal/customers/reconciliation`); no consumen este cache del front.
