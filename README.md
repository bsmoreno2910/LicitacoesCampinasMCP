# MCP Server - Licitações Campinas

Servidor MCP (Model Context Protocol) em C# para extração de dados de licitações do portal de Campinas.

## Requisitos

- .NET 8.0 SDK
- Windows, Linux ou macOS

## Instalação

### 1. Clone ou baixe o projeto

```bash
# Crie uma pasta para o projeto
mkdir LicitacoesCampinasMCP
cd LicitacoesCampinasMCP
```

### 2. Restaure as dependências e compile

```bash
dotnet restore
dotnet build
```

### 3. Instale os navegadores do Playwright

```bash
# Windows (PowerShell)
pwsh bin/Debug/net8.0/playwright.ps1 install chromium

# Linux/macOS
./bin/Debug/net8.0/playwright.sh install chromium
```

Ou via dotnet:
```bash
dotnet tool install --global Microsoft.Playwright.CLI
playwright install chromium
```

## Configuração no N8N

### 1. Configure o MCP Server no N8N

Adicione o servidor MCP nas configurações do N8N. No arquivo de configuração do N8N ou via variáveis de ambiente:

```json
{
  "mcpServers": {
    "licitacoes-campinas": {
      "command": "dotnet",
      "args": ["run", "--project", "CAMINHO/PARA/LicitacoesCampinasMCP"]
    }
  }
}
```

Ou se você compilou para um executável:

```json
{
  "mcpServers": {
    "licitacoes-campinas": {
      "command": "CAMINHO/PARA/LicitacoesCampinasMCP.exe"
    }
  }
}
```

### 2. No N8N, use o nó MCP

Após configurar, você terá acesso às seguintes ferramentas:

## Ferramentas Disponíveis

### 1. `buscar_edital`

Busca os detalhes completos de um edital/licitação pelo ID.

**Parâmetros:**
- `edital_id` (string, obrigatório): ID do edital (ex: "12043")

**Exemplo de uso no N8N:**
```json
{
  "edital_id": "12043"
}
```

**Retorno:**
```json
{
  "id": "12043",
  "titulo": "Pregão - Eletrônico 363/2025",
  "tipo": "Edital",
  "processo": "PMC.2025.00124491-59",
  "secretaria": "SECRETARIA MUNICIPAL DE SAÚDE",
  "modalidade": "Pregão - Eletrônico",
  "objeto": "Fornecimento contínuo de refeições...",
  "valor_estimado": 7460701.20,
  "valor_homologado": 0,
  "arquivos": [...],
  "itens": [...]
}
```

### 2. `buscar_lista_licitacoes`

Busca a lista de licitações da página principal.

**Parâmetros:**
- `pagina` (integer, opcional): Número da página (padrão: 1)
- `itens_por_pagina` (integer, opcional): Itens por página - 10, 25, 50, 100 (padrão: 100)

**Exemplo de uso no N8N:**
```json
{
  "pagina": 1,
  "itens_por_pagina": 100
}
```

**Retorno:**
```json
[
  {
    "id": "12043",
    "modalidade": "Pregão - Eletrônico",
    "numero": "363/2025",
    "processo": "PMC.2025.00124491-59",
    "unidade": "SECRETARIA MUNICIPAL DE SAÚDE",
    "objeto": "Fornecimento contínuo de refeições...",
    "status": "Em Andamento",
    "url": "https://campinas.sp.gov.br/licitacoes/edital/12043"
  },
  ...
]
```

### 3. `baixar_arquivo_edital`

Baixa um arquivo específico de um edital.

**Parâmetros:**
- `edital_id` (string, obrigatório): ID do edital
- `nome_arquivo` (string, obrigatório): Nome do arquivo a ser baixado
- `pasta_destino` (string, opcional): Pasta onde salvar (padrão: "./downloads")

**Exemplo de uso no N8N:**
```json
{
  "edital_id": "12043",
  "nome_arquivo": "98629105903632025000",
  "pasta_destino": "C:/Downloads/Licitacoes"
}
```

## Exemplo de Workflow no N8N

### Workflow para extrair todas as licitações:

1. **Manual Trigger** → Inicia o workflow

2. **MCP Tool: buscar_lista_licitacoes** → Busca lista de licitações
   ```json
   { "pagina": 1, "itens_por_pagina": 100 }
   ```

3. **Loop Over Items** → Processa cada licitação

4. **MCP Tool: buscar_edital** → Busca detalhes de cada edital
   ```json
   { "edital_id": "{{ $json.id }}" }
   ```

5. **Supabase** → Salva no banco de dados

6. **Wait** → Aguarda 2 segundos entre requisições

## Executando Manualmente (para teste)

```bash
cd LicitacoesCampinasMCP
dotnet run
```

O servidor iniciará e aguardará conexões via stdio.

## Troubleshooting

### Erro de SSL
O Playwright usa seu próprio navegador Chromium que não tem problemas com SSL.

### Erro "Playwright not found"
Execute: `playwright install chromium`

### Timeout na página
Aumente o timeout no código ou verifique sua conexão com a internet.

### Página não carrega dados
O site usa Angular e pode demorar para renderizar. O código já aguarda o carregamento completo.

## Estrutura do Projeto

```
LicitacoesCampinasMCP/
├── LicitacoesCampinasMCP.csproj  # Arquivo de projeto
├── Program.cs                     # Código principal
├── README.md                      # Este arquivo
└── bin/                          # Binários compilados
```

## Licença

MIT
