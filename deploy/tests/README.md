# TaxVision automated tests

Cada microservicio implementado tiene un proyecto xUnit:

- `TaxVision.Auth.Tests`
- `TaxVision.Tenant.Tests`
- `TaxVision.Customer.Tests`
- `TaxVision.Subscription.Tests`
- `TaxVision.Notification.Tests`
- `TaxVision.CloudStorage.Tests`

`Billing` no tiene tests porque todavia no contiene ningun proyecto o dominio.

## Ejecutar en el host

```powershell
dotnet test TaxVision.slnx
```

Un proyecto individual:

```powershell
dotnet test deploy/tests/TaxVision.Auth.Tests/TaxVision.Auth.Tests.csproj
dotnet test deploy/tests/TaxVision.CloudStorage.Tests/TaxVision.CloudStorage.Tests.csproj
```

## Ejecutar con Docker

Desde la raiz:

```powershell
docker compose -f deploy/tests/docker-compose.tests.yml build
docker compose -f deploy/tests/docker-compose.tests.yml run --rm tests
```

## Construir, migrar y levantar TaxVision

```powershell
# Construir imagenes
docker compose --env-file .env -f deploy/docker/docker-compose.yml build

# Aplicar todas las migraciones usando el runner Docker
docker compose --env-file .env -f deploy/docker/docker-compose.yml `
  --profile tools run --build --rm migrations

# Levantar el stack
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d

# Ver estado y readiness
docker compose --env-file .env -f deploy/docker/docker-compose.yml ps
```

Para detenerlo:

```powershell
docker compose --env-file .env -f deploy/docker/docker-compose.yml down
```
