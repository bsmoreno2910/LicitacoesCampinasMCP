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
        
        app.MapGet("/api/edital/{id}", async (string id, ApiService api) => {
            try { return Results.Content(await LicitacoesTools.BuscarEdital(api, id), "application/json"); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });
        
        app.MapGet("/api/licitacoes", async (int? pagina, int? itens, ApiService api) => {
            try { return Results.Content(await LicitacoesTools.BuscarListaLicitacoes(api, pagina ?? 1, itens ?? 100), "application/json"); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        Console.WriteLine("API HTTP em http://0.0.0.0:8080");
        await app.RunAsync();
    }
}

/// <summary>
/// Serviço que gerencia a API Key e faz requisições à API de Campinas
/// </summary>
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

    /// <summary>
    /// Obtém a API Key interceptando requisições do site
    /// </summary>
    public async Task<string> GetApiKeyAsync()
    {
        // Se a key ainda é válida (cache de 30 minutos), retorna
        if (!string.IsNullOrEmpty(_apiKey) && DateTime.UtcNow < _apiKeyExpiry)
            return _apiKey;

        await _semaphore.WaitAsync();
        try
        {
            // Double-check após obter o lock
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

            // Intercepta requisições para capturar a API Key
            page.Request += (_, request) =>
            {
                if (request.Url.Contains("contratacoes-api.campinas.sp.gov.br"))
                {
                    var headers = request.Headers;
                    if (headers.TryGetValue("x-api-key", out var key))
                    {
                        capturedKey = key;
                        Console.WriteLine($"API Key capturada: {key.Substring(0, 10)}...");
                    }
                }
            };

            try
            {
                // Navega para o site para disparar as requisições
                await page.GotoAsync(SITE_URL, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
                
                // Aguarda um pouco para garantir que as requisições foram feitas
                await page.WaitForTimeoutAsync(3000);

                if (string.IsNullOrEmpty(capturedKey))
                {
                    // Tenta clicar em algum item para forçar mais requisições
                    try
                    {
                        var btn = await page.QuerySelectorAsync("button:has(mat-icon)");
                        if (btn != null) await btn.ClickAsync();
                        await page.WaitForTimeoutAsync(2000);
                    }
                    catch { }
                }
            }
            finally
            {
                await page.CloseAsync();
            }

            if (string.IsNullOrEmpty(capturedKey))
                throw new Exception("Não foi possível capturar a API Key");

            _apiKey = capturedKey;
            _apiKeyExpiry = DateTime.UtcNow.AddMinutes(30); // Cache por 30 minutos

            // Cria o contexto de API com a key capturada
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

            return _apiKey;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Faz uma requisição GET à API
    /// </summary>
    public async Task<JsonDocument?> GetAsync(string endpoint)
    {
        await GetApiKeyAsync(); // Garante que temos a API Key

        if (_apiContext == null)
            throw new Exception("API Context não inicializado");

        var response = await _apiContext.GetAsync(endpoint);
        
        if (!response.Ok)
        {
            // Se der erro 401/403, invalida a key e tenta novamente
            if (response.Status == 401 || response.Status == 403)
            {
                _apiKey = null;
                _apiKeyExpiry = DateTime.MinValue;
                await GetApiKeyAsync();
                response = await _apiContext.GetAsync(endpoint);
            }
        }

        if (!response.Ok)
            throw new Exception($"Erro na API: {response.Status} - {response.StatusText}");

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

// Classes de modelo
public class LicitacaoData
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("titulo")] public string? Titulo { get; set; }
    [JsonPropertyName("tipo")] public string? Tipo { get; set; }
    [JsonPropertyName("processo")] public string? Processo { get; set; }
    [JsonPropertyName("secretaria")] public string? Secretaria { get; set; }
    [JsonPropertyName("modalidade")] public string? Modalidade { get; set; }
    [JsonPropertyName("instrumento_convocatorio")] public string? InstrumentoConvocatorio { get; set; }
    [JsonPropertyName("modo_disputa")] public string? ModoDisputa { get; set; }
    [JsonPropertyName("amparo_legal")] public string? AmparoLegal { get; set; }
    [JsonPropertyName("link_contratacao")] public string? LinkContratacao { get; set; }
    [JsonPropertyName("objeto")] public string? Objeto { get; set; }
    [JsonPropertyName("informacao_complementar")] public string? InformacaoComplementar { get; set; }
    [JsonPropertyName("id_pncp")] public string? IdPncp { get; set; }
    [JsonPropertyName("numero_controle_pncp")] public string? NumeroControlePncp { get; set; }
    [JsonPropertyName("situacao_compra")] public string? SituacaoCompra { get; set; }
    [JsonPropertyName("status_compra")] public string? StatusCompra { get; set; }
    [JsonPropertyName("data_inicio_propostas")] public string? DataInicioPropostas { get; set; }
    [JsonPropertyName("data_fim_propostas")] public string? DataFimPropostas { get; set; }
    [JsonPropertyName("ultima_alteracao")] public string? UltimaAlteracao { get; set; }
    [JsonPropertyName("valor_estimado")] public decimal ValorEstimado { get; set; }
    [JsonPropertyName("valor_homologado")] public decimal ValorHomologado { get; set; }
    [JsonPropertyName("data_extracao")] public string? DataExtracao { get; set; }
    [JsonPropertyName("fonte")] public string Fonte { get; set; } = "campinas.sp.gov.br";
    [JsonPropertyName("arquivos")] public List<ArquivoData>? Arquivos { get; set; }
    [JsonPropertyName("itens")] public List<ItemData>? Itens { get; set; }
}

public class ArquivoData
{
    [JsonPropertyName("nome")] public string? Nome { get; set; }
    [JsonPropertyName("tipo")] public string? Tipo { get; set; }
    [JsonPropertyName("data")] public string? Data { get; set; }
    [JsonPropertyName("url_download")] public string? UrlDownload { get; set; }
}

public class ItemData
{
    [JsonPropertyName("numero")] public string? Numero { get; set; }
    [JsonPropertyName("codigo")] public string? Codigo { get; set; }
    [JsonPropertyName("descricao")] public string? Descricao { get; set; }
    [JsonPropertyName("quantidade")] public string? Quantidade { get; set; }
    [JsonPropertyName("valor_unitario")] public decimal ValorUnitario { get; set; }
    [JsonPropertyName("valor_total")] public decimal ValorTotal { get; set; }
    [JsonPropertyName("situacao")] public string? Situacao { get; set; }
}

public class LicitacaoResumo
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("modalidade")] public string? Modalidade { get; set; }
    [JsonPropertyName("numero")] public string? Numero { get; set; }
    [JsonPropertyName("processo")] public string? Processo { get; set; }
    [JsonPropertyName("unidade")] public string? Unidade { get; set; }
    [JsonPropertyName("objeto")] public string? Objeto { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
}

[McpServerToolType]
public static class LicitacoesTools
{
    [McpServerTool, Description("Busca detalhes completos de um edital pelo ID, incluindo arquivos e itens.")]
    public static async Task<string> BuscarEdital(ApiService api, [Description("ID do edital (ex: 12043)")] string edital_id)
    {
        try
        {
            var lic = new LicitacaoData { Id = edital_id, DataExtracao = DateTime.UtcNow.ToString("o") };

            // Busca dados principais
            var compraDoc = await api.GetAsync($"/compras/{edital_id}?include=unidade,situacao_compra,modalidade,amparo_legal,instrumento_convocatorio,modo_disputa,situacao_pncp");
            if (compraDoc != null)
            {
                var data = compraDoc.RootElement.GetProperty("data");
                var attrs = data.GetProperty("attributes");

                lic.Processo = GetString(attrs, "processo");
                lic.Objeto = GetString(attrs, "objeto");
                lic.InformacaoComplementar = GetString(attrs, "informacao_complementar");
                lic.DataInicioPropostas = GetString(attrs, "data_inicio_propostas");
                lic.DataFimPropostas = GetString(attrs, "data_fim_propostas");
                lic.UltimaAlteracao = GetString(attrs, "updated_at");
                lic.ValorEstimado = GetDecimal(attrs, "valor_total_estimado");
                lic.ValorHomologado = GetDecimal(attrs, "valor_total_homologado");
                lic.IdPncp = GetString(attrs, "pncp_id_compra");
                lic.NumeroControlePncp = GetString(attrs, "pncp_numero_controle");
                lic.LinkContratacao = GetString(attrs, "link_contratacao");
                lic.Titulo = GetString(attrs, "numero_compra");

                // Dados incluídos (relacionamentos)
                if (compraDoc.RootElement.TryGetProperty("included", out var included))
                {
                    foreach (var inc in included.EnumerateArray())
                    {
                        var type = GetString(inc, "type");
                        var incAttrs = inc.GetProperty("attributes");
                        
                        switch (type)
                        {
                            case "modalidades": lic.Modalidade = GetString(incAttrs, "nome"); break;
                            case "unidades": lic.Secretaria = GetString(incAttrs, "nome"); break;
                            case "situacoes_compra": lic.SituacaoCompra = GetString(incAttrs, "nome"); break;
                            case "amparos_legais": lic.AmparoLegal = GetString(incAttrs, "nome"); break;
                            case "instrumentos_convocatorios": 
                                lic.InstrumentoConvocatorio = GetString(incAttrs, "nome"); 
                                lic.Tipo = GetString(incAttrs, "nome");
                                break;
                            case "modos_disputa": lic.ModoDisputa = GetString(incAttrs, "nome"); break;
                        }
                    }
                }
            }

            // Busca itens
            var itensDoc = await api.GetAsync($"/compras/{edital_id}/itens/?page[number]=1&page[size]=1000&sort=pncp_numero_item");
            if (itensDoc != null && itensDoc.RootElement.TryGetProperty("data", out var itensData))
            {
                lic.Itens = new List<ItemData>();
                foreach (var item in itensData.EnumerateArray())
                {
                    var attrs = item.GetProperty("attributes");
                    lic.Itens.Add(new ItemData
                    {
                        Numero = GetString(attrs, "pncp_numero_item"),
                        Codigo = GetString(attrs, "codigo_item"),
                        Descricao = GetString(attrs, "descricao"),
                        Quantidade = GetString(attrs, "quantidade"),
                        ValorUnitario = GetDecimal(attrs, "valor_unitario_estimado"),
                        ValorTotal = GetDecimal(attrs, "valor_total_estimado"),
                        Situacao = GetString(attrs, "situacao")
                    });
                }
            }

            // Busca arquivos
            var arqsDoc = await api.GetAsync($"/compras/{edital_id}/arquivos?filter[acao][neq]=remover&sort=pncp_titulo_documento&page[number]=1&page[size]=1000");
            if (arqsDoc != null && arqsDoc.RootElement.TryGetProperty("data", out var arqsData))
            {
                lic.Arquivos = new List<ArquivoData>();
                foreach (var arq in arqsData.EnumerateArray())
                {
                    var attrs = arq.GetProperty("attributes");
                    lic.Arquivos.Add(new ArquivoData
                    {
                        Nome = GetString(attrs, "pncp_titulo_documento") ?? GetString(attrs, "nome_arquivo"),
                        Tipo = GetString(attrs, "tipo_documento"),
                        Data = GetString(attrs, "created_at"),
                        UrlDownload = GetString(attrs, "url_arquivo")
                    });
                }
            }

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
            
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                // Monta dicionário de includes para lookup
                var includes = new Dictionary<string, JsonElement>();
                if (doc.RootElement.TryGetProperty("included", out var included))
                {
                    foreach (var inc in included.EnumerateArray())
                    {
                        var type = GetString(inc, "type");
                        var id = GetString(inc, "id");
                        if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(id))
                            includes[$"{type}:{id}"] = inc;
                    }
                }

                foreach (var item in data.EnumerateArray())
                {
                    var id = GetString(item, "id");
                    var attrs = item.GetProperty("attributes");
                    
                    var lic = new LicitacaoResumo
                    {
                        Id = id,
                        Numero = GetString(attrs, "numero_compra"),
                        Processo = GetString(attrs, "processo"),
                        Objeto = GetString(attrs, "objeto"),
                        Url = $"https://campinas.sp.gov.br/licitacoes/edital/{id}"
                    };

                    // Busca relacionamentos
                    if (item.TryGetProperty("relationships", out var rels))
                    {
                        if (rels.TryGetProperty("modalidade", out var modRel) && 
                            modRel.TryGetProperty("data", out var modData) &&
                            modData.ValueKind != JsonValueKind.Null)
                        {
                            var modId = GetString(modData, "id");
                            if (includes.TryGetValue($"modalidades:{modId}", out var mod))
                                lic.Modalidade = GetString(mod.GetProperty("attributes"), "nome");
                        }

                        if (rels.TryGetProperty("unidade", out var unidRel) && 
                            unidRel.TryGetProperty("data", out var unidData) &&
                            unidData.ValueKind != JsonValueKind.Null)
                        {
                            var unidId = GetString(unidData, "id");
                            if (includes.TryGetValue($"unidades:{unidId}", out var unid))
                                lic.Unidade = GetString(unid.GetProperty("attributes"), "nome");
                        }

                        if (rels.TryGetProperty("situacao_compra", out var sitRel) && 
                            sitRel.TryGetProperty("data", out var sitData) &&
                            sitData.ValueKind != JsonValueKind.Null)
                        {
                            var sitId = GetString(sitData, "id");
                            if (includes.TryGetValue($"situacoes_compra:{sitId}", out var sit))
                                lic.Status = GetString(sit.GetProperty("attributes"), "nome");
                        }
                    }

                    lics.Add(lic);
                }
            }

            // Pega metadados de paginação
            int total = 0;
            if (doc.RootElement.TryGetProperty("meta", out var meta) && 
                meta.TryGetProperty("page", out var pageMeta))
            {
                total = pageMeta.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
            }

            return JsonSerializer.Serialize(new { pagina, itens_por_pagina, total, licitacoes = lics }, new JsonSerializerOptions { WriteIndented = true });
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
