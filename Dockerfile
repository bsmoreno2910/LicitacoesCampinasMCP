# ============================================================
# Dockerfile - MCP Server Licitações Campinas
# ============================================================

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia arquivos do projeto
COPY *.csproj ./
RUN dotnet restore

# Copia código fonte e compila
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime com Playwright
FROM mcr.microsoft.com/playwright/dotnet:v1.49.0-noble AS runtime
WORKDIR /app

# Copia aplicação compilada
COPY --from=build /app/publish .

# Variáveis de ambiente
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV ASPNETCORE_URLS=http://+:8080

# Expõe porta para API HTTP (se necessário)
EXPOSE 8080

# Comando de entrada
ENTRYPOINT ["dotnet", "LicitacoesCampinasMCP.dll"]
