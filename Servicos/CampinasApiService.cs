using LicitacoesCampinasMCP.Dominio;
using Microsoft.Playwright;
using System.Text.Json;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Serviço responsável por fazer requisições à API de contratações de Campinas.
/// Gerencia múltiplos contextos de API para suportar requisições simultâneas.
/// </summary>
public class CampinasApiService : IAsyncDisposable
{
    private readonly BrowserPoolService _browserPool;
    private readonly ApiKeyService _apiKeyService;
    private readonly SemaphoreSlim _contextLock = new(1, 1);
    
    private IPlaywright? _playwright;
    private IAPIRequestContext? _sharedApiContext;
    private string? _currentApiKey;
    
    private const string BASE_URL = "https://contratacoes-api.campinas.sp.gov.br";

    public CampinasApiService(BrowserPoolService browserPool, ApiKeyService apiKeyService)
    {
        _browserPool = browserPool;
        _apiKeyService = apiKeyService;
    }

    /// <summary>
    /// Inicializa o Playwright para requisições de API.
    /// </summary>
    private async Task EnsurePlaywrightAsync()
    {
        if (_playwright != null) return;

        await _contextLock.WaitAsync();
        try
        {
            if (_playwright != null) return;
            Console.WriteLine("[CampinasApi] Inicializando Playwright...");
            _playwright = await Playwright.CreateAsync();
        }
        finally
        {
            _contextLock.Release();
        }
    }

    /// <summary>
    /// Obtém ou cria um contexto de API com a chave atual.
    /// </summary>
    private async Task<IAPIRequestContext> GetApiContextAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        
        await _contextLock.WaitAsync(cancellationToken);
        try
        {
            // Se a chave mudou, recria o contexto
            if (_sharedApiContext == null || _currentApiKey != apiKey)
            {
                await EnsurePlaywrightAsync();
                
                if (_sharedApiContext != null)
                {
                    await _sharedApiContext.DisposeAsync();
                }

                Console.WriteLine("[CampinasApi] Criando contexto de API...");
                _sharedApiContext = await _playwright!.APIRequest.NewContextAsync(new APIRequestNewContextOptions
                {
                    BaseURL = BASE_URL,
                    ExtraHTTPHeaders = new Dictionary<string, string>
                    {
                        ["x-api-key"] = apiKey,
                        ["Accept"] = "application/json",
                        ["Content-Type"] = "application/json",
                        ["Origin"] = "https://campinas.sp.gov.br"
                    }
                });
                
                _currentApiKey = apiKey;
            }

            return _sharedApiContext;
        }
        finally
        {
            _contextLock.Release();
        }
    }

    /// <summary>
    /// Faz uma requisição GET à API de Campinas.
    /// </summary>
    public async Task<JsonDocument?> GetAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var apiContext = await GetApiContextAsync(cancellationToken);
        
        Console.WriteLine($"[CampinasApi] GET {endpoint}");
        var response = await apiContext.GetAsync(endpoint);
        
        // Se a chave expirou, tenta renovar
        if (!response.Ok && (response.Status == 401 || response.Status == 403))
        {
            Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
            _apiKeyService.InvalidateApiKey();
            
            // Recria o contexto com a nova chave
            _sharedApiContext = null;
            apiContext = await GetApiContextAsync(cancellationToken);
            response = await apiContext.GetAsync(endpoint);
        }

        if (!response.Ok)
        {
            var errorBody = await response.TextAsync();
            throw new Exception($"Erro na API: {response.Status} - {errorBody}");
        }

        var json = await response.TextAsync();
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Faz download de um arquivo da API de Campinas.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoAsync(string compraId, string arquivoId, CancellationToken cancellationToken = default)
    {
        var apiContext = await GetApiContextAsync(cancellationToken);
        
        var endpoint = $"/compras/{compraId}/arquivos/{arquivoId}/blob";
        Console.WriteLine($"[CampinasApi] DOWNLOAD {endpoint}");
        
        var response = await apiContext.GetAsync(endpoint);
        
        // Se a chave expirou, tenta renovar
        if (!response.Ok && (response.Status == 401 || response.Status == 403))
        {
            Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
            _apiKeyService.InvalidateApiKey();
            
            _sharedApiContext = null;
            apiContext = await GetApiContextAsync(cancellationToken);
            response = await apiContext.GetAsync(endpoint);
        }

        if (!response.Ok)
        {
            var errorBody = await response.TextAsync();
            throw new Exception($"Erro ao baixar arquivo: {response.Status} - {errorBody}");
        }

        var bytes = await response.BodyAsync();
        var contentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "application/octet-stream";
        
        // Tenta extrair nome do arquivo do header content-disposition
        var fileName = $"arquivo_{arquivoId}";
        if (response.Headers.TryGetValue("content-disposition", out var cd))
        {
            var match = System.Text.RegularExpressions.Regex.Match(cd, "filename[^;=\\n]*=(['\"]?)([^'\"\\n]*)");
            if (match.Success) fileName = match.Groups[2].Value;
        }
        
        // Adiciona extensão baseada no content-type se não tiver
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

        return new ArquivoDownload
        {
            Bytes = bytes,
            ContentType = contentType,
            FileName = fileName
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_sharedApiContext != null)
        {
            await _sharedApiContext.DisposeAsync();
        }
        _playwright?.Dispose();
        _contextLock.Dispose();
    }
}
