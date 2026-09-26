# Cómo compilar, probar y levantar este repo

SDK: **.NET 10.0.300** (`global.json`). Solución: **`TaxVision.slnx`**.
Framework de tests: **xUnit**. 26 proyectos en `deploy/tests/TaxVision.<Servicio>.Tests/`.
Communication (Node) tiene su propia suite: **Vitest**, en `src/Services/Communication/`.

---

## 1. Comandos exactos

### Build

```bash
dotnet build TaxVision.slnx -c Release
```

### Gate de CI — el comando **exacto** de `.github/workflows/deploy.yml`

```bash
dotnet test TaxVision.slnx --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"
```

Añade `-m:1` al correrlo en local: la suite completa necesita ejecución **serial**.
**Nunca** lo pipees a `tail` ni a `head`: enmascara el código de salida. Redirige a un archivo y
después lee el archivo.

### Tests de integración (necesitan base de datos), por servicio y en serie

```bash
dotnet test deploy/tests/TaxVision.Auth.Tests --filter "FullyQualifiedName~Integration" -m:1
```

### Formato — **acotado a los archivos de la fase**, nunca a todo el repo

```bash
dotnet csharpier check src/Services/Auth/Domain/Roles/Role.cs
dotnet csharpier format src/Services/Auth/Domain/Roles/Role.cs
```

### Communication (Node)

```bash
cd src/Services/Communication
npm ci
npm run typecheck
npm test
```

El lint de este subproyecto ya está en rojo de base: **no es gate**. El gate es `typecheck` + `test`.

### Migraciones

```bash
dotnet ef migrations add <Nombre> -p src/Services/<Svc>/<...>.Infrastructure -s src/Services/<Svc>/<...>.Api
dotnet ef database update      -p src/Services/<Svc>/<...>.Infrastructure -s src/Services/<Svc>/<...>.Api
```

**Se aplica en el acto**, no se deja pendiente. Una migración sin aplicar produce drift y una cascada
de 503 en el Gateway. Prisma (Communication): `npx prisma migrate dev`.

---

## 2. Levantar el proyecto

### Opción 1 — con Docker (preferida si el entorno lo permite)

`deploy/docker/docker-compose.yml` es el de **producción**: 47 servicios y **SQL Server externo**. No
sirve para desarrollo.

Para esta sesión hay un compose propio con solo la infraestructura:

```bash
cd deploy/docker
docker compose -f docker-compose.claude.yml up -d
docker compose -f docker-compose.claude.yml ps
```

Levanta SQL Server 2022, RabbitMQ, Redis y MinIO en puertos locales, con **credenciales de ejemplo**.
Después, aplica las migraciones y arranca solo los servicios que la fase necesite:

```bash
dotnet run --project src/Services/Auth/Api
```

> `dotnet run` resuelve el *content root* desde el **cwd**: lánzalo desde la raíz del repo con
> `--project`, no con `cd`.

### Opción 2 — sin Docker

Build + gate de CI + los tests de Communication. Los tests de integración y de persistencia quedan
fuera (están excluidos del gate a propósito).

**Documenta en `PROGRESO.md` y en el PR qué quedó sin verificar.**

---

## 3. Variables de entorno

Solo valores de **ejemplo**. Local se configura con `dotnet user-secrets`; producción con
`deploy/docker/docker-compose.yml`. **Nunca** se escriben secretos reales en un archivo del repo.

| Variable | Ejemplo | Para qué |
|---|---|---|
| `ConnectionStrings__Default` | `Server=localhost,1433;Database=TaxVision_Auth;User Id=sa;Password=CHANGE_ME;TrustServerCertificate=True` | Base de datos del servicio |
| `RabbitMq__Host` / `__User` / `__Password` | `localhost` / `guest` / `CHANGE_ME` | Bus de eventos |
| `Redis__ConnectionString` | `localhost:6379` | Denylist de sesión y caches |
| `Minio__Endpoint` / `__AccessKey` / `__SecretKey` | `localhost:9000` / `CHANGE_ME` / `CHANGE_ME` | Almacenamiento de archivos |
| `Authorization__PermissionsSource` | `Projection` | **Obligatorio** en todo servicio con `[HasPermission]`: en otro modo revienta al arrancar |
| `Authorization__ModuleGate__Enforce` | `false` | Gate de módulos. **Hoy no está definido en ninguna parte y el default es `false`** |
| `Authorization__ResourceOwnership__Enabled` | `true` | Capa de ownership |
| `<Svc>__AssignmentVisibility__Enabled` | `false` | Visibilidad por asignación. **Apagado en producción** |
| `Encryption__MasterKey` | `CHANGE_ME` | Cifrado de secretos en reposo |

`appsettings.Claude.json` (en la raíz de cada `Api`) trae estos valores de ejemplo apuntando al compose
de arriba. **No** toca las configuraciones existentes.

---

## 4. Trampas conocidas de este repo

- **MSB3027 / MSB3021 al compilar:** un servicio corriendo tiene las DLLs bloqueadas. Para el proceso
  antes de compilar. Si tocaste `BuildingBlocks` o un proyecto compartido, para **todos**.
- **Wolverine codegen arranca el servicio.** No uses `codegen preview` para verificar nada.
- **Un sufijo plural rompe el descubrimiento de handlers** de Wolverine.
- **Un job sin `Include` lee colecciones vacías**: el filtro global y el lazy loading apagado hacen que
  una navegación no cargada se vea como vacía, no como error.
- **Un `MeterListener` sobre un `Meter` de nombre fijo se contamina entre clases de test en paralelo.**
  Esas clases van en una `[Collection]` compartida.
- **En C#, llamar a un extension method "pelado" dentro de un método de instancia no compila**: hace
  falta `this.` explícito.
- **Git Bash en Windows mangla rutas absolutas Unix** que se pasan a Docker: prefija `MSYS_NO_PATHCONV=1`.
- **Un 404 no significa que algo se borró.** Verifica antes de concluirlo.
