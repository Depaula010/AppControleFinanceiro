# syntax=docker/dockerfile:1
# Contexto de build: ./dotnet  (docker compose: build: ./dotnet)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore em camada separada para aproveitar cache quando só o código muda.
COPY global.json Directory.Build.props Directory.Packages.props MeuSecretario.slnx ./
COPY src/MeuSecretario.Domain/*.csproj src/MeuSecretario.Domain/
COPY src/MeuSecretario.Application/*.csproj src/MeuSecretario.Application/
COPY src/MeuSecretario.Infrastructure/*.csproj src/MeuSecretario.Infrastructure/
COPY src/MeuSecretario.Api/*.csproj src/MeuSecretario.Api/
RUN dotnet restore src/MeuSecretario.Api/MeuSecretario.Api.csproj

COPY src/ src/
COPY migrations/ migrations/
RUN dotnet publish src/MeuSecretario.Api/MeuSecretario.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Debian (não Alpine): ICU e tzdata completos para America/Sao_Paulo e pt-BR.
# Gráficos (ScottPlot/SkiaSharp, relatório mensal e intents de gráfico): a imagem aspnet não traz fontes nem as bibliotecas
# nativas que o libSkiaSharp carrega (libfontconfig1, libfreetype6). Sem elas o PNG falha (texto em branco ou exceção de
# biblioteca nativa, e o renderizador devolve null). fonts-dejavu-core cobre os acentos do português.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl tzdata fontconfig libfontconfig1 libfreetype6 fonts-dejavu-core \
    && fc-cache -f \
    && rm -rf /var/lib/apt/lists/*

ENV TZ=America/Sao_Paulo \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8000 \
    LOG_DIR=/app/logs

WORKDIR /app
COPY --from=build /app .

# /app/logs é o volume do compose (./logs:/app/logs); precisa ser gravável pelo usuário do app.
RUN mkdir -p /app/logs && chown -R app:app /app/logs
USER app

EXPOSE 8000
LABEL app=meu-secretario

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8000/api/health || exit 1

ENTRYPOINT ["dotnet", "MeuSecretario.Api.dll"]
