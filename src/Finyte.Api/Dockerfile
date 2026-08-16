FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Finyte.Core/Finyte.Core.csproj src/Finyte.Core/
COPY src/Finyte.Data/Finyte.Data.csproj src/Finyte.Data/
COPY src/Finyte.Api/Finyte.Api.csproj src/Finyte.Api/
RUN --mount=type=cache,target=/root/.nuget/packages dotnet restore src/Finyte.Api/Finyte.Api.csproj
COPY src/Finyte.Core/ src/Finyte.Core/
COPY src/Finyte.Data/ src/Finyte.Data/
COPY src/Finyte.Api/ src/Finyte.Api/
RUN --mount=type=cache,target=/root/.nuget/packages dotnet publish src/Finyte.Api/Finyte.Api.csproj -c Release -o /app --no-self-contained --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Finyte.Api.dll"]
