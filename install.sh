#!/bin/bash

# Script de instalação - LicitacoesCampinasMCP
# Execute: chmod +x install.sh && ./install.sh

echo "=== Instalando LicitacoesCampinasMCP ==="

# Verifica se o .NET 8 está instalado
DOTNET_VERSION=$(dotnet --version 2>/dev/null)
if [[ -z "$DOTNET_VERSION" ]] || [[ ! "$DOTNET_VERSION" == 8.* ]]; then
    echo "ERRO: .NET 8.0 SDK não encontrado!"
    echo "Baixe em: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
fi
echo "✓ .NET SDK encontrado: $DOTNET_VERSION"

# Restaura dependências
echo ""
echo "Restaurando dependências..."
dotnet restore
if [ $? -ne 0 ]; then
    echo "ERRO: Falha ao restaurar dependências"
    exit 1
fi
echo "✓ Dependências restauradas"

# Compila o projeto
echo ""
echo "Compilando projeto..."
dotnet build -c Release
if [ $? -ne 0 ]; then
    echo "ERRO: Falha ao compilar"
    exit 1
fi
echo "✓ Projeto compilado"

# Instala Playwright CLI
echo ""
echo "Instalando Playwright CLI..."
dotnet tool install --global Microsoft.Playwright.CLI 2>/dev/null || true

# Instala navegadores do Playwright
echo ""
echo "Instalando navegador Chromium..."
~/.dotnet/tools/playwright install chromium 2>/dev/null || playwright install chromium 2>/dev/null || {
    echo "Tentando método alternativo..."
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    if [ -f "$SCRIPT_DIR/bin/Release/net8.0/playwright.sh" ]; then
        chmod +x "$SCRIPT_DIR/bin/Release/net8.0/playwright.sh"
        "$SCRIPT_DIR/bin/Release/net8.0/playwright.sh" install chromium
    fi
}
echo "✓ Navegador instalado"

# Exibe caminho do executável
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXE_PATH="$SCRIPT_DIR/bin/Release/net8.0/LicitacoesCampinasMCP"

echo ""
echo "=== Instalação concluída! ==="
echo ""
echo "Executável: $EXE_PATH"

# Exibe configuração para N8N
echo ""
echo "=== Configuração para N8N ==="
cat << EOF

Adicione ao seu arquivo de configuração MCP do N8N:

{
  "mcpServers": {
    "licitacoes-campinas": {
      "command": "$EXE_PATH"
    }
  }
}

Ou para executar via dotnet:

{
  "mcpServers": {
    "licitacoes-campinas": {
      "command": "dotnet",
      "args": ["run", "--project", "$SCRIPT_DIR", "-c", "Release"]
    }
  }
}

EOF

echo "Para testar manualmente, execute:"
echo "dotnet run -c Release"
