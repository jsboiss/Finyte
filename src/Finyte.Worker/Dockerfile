FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Finyte.Core/Finyte.Core.csproj src/Finyte.Core/
COPY src/Finyte.Data/Finyte.Data.csproj src/Finyte.Data/
COPY src/Finyte.Worker/Finyte.Worker.csproj src/Finyte.Worker/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked dotnet restore src/Finyte.Worker/Finyte.Worker.csproj
COPY src/Finyte.Core/ src/Finyte.Core/
COPY src/Finyte.Data/ src/Finyte.Data/
COPY src/Finyte.Worker/ src/Finyte.Worker/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked dotnet publish src/Finyte.Worker/Finyte.Worker.csproj -c Release -o /app --no-self-contained --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Finyte.Worker.dll"]
