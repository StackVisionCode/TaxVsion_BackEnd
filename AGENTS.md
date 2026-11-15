# Instrucciones de compilacion y pruebas

Ejecuta los comandos desde la raiz de este repositorio, salvo cuando se indique
otro directorio. Verifica primero la ruta y el estado de Git; conserva los cambios
existentes. Este archivo describe el checkout real del backend TaxVision.

## Estructura del repositorio

| Ruta | Contenido |
| --- | --- |
| `TaxVision.slnx`, `global.json` | Solucion .NET y version del SDK (10.0.300) |
| `src/Gateway/`, `src/Services/`, `src/BuildingBlocks/` | Gateway, servicios .NET y componentes compartidos |
| `deploy/tests/` | Proyectos de pruebas .NET y Compose para pruebas |
| `src/Services/Communication/` | API Node.js/TypeScript con Fastify, Socket.IO, Mediasoup y Prisma |
| `src/Services/CommunicationRecorder/` | Worker Node.js de grabaciones |
| `src/Services/CommunicationTranscriptWorker/` | Worker Node.js de transcripcion con `whisper.cpp` |
| `src/Services/FinancialInsights/` | Servicio .NET de registro de lotes financieros por tenant y cliente |
| `deploy/docker/` | Compose, overrides locales y wrapper `compose.ps1` |

Los frontends Angular **no estan en este repositorio**. En el workspace del
proyecto se encuentran como carpetas hermanas: `../TaxVsion_Front/` (CRM) y
`../WebClient/` (portal). En un checkout aislado pueden no estar presentes.
Tampoco existen aqui `frontend/`, `api-dotnet/`, `api-node/` ni un
`docker-compose.yml` en la raiz.

## Requisitos

- .NET SDK 10.0.300 segun `global.json`.
- Node.js 24 o superior y npm para los servicios Node.
- Docker y Docker Compose solo para pruebas con contenedores o ejecucion del stack.
- Para pruebas de integracion, configura servicios y credenciales de un entorno
  aislado. No uses datos, volumenes ni secretos de produccion.

## Validacion .NET

```powershell
dotnet restore TaxVision.slnx
dotnet build TaxVision.slnx --no-restore
dotnet test TaxVision.slnx --no-restore
```

Para una prueba puntual, usa el proyecto correspondiente. Ejemplo:

```powershell
dotnet test deploy/tests/TaxVision.CloudStorage.Tests/TaxVision.CloudStorage.Tests.csproj
dotnet test deploy/tests/TaxVision.FinancialInsights.Tests/TaxVision.FinancialInsights.Tests.csproj
```

Las pruebas .NET tambien pueden ejecutarse en la imagen definida en
`deploy/tests/docker-compose.tests.yml`:

```powershell
docker compose -f deploy/tests/docker-compose.tests.yml build
docker compose -f deploy/tests/docker-compose.tests.yml run --rm tests
```

## Validacion Node.js

Cada servicio tiene su propio `package-lock.json`. Ejecuta en cada uno:

```powershell
cd src/Services/Communication
npm ci
npm run typecheck
npm run build
npm test
cd ../../..

cd src/Services/CommunicationRecorder
npm ci
npm run typecheck
npm run build
npm test
cd ../../..

cd src/Services/CommunicationTranscriptWorker
npm ci
npm run typecheck
npm run build
npm test
cd ../../..
```

`npm test` ejecuta las pruebas unitarias con Vitest. Las pruebas de integracion
de Communication y CommunicationRecorder se ejecutan por separado con
`npm run test:integration` desde el directorio del servicio y requieren sus
dependencias externas configuradas. No interpretes una prueba unitaria verde
como verificacion de SQL Server, RabbitMQ, MinIO o WebRTC en ejecucion.

## Frontends Angular, si estan disponibles

Desde la raiz de este backend, en PowerShell:

```powershell
cd ../TaxVsion_Front
npm ci
npm run build
npm test -- --watch=false

cd ../WebClient
npm ci
npm run build
npm test -- --watch=false
```

Ejecuta cada bloque de frontend solo si existe la carpeta hermana. Los
frontends tienen repositorios y dependencias propios.

## Docker y servicios locales

El wrapper `deploy/docker/compose.ps1` usa el `.env` de la raiz. Prepara un
`.env` **exclusivo del sandbox** con los valores requeridos por Compose; no
copies uno real. El modo `local` tambien requiere
`dev-keys/jwt-private.pem` y `dev-keys/jwt-public.pem` de desarrollo.

```powershell
.\deploy\docker\compose.ps1 infra up -d
.\deploy\docker\compose.ps1 infra ps

.\deploy\docker\compose.ps1 local up -d --build
.\deploy\docker\compose.ps1 local ps
.\deploy\docker\compose.ps1 local logs -f gateway
.\deploy\docker\compose.ps1 local down
```

`infra` levanta RabbitMQ, Redis, MinIO, ClamAV y ContentScanner. `local` levanta el stack de
desarrollo sin Caddy, PaymentApp ni Signature. El comando `ps` comprueba el
estado de los contenedores, no la salud funcional de todos los servicios;
comprueba los endpoints `/health/live` y `/health/ready` del servicio bajo
prueba. Evita `down -v` si hay datos que conservar.

Para mas detalles de pruebas y migraciones, consulta `deploy/tests/README.md`
y `README.md`.
