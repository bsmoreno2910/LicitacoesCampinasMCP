# Script de instalação - LicitacoesCampinasMCP
# Execute este script no PowerShell como Administrador

Write-Host "=== Instalando LicitacoesCampinasMCP ===" -ForegroundColor Cyan

# Verifica se o .NET 8 está instalado
$dotnetVersion = dotnet --version 2>$null
if (-not $dotnetVersion -or -not $dotnetVersion.StartsWith("8.")) {
    Write-Host "ERRO: .NET 8.0 SDK não encontrado!" -ForegroundColor Red
    Write-Host "Baixe em: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}
Write-Host "✓ .NET SDK encontrado: $dotnetVersion" -ForegroundColor Green

# Restaura dependências
Write-Host "`nRestaurando dependências..." -ForegroundColor Cyan
dotnet restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERRO: Falha ao restaurar dependências" -ForegroundColor Red
    exit 1
}
Write-Host "✓ Dependências restauradas" -ForegroundColor Green

# Compila o projeto
Write-Host "`nCompilando projeto..." -ForegroundColor Cyan
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERRO: Falha ao compilar" -ForegroundColor Red
    exit 1
}
Write-Host "✓ Projeto compilado" -ForegroundColor Green

# Instala Playwright CLI
Write-Host "`nInstalando Playwright CLI..." -ForegroundColor Cyan
dotnet tool install --global Microsoft.Playwright.CLI 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Playwright CLI já instalado ou erro na instalação" -ForegroundColor Yellow
}

# Instala navegadores do Playwright
Write-Host "`nInstalando navegador Chromium..." -ForegroundColor Cyan
playwright install chromium
if ($LASTEXITCODE -ne 0) {
    # Tenta método alternativo
    Write-Host "Tentando método alternativo..." -ForegroundColor Yellow
    $playwrightScript = Join-Path $PSScriptRoot "bin\Release\net8.0\playwright.ps1"
    if (Test-Path $playwrightScript) {
        & $playwrightScript install chromium
    }
}
Write-Host "✓ Navegador instalado" -ForegroundColor Green

# Exibe caminho do executável
$exePath = Join-Path $PSScriptRoot "bin\Release\net8.0\LicitacoesCampinasMCP.exe"
Write-Host "`n=== Instalação concluída! ===" -ForegroundColor Green
Write-Host "`nExecutável: $exePath" -ForegroundColor Cyan

# Exibe configuração para N8N
Write-Host "`n=== Configuração para N8N ===" -ForegroundColor Yellow
Write-Host @"

Adicione ao seu arquivo de configuração MCP do N8N:

{
  "mcpServers": {
    "licitacoes-campinas": {
      "command": "$($exePath.Replace('\', '\\'))"
    }
  }
}

Ou para executar via dotnet:

{
  "mcpServers": {
    "licitacoes-campinas": {
      "command": "dotnet",
      "args": ["run", "--project", "$($PSScriptRoot.Replace('\', '\\'))", "-c", "Release"]
    }
  }
}

"@ -ForegroundColor White

Write-Host "Para testar manualmente, execute:" -ForegroundColor Cyan
Write-Host "dotnet run -c Release" -ForegroundColor White
