using LicitacoesCampinasMCP.Dominio;
using Microsoft.Playwright;
using System.Text.Json;
using System.Net.Http;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Serviço responsável por fazer requisições à API de contratações de Campinas.
/// Usa Playwright para requisições de API e HttpClient para downloads de arquivos grandes.
/// </summary>
public class CampinasApiService : IAsyncDisposable
{
    private readonly BrowserPoolService _browserPool;
    private readonly ApiKeyService _apiKeyService;
    private readonly HttpClient _httpClient;
    
    private const string BASE_URL = "https://contratacoes-api.campinas.sp.gov.br";
    private const int REQUEST_TIMEOUT_MS = 300000; // 5 minutos

    public CampinasApiService(BrowserPoolService browserPool, ApiKeyService apiKeyService)
    {
        _browserPool = browserPool;
        _apiKeyService = apiKeyService;
        
        // HttpClient para downloads de arquivos grandes
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS)
        };
        _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
        _httpClient.DefaultRequestHeaders.Add("Origin", "https://campinas.sp.gov.br");
    }

    /// <summary>
    /// Cria um contexto de API com a chave atual usando uma sessão do pool.
    /// </summary>
    private async Task<IAPIRequestContext> CreateApiContextAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        
        Console.WriteLine("[CampinasApi] Criando contexto de API via Browserless...");
        return await session.Playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL = BASE_URL,
            Timeout = REQUEST_TIMEOUT_MS,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["x-api-key"] = apiKey,
                ["Accept"] = "application/json",
                ["Content-Type"] = "application/json",
                ["Origin"] = "https://campinas.sp.gov.br"
            }
        });
    }

    /// <summary>
    /// Faz uma requisição GET à API de Campinas via Browserless.
    /// Usa uma sessão dedicada do pool para cada requisição.
    /// </summary>
    public async Task<JsonDocument?> GetAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        BrowserSession? session = null;
        IAPIRequestContext? apiContext = null;
        
        try
        {
            // Adquire uma sessão do pool
            Console.WriteLine("[CampinasApi] Adquirindo sessão do BrowserPool...");
            session = await _browserPool.AcquireSessionAsync(cancellationToken);
            
            // Cria contexto de API para esta requisição
            apiContext = await CreateApiContextAsync(session, cancellationToken);
            
            Console.WriteLine($"[CampinasApi] GET {endpoint} (via Browserless)");
            var response = await apiContext.GetAsync(endpoint);
            
            // Se a chave expirou, tenta renovar
            if (!response.Ok && (response.Status == 401 || response.Status == 403))
            {
                Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
                _apiKeyService.InvalidateApiKey();
                
                // Recria o contexto com a nova chave
                await apiContext.DisposeAsync();
                apiContext = await CreateApiContextAsync(session, cancellationToken);
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
        finally
        {
            // Sempre libera os recursos
            if (apiContext != null)
            {
                await apiContext.DisposeAsync();
            }
            if (session != null)
            {
                await _browserPool.ReleaseSessionAsync(session);
            }
        }
    }

    /// <summary>
    /// Faz download de um arquivo da API de Campinas usando HttpClient (para arquivos grandes).
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoAsync(string compraId, string arquivoId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        var url = $"{BASE_URL}/compras/{compraId}/arquivos/{arquivoId}/blob";
        
        Console.WriteLine($"[CampinasApi] DOWNLOAD {url} (via HttpClient)");
        
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        // Se a chave expirou, tenta renovar
        if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
        {
            Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
            _apiKeyService.InvalidateApiKey();
            apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
            
            using var retryRequest = new HttpRequestMessage(HttpMethod.Get, url);
            retryRequest.Headers.Add("x-api-key", apiKey);
            response = await _httpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Erro ao baixar arquivo: {(int)response.StatusCode} - {errorBody}");
        }

        // Lê o arquivo como stream para evitar problemas de memória
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        
        // Tenta extrair nome do arquivo do header content-disposition
        var fileName = $"arquivo_{arquivoId}";
        if (response.Content.Headers.ContentDisposition != null)
        {
            var cd = response.Content.Headers.ContentDisposition;
            if (!string.IsNullOrEmpty(cd.FileNameStar))
            {
                fileName = cd.FileNameStar;
            }
            else if (!string.IsNullOrEmpty(cd.FileName))
            {
                fileName = cd.FileName.Trim('"');
            }
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
    /// Faz download de um arquivo de empenho da API de Campinas usando HttpClient.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoEmpenhoAsync(string compraId, string empenhoId, string arquivoId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        var url = $"{BASE_URL}/compras/{compraId}/empenhos/{empenhoId}/arquivos/{arquivoId}/blob";
        
        Console.WriteLine($"[CampinasApi] DOWNLOAD EMPENHO {url} (via HttpClient)");
        
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
        {
            Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
            _apiKeyService.InvalidateApiKey();
            apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
            
            using var retryRequest = new HttpRequestMessage(HttpMethod.Get, url);
            retryRequest.Headers.Add("x-api-key", apiKey);
            response = await _httpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Erro ao baixar arquivo de empenho: {(int)response.StatusCode} - {errorBody}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        
        var fileName = $"empenho_{empenhoId}_arquivo_{arquivoId}";
        if (response.Content.Headers.ContentDisposition != null)
        {
            var cd = response.Content.Headers.ContentDisposition;
            if (!string.IsNullOrEmpty(cd.FileNameStar))
            {
                fileName = cd.FileNameStar;
            }
            else if (!string.IsNullOrEmpty(cd.FileName))
            {
                fileName = cd.FileName.Trim('"');
            }
        }
        
        if (!fileName.Contains("."))
        {
            fileName += contentType switch
            {
                "application/pdf" => ".pdf",
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "application/zip" => ".zip",
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
    /// Faz download direto do PDF de um empenho da API de Campinas usando HttpClient.
    /// O endpoint /compras/{compraId}/empenhos/{empenhoId} retorna diretamente o arquivo PDF.
    /// </summary>
    public async Task<ArquivoDownload> DownloadEmpenhoAsync(string compraId, string empenhoId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        var url = $"{BASE_URL}/compras/{compraId}/empenhos/{empenhoId}";
        
        Console.WriteLine($"[CampinasApi] DOWNLOAD EMPENHO DIRETO {url} (via HttpClient)");
        
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
        {
            Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
            _apiKeyService.InvalidateApiKey();
            apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
            
            using var retryRequest = new HttpRequestMessage(HttpMethod.Get, url);
            retryRequest.Headers.Add("x-api-key", apiKey);
            response = await _httpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Erro ao baixar empenho: {(int)response.StatusCode} - {errorBody}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";
        
        var fileName = $"empenho_{empenhoId}.pdf";
        if (response.Content.Headers.ContentDisposition != null)
        {
            var cd = response.Content.Headers.ContentDisposition;
            if (!string.IsNullOrEmpty(cd.FileNameStar))
            {
                fileName = cd.FileNameStar;
            }
            else if (!string.IsNullOrEmpty(cd.FileName))
            {
                fileName = cd.FileName.Trim('"');
            }
        }
        
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

    public ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
