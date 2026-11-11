# syntax=docker/dockerfile:1

# =====================================================================
# Etapa 1 — build: compila Api y Worker con el SDK completo
# =====================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1a. Solo lo necesario para restaurar paquetes.
#     Si no cambian los .csproj, Docker reutiliza esta capa y no vuelve a descargar NuGet.
COPY global.json Directory.Build.props .editorconfig ./
COPY src/QueryDoctor.Domain/QueryDoctor.Domain.csproj                 src/QueryDoctor.Domain/
COPY src/QueryDoctor.Application/QueryDoctor.Application.csproj       src/QueryDoctor.Application/
COPY src/QueryDoctor.Infrastructure/QueryDoctor.Infrastructure.csproj src/QueryDoctor.Infrastructure/
COPY src/QueryDoctor.Api/QueryDoctor.Api.csproj                       src/QueryDoctor.Api/
COPY src/QueryDoctor.Worker/QueryDoctor.Worker.csproj                 src/QueryDoctor.Worker/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/QueryDoctor.Api/QueryDoctor.Api.csproj \
 && dotnet restore src/QueryDoctor.Worker/QueryDoctor.Worker.csproj

# 1b. El código fuente y la publicación (Release, sin volver a restaurar)
COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/QueryDoctor.Api/QueryDoctor.Api.csproj       -c Release --no-restore -o /out/api \
 && dotnet publish src/QueryDoctor.Worker/QueryDoctor.Worker.csproj -c Release --no-restore -o /out/worker
# =====================================================================
# Etapa 2a — api: solo el resultado compilado, sobre la imagen ASP.NET
# =====================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "QueryDoctor.Api.dll"]

# =====================================================================
# Etapa 2b — worker: solo el resultado compilado, sobre el runtime mínimo
# =====================================================================
FROM mcr.microsoft.com/dotnet/runtime:11.0 AS worker
WORKDIR /app
COPY --from=build /out/worker ./
USER $APP_UID
ENTRYPOINT ["dotnet", "QueryDoctor.Worker.dll"]