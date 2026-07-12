FROM mcr.microsoft.com/dotnet/sdk:10.0.300
WORKDIR /src

ENV PATH="${PATH}:/root/.dotnet/tools"

COPY . .
RUN dotnet tool install --global dotnet-ef --version 10.0.9
RUN dotnet restore TaxVision.slnx
RUN dotnet build TaxVision.slnx -c Release --no-restore

RUN chmod +x /src/deploy/docker/migrations/apply-migrations.sh
ENTRYPOINT ["/src/deploy/docker/migrations/apply-migrations.sh"]
