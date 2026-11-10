FROM mcr.microsoft.com/dotnet/sdk:10.0.300
WORKDIR /src

ENV PATH="${PATH}:/root/.dotnet/tools"

COPY . .
# dotnet-ef sale del manifiesto del repo (dotnet-tools.json): con él presente, la global no se usa.
RUN dotnet tool restore
RUN dotnet restore TaxVision.slnx
RUN dotnet build TaxVision.slnx -c Release --no-restore

RUN chmod +x /src/deploy/docker/migrations/apply-migrations.sh
ENTRYPOINT ["/src/deploy/docker/migrations/apply-migrations.sh"]
