using LicitacoesCampinasMCP.Dominio;
using Microsoft.Playwright;
using System.Text.Json;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Serviço responsável por fazer requisições à API de contratações de Campinas.
/// Utiliza o BrowserPoolService para todas as operações com Playwright.
/// </summary>
public class CampinasApiService : IAsyncDisposable
{
    private readonly BrowserPoolService _browserPool;
    private readonly ApiKeyService _apiKeyService;
    private readonly SemaphoreSlim _contextLock = new(1, 1);
    
    private BrowserSession? _dedicatedSession;
    private IAPIRequestContext? _sharedApiContext;
    private string? _currentApiKey;
    
    private const string BASE_URL = "https://contratacoes-api.campinas.sp.gov.br";

    public CampinasApiService(BrowserPoolService browserPool, ApiKeyService apiKeyService)
    {
        _browserPool = browserPool;
        _apiKeyService = apiKeyService;
    }

    /// <summary>
    /// Obtém ou cria um contexto de API com a chave atual usando o BrowserPoolService.
    /// </summary>
    private async Task<IAPIRequestContext> GetApiContextAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        
        await _contextLock.WaitAsync(cancellationToken);
        try
        {
            // Se a chave mudou ou não temos sessão, recria o contexto
            if (_sharedApiContext == null || _currentApiKey != apiKey || _dedicatedSession == null || !await _dedicatedSession.IsValidAsync())
            {
                // Libera sessão anterior se existir
                if (_dedicatedSession != null)
                {
                    if (_sharedApiContext != null)
                    {
                        await _sharedApiContext.DisposeAsync();
                        _sharedApiContext = null;
                    }
                    await _browserPool.ReleaseSessionAsync(_dedicatedSession);
                    _dedicatedSession = null;
                }

                // Adquire nova sessão do pool
                Console.WriteLine("[CampinasApi] Adquirindo sessão do BrowserPool...");
                _dedicatedSession = await _browserPool.AcquireSessionAsync(cancellationToken);

                // Cria contexto de API usando o Playwright da sessão
                Console.WriteLine("[CampinasApi] Criando contexto de API via Browserless...");
                _sharedApiContext = await _dedicatedSession.Playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
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

            return _sharedApiContext!;
        }
        finally
        {
            _contextLock.Release();
        }
    }

    /// <summary>
    /// Faz uma requisição GET à API de Campinas via Browserless.
    /// </summary>
    public async Task<JsonDocument?> GetAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var apiContext = await GetApiContextAsync(cancellationToken);
        
        Console.WriteLine($"[CampinasApi] GET {endpoint} (via Browserless)");
        var response = await apiContext.GetAsync(endpoint);
        
        // Se a chave expirou, tenta renovar
        if (!response.Ok && (response.Status == 401 || response.Status == 403))
        {
            Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
            _apiKeyService.InvalidateApiKey();
            
            // Força recriação do contexto
            _currentApiKey = null;
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
    /// Faz download de um arquivo da API de Campinas via Browserless.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoAsync(string compraId, string arquivoId, CancellationToken cancellationToken = default)
    {
        var apiContext = await GetApiContextAsync(cancellationToken);
        
        var endpoint = $"/compras/{compraId}/arquivos/{arquivoId}/blob";
        Console.WriteLine($"[CampinasApi] DOWNLOAD {endpoint} (via Browserless)");
        
        var response = await apiContext.GetAsync(endpoint);
        
        // Se a chave expirou, tenta renovar
        if (!response.Ok && (response.Status == 401 || response.Status == 403))
        {
            Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
            _apiKeyService.InvalidateApiKey();
            
            _currentApiKey = null;
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

    /// <summary>
    /// Faz download de um arquivo de empenho da API de Campinas via Browserless.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoEmpenhoAsync(string compraId, string empenhoId, string arquivoId, CancellationToken cancellationToken = default)
    {
        var apiContext = await GetApiContextAsync(cancellationToken);
        
        var endpoint = $"/compras/{compraId}/empenhos/{empenhoId}/arquivos/{arquivoId}/blob";
        Console.WriteLine($"[CampinasApi] DOWNLOAD EMPENHO {endpoint} (via Browserless)");
        
        var response = await apiContext.GetAsync(endpoint);
        
        // Se a chave expirou, tenta renovar
        if (!response.Ok && (response.Status == 401 || response.Status == 403))
        {
            Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
            _apiKeyService.InvalidateApiKey();
            
            _currentApiKey = null;
            apiContext = await GetApiContextAsync(cancellationToken);
            response = await apiContext.GetAsync(endpoint);
        }

        if (!response.Ok)
        {
            var errorBody = await response.TextAsync();
            throw new Exception($"Erro ao baixar arquivo de empenho: {response.Status} - {errorBody}");
        }

        var bytes = await response.BodyAsync();
        var contentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "application/octet-stream";
        
        // Tenta extrair nome do arquivo do header content-disposition
        var fileName = $"empenho_{empenhoId}_arquivo_{arquivoId}";
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

    /// <summary>
    /// Faz download direto do PDF de um empenho da API de Campinas via Browserless.
    /// O endpoint /compras/{compraId}/empenhos/{empenhoId} retorna diretamente o arquivo PDF.
    /// </summary>
    public async Task<ArquivoDownload> DownloadEmpenhoAsync(string compraId, string empenhoId, CancellationToken cancellationToken = default)
    {
        var apiContext = await GetApiContextAsync(cancellationToken);
        
        var endpoint = $"/compras/{compraId}/empenhos/{empenhoId}";
        Console.WriteLine($"[CampinasApi] DOWNLOAD EMPENHO DIRETO {endpoint} (via Browserless)");
        
        var response = await apiContext.GetAsync(endpoint);
        
        // Se a chave expirou, tenta renovar
        if (!response.Ok && (response.Status == 401 || response.Status == 403))
        {
            Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
            _apiKeyService.InvalidateApiKey();
            
            _currentApiKey = null;
            apiContext = await GetApiContextAsync(cancellationToken);
            response = await apiContext.GetAsync(endpoint);
        }

        if (!response.Ok)
        {
            var errorBody = await response.TextAsync();
            throw new Exception($"Erro ao baixar empenho: {response.Status} - {errorBody}");
        }

        var bytes = await response.BodyAsync();
        var contentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "application/pdf";
        
        // Tenta extrair nome do arquivo do header content-disposition
        var fileName = $"empenho_{empenhoId}.pdf";
        if (response.Headers.TryGetValue("content-disposition", out var cd))
        {
            var match = System.Text.RegularExpressions.Regex.Match(cd, "filename[^;=\\n]*=(['\"]?)([^'\"\\n]*)");
            if (match.Success) fileName = match.Groups[2].Value;
        }
        
        // Adiciona extensão .pdf se não tiver
        if (!fileName.Contains("."))
        {
            fileName += ".pdf";
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
        await _contextLock.WaitAsync();
        try
        {
            if (_sharedApiContext != null)
            {
                await _sharedApiContext.DisposeAsync();
                _sharedApiContext = null;
            }
            
            if (_dedicatedSession != null)
            {
                await _browserPool.ReleaseSessionAsync(_dedicatedSession);
                _dedicatedSession = null;
            }
        }
        finally
        {
            _contextLock.Release();
            _contextLock.Dispose();
        }
    }
}
