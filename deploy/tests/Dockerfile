FROM mcr.microsoft.com/dotnet/sdk:10.0.300
WORKDIR /src

COPY . .
RUN dotnet restore TaxVision.slnx

ENTRYPOINT ["dotnet", "test", "TaxVision.slnx", "-c", "Release", "--no-restore", "--logger", "console;verbosity=normal"]
