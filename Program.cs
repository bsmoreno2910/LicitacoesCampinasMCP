using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP;

class Program
{
    static async Task Main(string[] args)
    {
        var runAsApi = Environment.GetEnvironmentVariable("RUN_AS_API") == "true" || args.Contains("--api");
        if (runAsApi) await RunAsHttpApi(args);
        else await RunAsMcpServer(args);
    }

    static async Task RunAsMcpServer(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "licitacoes-campinas", Version = "1.0.0" })
            .WithStdioServerTransport().WithToolsFromAssembly();
        await builder.Build().RunAsync();
    }

    static async Task RunAsHttpApi(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        var app = builder.Build();
        app.UseSwagger();
        app.UseSwaggerUI();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
        
        // Endpoint para obter apenas a API Key
        app.MapGet("/api/apikey", async (ApiService api) => {
            try 
            { 
                var apiKey = await api.GetApiKeyAsync();
                return Results.Ok(new { 
                    api_key = apiKey, 
                    expires_at = api.GetApiKeyExpiry(),
                    base_url = "https://contratacoes-api.campinas.sp.gov.br"
                }); 
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });
        
        app.MapGet("/api/edital/{id}", async (string id, ApiService api) => {
            try { return Results.Content(await LicitacoesTools.BuscarEdital(api, id), "application/json"); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });
        
        app.MapGet("/api/licitacoes", async (int? pagina, int? itens, ApiService api) => {
            try { return Results.Content(await LicitacoesTools.BuscarListaLicitacoes(api, pagina ?? 1, itens ?? 100), "application/json"); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });
        
        // Endpoint para buscar por número de processo
        app.MapGet("/api/buscar", async (string? processo, string? objeto, ApiService api) => {
            try { return Results.Content(await LicitacoesTools.BuscarPorFiltro(api, processo, objeto), "application/json"); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });
        
        // Endpoint para download de arquivo
        app.MapGet("/api/compra/{compraId}/arquivo/{arquivoId}/download", async (string compraId, string arquivoId, ApiService api) => {
            try 
            { 
                var (bytes, contentType, fileName) = await api.DownloadArquivoAsync(compraId, arquivoId);
                return Results.File(bytes, contentType, fileName);
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });
        
        // Endpoint para obter URL de download de arquivo
        app.MapGet("/api/compra/{compraId}/arquivo/{arquivoId}/url", async (string compraId, string arquivoId, ApiService api) => {
            try 
            { 
                var apiKey = await api.GetApiKeyAsync();
                return Results.Ok(new { 
                    download_url = $"https://contratacoes-api.campinas.sp.gov.br/compras/{compraId}/arquivos/{arquivoId}/blob",
                    api_key = apiKey,
                    headers = new { x_api_key = apiKey }
                });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        Console.WriteLine("API HTTP em http://0.0.0.0:8080");
        await app.RunAsync();
    }
}

public class ApiService : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IAPIRequestContext? _apiContext;
    private string? _apiKey;
    private DateTime _apiKeyExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    
    private const string BASE_URL = "https://contratacoes-api.campinas.sp.gov.br";
    private const string SITE_URL = "https://campinas.sp.gov.br/licitacoes/home";

    public DateTime GetApiKeyExpiry() => _apiKeyExpiry;

    public async Task<string> GetApiKeyAsync()
    {
        if (!string.IsNullOrEmpty(_apiKey) && DateTime.UtcNow < _apiKeyExpiry)
            return _apiKey;

        await _semaphore.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(_apiKey) && DateTime.UtcNow < _apiKeyExpiry)
                return _apiKey;

            Console.WriteLine("Capturando API Key...");

            _playwright ??= await Playwright.CreateAsync();
            _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" }
            });

            var page = await _browser.NewPageAsync();
            string? capturedKey = null;

            page.Request += (_, request) =>
            {
                if (request.Url.Contains("contratacoes-api.campinas.sp.gov.br"))
                {
                    var headers = request.Headers;
                    if (headers.TryGetValue("x-api-key", out var key))
                    {
                        capturedKey = key;
                        Console.WriteLine($"API Key capturada: {key.Substring(0, Math.Min(10, key.Length))}...");
                    }
                }
            };

            try
            {
                await page.GotoAsync(SITE_URL, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
                await page.WaitForTimeoutAsync(5000);

                if (string.IsNullOrEmpty(capturedKey))
                {
                    try
                    {
                        await page.WaitForSelectorAsync("table tbody tr", new PageWaitForSelectorOptions { Timeout = 10000 });
                        var btn = await page.QuerySelectorAsync("table tbody tr button");
                        if (btn != null)
                        {
                            await btn.ClickAsync();
                            await page.WaitForTimeoutAsync(3000);
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"Erro ao clicar: {ex.Message}"); }
                }
            }
            finally
            {
                await page.CloseAsync();
            }

            if (string.IsNullOrEmpty(capturedKey))
                throw new Exception("Não foi possível capturar a API Key. Verifique se o site está acessível.");

            _apiKey = capturedKey;
            _apiKeyExpiry = DateTime.UtcNow.AddMinutes(30);

            if (_apiContext != null)
                await _apiContext.DisposeAsync();

            _apiContext = await _playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
            {
                BaseURL = BASE_URL,
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["x-api-key"] = _apiKey,
                    ["Accept"] = "application/json",
                    ["Content-Type"] = "application/json",
                    ["Origin"] = "https://campinas.sp.gov.br"
                }
            });

            Console.WriteLine("API Key configurada com sucesso!");
            return _apiKey;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<(byte[] bytes, string contentType, string fileName)> DownloadArquivoAsync(string compraId, string arquivoId)
    {
        await GetApiKeyAsync();

        if (_apiContext == null)
            throw new Exception("API Context não inicializado");

        var endpoint = $"/compras/{compraId}/arquivos/{arquivoId}/blob";
        Console.WriteLine($"DOWNLOAD {endpoint}");
        
        var response = await _apiContext.GetAsync(endpoint);
        
        if (!response.Ok)
        {
            if (response.Status == 401 || response.Status == 403)
            {
                Console.WriteLine("API Key expirada, renovando...");
                _apiKey = null;
                _apiKeyExpiry = DateTime.MinValue;
                await GetApiKeyAsync();
                response = await _apiContext.GetAsync(endpoint);
            }
        }

        if (!response.Ok)
        {
            var errorBody = await response.TextAsync();
            throw new Exception($"Erro ao baixar arquivo: {response.Status} - {errorBody}");
        }

        var bytes = await response.BodyAsync();
        var contentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "application/octet-stream";
        
        // Tentar extrair nome do arquivo do header content-disposition
        var fileName = $"arquivo_{arquivoId}";
        if (response.Headers.TryGetValue("content-disposition", out var cd))
        {
            var match = System.Text.RegularExpressions.Regex.Match(cd, @"filename[^;=\n]*=(['"]?)([^'"\n]*)");
            if (match.Success) fileName = match.Groups[2].Value;
        }
        
        // Adicionar extensão baseada no content-type se não tiver
        if (!fileName.Contains("."))
        {
            fileName += contentType switch
            {
                "application/pdf" => ".pdf",
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "application/zip" => ".zip",
                "application/msword" => ".doc",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/vnd.ms-excel" => ".xls",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                _ => ""
            };
        }

        return (bytes, contentType, fileName);
    }

    public async Task<JsonDocument?> GetAsync(string endpoint)
    {
        await GetApiKeyAsync();

        if (_apiContext == null)
            throw new Exception("API Context não inicializado");

        Console.WriteLine($"GET {endpoint}");
        var response = await _apiContext.GetAsync(endpoint);
        
        if (!response.Ok)
        {
            if (response.Status == 401 || response.Status == 403)
            {
                Console.WriteLine("API Key expirada, renovando...");
                _apiKey = null;
                _apiKeyExpiry = DateTime.MinValue;
                await GetApiKeyAsync();
                response = await _apiContext.GetAsync(endpoint);
            }
        }

        if (!response.Ok)
        {
            var errorBody = await response.TextAsync();
            throw new Exception($"Erro na API: {response.Status} - {errorBody}");
        }

        var json = await response.TextAsync();
        return JsonDocument.Parse(json);
    }

    public async ValueTask DisposeAsync()
    {
        if (_apiContext != null) await _apiContext.DisposeAsync();
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}

public class LicitacaoData
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("numero_compra")] public string? NumeroCompra { get; set; }
    [JsonPropertyName("processo")] public string? Processo { get; set; }
    [JsonPropertyName("objeto")] public string? Objeto { get; set; }
    [JsonPropertyName("informacao_complementar")] public string? InformacaoComplementar { get; set; }
    [JsonPropertyName("data_abertura_proposta")] public string? DataAberturaProposta { get; set; }
    [JsonPropertyName("data_encerramento_proposta")] public string? DataEncerramentoProposta { get; set; }
    [JsonPropertyName("link_sistema_origem")] public string? LinkSistemaOrigem { get; set; }
    [JsonPropertyName("sequencial_compra")] public int? SequencialCompra { get; set; }
    [JsonPropertyName("numero_controle_pncp")] public string? NumeroControlePncp { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
    
    // Relacionamentos
    [JsonPropertyName("unidade")] public UnidadeData? Unidade { get; set; }
    [JsonPropertyName("modalidade")] public DominioData? Modalidade { get; set; }
    [JsonPropertyName("amparo_legal")] public DominioData? AmparoLegal { get; set; }
    [JsonPropertyName("instrumento_convocatorio")] public DominioData? InstrumentoConvocatorio { get; set; }
    [JsonPropertyName("modo_disputa")] public DominioData? ModoDisputa { get; set; }
    [JsonPropertyName("situacao_compra")] public DominioData? SituacaoCompra { get; set; }
    
    // Calculados
    [JsonPropertyName("valor_total_estimado")] public decimal ValorTotalEstimado { get; set; }
    [JsonPropertyName("valor_total_homologado")] public decimal ValorTotalHomologado { get; set; }
    
    [JsonPropertyName("arquivos")] public List<ArquivoData>? Arquivos { get; set; }
    [JsonPropertyName("itens")] public List<ItemData>? Itens { get; set; }
    
    [JsonPropertyName("data_extracao")] public string? DataExtracao { get; set; }
    [JsonPropertyName("fonte")] public string Fonte { get; set; } = "campinas.sp.gov.br";
}

public class UnidadeData
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("codigo")] public string? Codigo { get; set; }
    [JsonPropertyName("nome")] public string? Nome { get; set; }
}

public class DominioData
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("titulo")] public string? Titulo { get; set; }
}

public class ArquivoData
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("tipo_documento")] public int TipoDocumento { get; set; }
    [JsonPropertyName("titulo")] public string? Titulo { get; set; }
    [JsonPropertyName("link_pncp")] public string? LinkPncp { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
}

public class ItemData
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("numero_item")] public int NumeroItem { get; set; }
    [JsonPropertyName("codigo_reduzido")] public string? CodigoReduzido { get; set; }
    [JsonPropertyName("descricao")] public string? Descricao { get; set; }
    [JsonPropertyName("quantidade")] public decimal Quantidade { get; set; }
    [JsonPropertyName("unidade_medida")] public string? UnidadeMedida { get; set; }
    [JsonPropertyName("valor_unitario_estimado")] public decimal ValorUnitarioEstimado { get; set; }
    [JsonPropertyName("valor_total")] public decimal ValorTotal { get; set; }
}

public class LicitacaoResumo
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("numero_compra")] public string? NumeroCompra { get; set; }
    [JsonPropertyName("processo")] public string? Processo { get; set; }
    [JsonPropertyName("objeto")] public string? Objeto { get; set; }
    [JsonPropertyName("modalidade")] public string? Modalidade { get; set; }
    [JsonPropertyName("unidade")] public string? Unidade { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
}

[McpServerToolType]
public static class LicitacoesTools
{
    [McpServerTool, Description("Obtém a API Key para acessar a API de contratações de Campinas.")]
    public static async Task<string> ObterApiKey(ApiService api)
    {
        try
        {
            var apiKey = await api.GetApiKeyAsync();
            return JsonSerializer.Serialize(new { 
                api_key = apiKey, 
                expires_at = api.GetApiKeyExpiry(),
                base_url = "https://contratacoes-api.campinas.sp.gov.br"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message });
        }
    }

    [McpServerTool, Description("Busca detalhes completos de um edital pelo ID, incluindo arquivos e itens.")]
    public static async Task<string> BuscarEdital(ApiService api, [Description("ID do edital (ex: 12043)")] string edital_id)
    {
        try
        {
            // Busca dados principais - JSON simples (sem wrapper data)
            var compraDoc = await api.GetAsync($"/compras/{edital_id}?include=unidade,situacao_compra,modalidade,amparo_legal,instrumento_convocatorio,modo_disputa");
            
            if (compraDoc == null)
                return JsonSerializer.Serialize(new { erro = "Edital não encontrado", id = edital_id });

            var root = compraDoc.RootElement;
            
            var lic = new LicitacaoData
            {
                Id = GetInt(root, "id"),
                NumeroCompra = GetString(root, "pncp_numero_compra"),
                Processo = GetString(root, "pncp_numero_processo"),
                Objeto = GetString(root, "pncp_objeto_compra"),
                InformacaoComplementar = GetString(root, "pncp_informacao_complementar"),
                DataAberturaProposta = GetString(root, "pncp_data_abertura_proposta"),
                DataEncerramentoProposta = GetString(root, "pncp_data_encerramento_proposta"),
                LinkSistemaOrigem = GetString(root, "pncp_link_sistema_origem"),
                SequencialCompra = GetInt(root, "pncp_sequencial_compra"),
                NumeroControlePncp = GetString(root, "numero_controle_pncp"),
                Status = GetString(root, "status"),
                UpdatedAt = GetString(root, "updated_at"),
                DataExtracao = DateTime.UtcNow.ToString("o")
            };

            // Unidade
            if (root.TryGetProperty("unidade", out var unidade))
            {
                lic.Unidade = new UnidadeData
                {
                    Id = GetInt(unidade, "id"),
                    Codigo = GetString(unidade, "pncp_codigo_unidade"),
                    Nome = GetString(unidade, "pncp_nome_unidade")
                };
            }

            // Modalidade
            if (root.TryGetProperty("modalidade", out var modalidade))
            {
                lic.Modalidade = new DominioData
                {
                    Id = GetInt(modalidade, "id"),
                    Titulo = GetString(modalidade, "item_titulo")
                };
            }

            // Amparo Legal
            if (root.TryGetProperty("amparo_legal", out var amparoLegal))
            {
                lic.AmparoLegal = new DominioData
                {
                    Id = GetInt(amparoLegal, "id"),
                    Titulo = GetString(amparoLegal, "item_titulo")
                };
            }

            // Instrumento Convocatório
            if (root.TryGetProperty("instrumento_convocatorio", out var instrConv))
            {
                lic.InstrumentoConvocatorio = new DominioData
                {
                    Id = GetInt(instrConv, "id"),
                    Titulo = GetString(instrConv, "item_titulo")
                };
            }

            // Modo de Disputa
            if (root.TryGetProperty("modo_disputa", out var modoDisputa))
            {
                lic.ModoDisputa = new DominioData
                {
                    Id = GetInt(modoDisputa, "id"),
                    Titulo = GetString(modoDisputa, "item_titulo")
                };
            }

            // Situação da Compra
            if (root.TryGetProperty("situacao_compra", out var sitCompra))
            {
                lic.SituacaoCompra = new DominioData
                {
                    Id = GetInt(sitCompra, "id"),
                    Titulo = GetString(sitCompra, "item_titulo")
                };
            }

            // Busca itens - JSON com wrapper data
            try
            {
                var itensDoc = await api.GetAsync($"/compras/{edital_id}/itens/?page[number]=1&page[size]=1000&sort=pncp_numero_item");
                if (itensDoc != null && itensDoc.RootElement.TryGetProperty("data", out var itensData))
                {
                    lic.Itens = new List<ItemData>();
                    decimal totalEstimado = 0;
                    
                    foreach (var item in itensData.EnumerateArray())
                    {
                        var itemData = new ItemData
                        {
                            Id = GetInt(item, "id"),
                            NumeroItem = GetInt(item, "pncp_numero_item"),
                            CodigoReduzido = GetString(item, "codigo_reduzido"),
                            Descricao = GetString(item, "pncp_descricao"),
                            Quantidade = GetDecimal(item, "pncp_quantidade"),
                            UnidadeMedida = GetString(item, "pncp_unidade_medida"),
                            ValorUnitarioEstimado = GetDecimal(item, "pncp_valor_unitario_estimado"),
                            ValorTotal = GetDecimal(item, "pncp_valor_total")
                        };
                        lic.Itens.Add(itemData);
                        totalEstimado += itemData.ValorTotal;
                    }
                    
                    lic.ValorTotalEstimado = totalEstimado;
                }
            }
            catch (Exception ex) { Console.WriteLine($"Erro ao buscar itens: {ex.Message}"); }

            // Busca arquivos - JSON com wrapper data
            try
            {
                var arqsDoc = await api.GetAsync($"/compras/{edital_id}/arquivos?filter[acao][neq]=remover&sort=pncp_titulo_documento&page[number]=1&page[size]=1000");
                if (arqsDoc != null && arqsDoc.RootElement.TryGetProperty("data", out var arqsData))
                {
                    lic.Arquivos = new List<ArquivoData>();
                    foreach (var arq in arqsData.EnumerateArray())
                    {
                        lic.Arquivos.Add(new ArquivoData
                        {
                            Id = GetInt(arq, "id"),
                            TipoDocumento = GetInt(arq, "pncp_tipo_documento"),
                            Titulo = GetString(arq, "pncp_titulo_documento"),
                            LinkPncp = GetString(arq, "pncp_link"),
                            CreatedAt = GetString(arq, "created_at")
                        });
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"Erro ao buscar arquivos: {ex.Message}"); }

            return JsonSerializer.Serialize(lic, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message, id = edital_id });
        }
    }

    [McpServerTool, Description("Lista licitações com paginação.")]
    public static async Task<string> BuscarListaLicitacoes(ApiService api, [Description("Página (padrão: 1)")] int pagina = 1, [Description("Itens por página (padrão: 100)")] int itens_por_pagina = 100)
    {
        try
        {
            var doc = await api.GetAsync($"/compras?page[number]={pagina}&page[size]={itens_por_pagina}&sort=-id&include=modalidade,unidade,situacao_compra");
            
            if (doc == null)
                return JsonSerializer.Serialize(new { erro = "Sem resposta da API" });

            var lics = new List<LicitacaoResumo>();
            var root = doc.RootElement;
            
            // A lista de compras também usa wrapper "data"
            if (root.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = GetInt(item, "id");
                    
                    var lic = new LicitacaoResumo
                    {
                        Id = id,
                        NumeroCompra = GetString(item, "pncp_numero_compra"),
                        Processo = GetString(item, "pncp_numero_processo"),
                        Objeto = GetString(item, "pncp_objeto_compra"),
                        Status = GetString(item, "status"),
                        Url = $"https://campinas.sp.gov.br/licitacoes/edital/{id}"
                    };

                    // Relacionamentos inline
                    if (item.TryGetProperty("modalidade", out var mod))
                        lic.Modalidade = GetString(mod, "item_titulo");
                    
                    if (item.TryGetProperty("unidade", out var unid))
                        lic.Unidade = GetString(unid, "pncp_nome_unidade");
                    
                    if (item.TryGetProperty("situacao_compra", out var sit))
                        lic.Status = GetString(sit, "item_titulo");

                    lics.Add(lic);
                }
            }

            int total = 0;
            if (root.TryGetProperty("meta", out var meta))
            {
                total = GetInt(meta, "total_count");
            }

            return JsonSerializer.Serialize(new { pagina, itens_por_pagina, total, licitacoes = lics }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message });
        }
    }

    [McpServerTool, Description("Busca licitações por número de processo ou termo no objeto.")]
    public static async Task<string> BuscarPorFiltro(ApiService api, [Description("Número do processo (ex: PMC.2025.00124491-59)")] string? processo = null, [Description("Termo para buscar no objeto")] string? objeto = null)
    {
        try
        {
            // Monta os filtros
            var filtros = new List<string>();
            
            if (!string.IsNullOrWhiteSpace(processo))
            {
                // Busca exata ou parcial por processo
                filtros.Add($"filter[pncp_numero_processo][cont]={Uri.EscapeDataString(processo)}");
            }
            
            if (!string.IsNullOrWhiteSpace(objeto))
            {
                // Busca parcial no objeto
                filtros.Add($"filter[pncp_objeto_compra][cont]={Uri.EscapeDataString(objeto)}");
            }
            
            if (filtros.Count == 0)
            {
                return JsonSerializer.Serialize(new { erro = "Informe pelo menos um filtro: processo ou objeto" });
            }
            
            var queryString = string.Join("&", filtros);
            var doc = await api.GetAsync($"/compras?{queryString}&page[number]=1&page[size]=100&sort=-id&include=modalidade,unidade,situacao_compra");
            
            if (doc == null)
                return JsonSerializer.Serialize(new { erro = "Sem resposta da API" });

            var lics = new List<LicitacaoResumo>();
            var root = doc.RootElement;
            
            if (root.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = GetInt(item, "id");
                    
                    var lic = new LicitacaoResumo
                    {
                        Id = id,
                        NumeroCompra = GetString(item, "pncp_numero_compra"),
                        Processo = GetString(item, "pncp_numero_processo"),
                        Objeto = GetString(item, "pncp_objeto_compra"),
                        Status = GetString(item, "status"),
                        Url = $"https://campinas.sp.gov.br/licitacoes/edital/{id}"
                    };

                    if (item.TryGetProperty("modalidade", out var mod))
                        lic.Modalidade = GetString(mod, "item_titulo");
                    
                    if (item.TryGetProperty("unidade", out var unid))
                        lic.Unidade = GetString(unid, "pncp_nome_unidade");
                    
                    if (item.TryGetProperty("situacao_compra", out var sit))
                        lic.Status = GetString(sit, "item_titulo");

                    lics.Add(lic);
                }
            }

            int total = 0;
            if (root.TryGetProperty("meta", out var meta))
            {
                total = GetInt(meta, "total_count");
            }

            return JsonSerializer.Serialize(new { 
                filtros = new { processo, objeto },
                total, 
                licitacoes = lics 
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message });
        }
    }

    private static string? GetString(JsonElement el, string prop)
    {
        try { return el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null; }
        catch { return null; }
    }

    private static int GetInt(JsonElement el, string prop)
    {
        try 
        { 
            if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetInt32();
                if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i)) return i;
            }
            return 0;
        }
        catch { return 0; }
    }

    private static decimal GetDecimal(JsonElement el, string prop)
    {
        try 
        { 
            if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
                if (v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString()?.Replace(".", "").Replace(",", ".");
                    return decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
                }
            }
            return 0;
        }
        catch { return 0; }
    }
}
