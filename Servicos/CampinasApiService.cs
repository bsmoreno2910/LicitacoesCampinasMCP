using LicitacoesCampinasMCP.Dominio;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Net.Http.Headers;
using MagicBytesValidator.Services;
using MagicBytesValidator.Services.Streams;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Serviço responsável por fazer requisições à API de contratações de Campinas.
/// Usa Playwright/Browserless para requisições de API JSON.
/// Usa HttpClient nativo para downloads de arquivos (streaming).
/// Utiliza MagicBytesValidator para identificar o tipo correto de arquivo pelos magic bytes.
/// </summary>
public class CampinasApiService : IAsyncDisposable
{
    private readonly BrowserPoolService _browserPool;
    private readonly ApiKeyService _apiKeyService;
    private readonly StreamFileTypeProvider _fileTypeProvider;
    private readonly Mapping _mapping;
    private readonly HttpClient _httpClient;
    
    // Extensões de arquivo conhecidas
    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp",
        "txt", "rtf", "csv", "xml", "json", "html", "htm",
        "zip", "rar", "7z", "tar", "gz", "bz2",
        "jpg", "jpeg", "png", "gif", "bmp", "tiff", "tif", "svg", "webp",
        "mp3", "mp4", "avi", "mov", "wmv", "flv", "mkv", "wav",
        "exe", "msi", "dll", "bat", "sh"
    };
    
    private const string BASE_URL = "https://contratacoes-api.campinas.sp.gov.br";
    private const int REQUEST_TIMEOUT_MS = 300000; // 5 minutos

    public CampinasApiService(BrowserPoolService browserPool, ApiKeyService apiKeyService)
    {
        _browserPool = browserPool;
        _apiKeyService = apiKeyService;
        
        // Inicializa o MagicBytesValidator
        _mapping = new Mapping();
        _fileTypeProvider = new StreamFileTypeProvider(_mapping);
        
        // HttpClient para downloads de arquivos
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
    /// Identifica a extensão do arquivo usando MagicBytesValidator.
    /// Analisa os magic bytes do arquivo para determinar o tipo real.
    /// </summary>
    private async Task<(string extension, string mimeType)> IdentifyFileTypeAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var fileType = await _fileTypeProvider.TryFindUnambiguousAsync(stream, cancellationToken);
            
            if (fileType != null)
            {
                var extension = fileType.Extensions.FirstOrDefault() ?? "";
                var mimeType = fileType.MimeTypes.FirstOrDefault() ?? "application/octet-stream";
                
                Console.WriteLine($"[MagicBytes] Tipo identificado: {mimeType}, extensão: .{extension}");
                return (extension, mimeType);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MagicBytes] Erro ao identificar tipo: {ex.Message}");
        }
        
        return ("", "application/octet-stream");
    }

    /// <summary>
    /// Verifica se o nome do arquivo tem uma extensão válida conhecida.
    /// </summary>
    private bool HasValidExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;
            
        // Procura por .extensão no final do nome
        var match = Regex.Match(fileName, @"\.([a-zA-Z0-9]+)$");
        if (match.Success)
        {
            var ext = match.Groups[1].Value;
            return KnownExtensions.Contains(ext);
        }
        
        return false;
    }

    /// <summary>
    /// Extrai o nome do arquivo do header Content-Disposition de uma resposta HTTP.
    /// </summary>
    private string ExtractFileNameFromHttpHeaders(HttpResponseMessage response, string defaultName)
    {
        var contentDisposition = response.Content.Headers.ContentDisposition;
        
        if (contentDisposition != null)
        {
            Console.WriteLine($"[CampinasApi] Content-Disposition: {contentDisposition}");
            
            // Tenta filename* primeiro (RFC 5987)
            if (!string.IsNullOrEmpty(contentDisposition.FileNameStar))
            {
                var fileName = contentDisposition.FileNameStar;
                Console.WriteLine($"[CampinasApi] Nome extraído (FileNameStar): {fileName}");
                return fileName;
            }
            
            // Fallback para filename
            if (!string.IsNullOrEmpty(contentDisposition.FileName))
            {
                var fileName = contentDisposition.FileName.Trim('"');
                Console.WriteLine($"[CampinasApi] Nome extraído (FileName): {fileName}");
                return fileName;
            }
        }
        
        // Tenta extrair do header raw se o parser não conseguiu
        if (response.Content.Headers.TryGetValues("Content-Disposition", out var values))
        {
            var headerValue = values.FirstOrDefault();
            if (!string.IsNullOrEmpty(headerValue))
            {
                Console.WriteLine($"[CampinasApi] Content-Disposition raw: {headerValue}");
                
                // Tenta extrair filename*=UTF-8''nome
                var match = Regex.Match(headerValue, @"filename\*=UTF-8''([^;]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var fileName = Uri.UnescapeDataString(match.Groups[1].Value);
                    Console.WriteLine($"[CampinasApi] Nome extraído (regex filename*): {fileName}");
                    return fileName;
                }
                
                // Tenta extrair filename="nome" ou filename=nome
                match = Regex.Match(headerValue, @"filename=""?([^"";]+)""?", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var fileName = match.Groups[1].Value;
                    Console.WriteLine($"[CampinasApi] Nome extraído (regex filename): {fileName}");
                    return fileName;
                }
            }
        }
        
        return defaultName;
    }

    /// <summary>
    /// Faz download de um arquivo da API de Campinas usando HttpClient.
    /// Usa streaming para suportar arquivos grandes.
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

        // Lê o arquivo como stream para suportar arquivos grandes
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        
        // Identifica o tipo real do arquivo usando magic bytes
        var (detectedExtension, detectedMimeType) = await IdentifyFileTypeAsync(bytes, cancellationToken);
        
        // Usa o content-type detectado pelos magic bytes, ou fallback para o header
        var contentType = !string.IsNullOrEmpty(detectedMimeType) && detectedMimeType != "application/octet-stream"
            ? detectedMimeType
            : response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        
        // Extrai nome do arquivo do header
        var fileName = ExtractFileNameFromHttpHeaders(response, $"arquivo_{arquivoId}");
        
        // Adiciona extensão detectada pelos magic bytes se não tiver extensão válida
        if (!HasValidExtension(fileName) && !string.IsNullOrEmpty(detectedExtension))
        {
            fileName += $".{detectedExtension}";
            Console.WriteLine($"[CampinasApi] Extensão adicionada via magic bytes: .{detectedExtension}");
        }

        Console.WriteLine($"[CampinasApi] Download concluído: {fileName} ({bytes.Length} bytes, {contentType})");
        
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
        
        Console.WriteLine($"[CampinasApi] DOWNLOAD EMPENHO ARQUIVO {url} (via HttpClient)");
        
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
        var (detectedExtension, detectedMimeType) = await IdentifyFileTypeAsync(bytes, cancellationToken);
        
        var contentType = !string.IsNullOrEmpty(detectedMimeType) && detectedMimeType != "application/octet-stream"
            ? detectedMimeType
            : response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        
        var fileName = ExtractFileNameFromHttpHeaders(response, $"empenho_{empenhoId}_arquivo_{arquivoId}");
        
        if (!HasValidExtension(fileName) && !string.IsNullOrEmpty(detectedExtension))
        {
            fileName += $".{detectedExtension}";
            Console.WriteLine($"[CampinasApi] Extensão adicionada via magic bytes: .{detectedExtension}");
        }

        Console.WriteLine($"[CampinasApi] Download concluído: {fileName} ({bytes.Length} bytes, {contentType})");
        
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
        var (detectedExtension, detectedMimeType) = await IdentifyFileTypeAsync(bytes, cancellationToken);
        
        var contentType = !string.IsNullOrEmpty(detectedMimeType) && detectedMimeType != "application/octet-stream"
            ? detectedMimeType
            : response.Content.Headers.ContentType?.MediaType ?? "application/pdf";
        
        var fileName = ExtractFileNameFromHttpHeaders(response, $"empenho_{empenhoId}");
        
        // Adiciona extensão se não tiver extensão válida
        if (!HasValidExtension(fileName))
        {
            if (!string.IsNullOrEmpty(detectedExtension))
            {
                fileName += $".{detectedExtension}";
                Console.WriteLine($"[CampinasApi] Extensão adicionada via magic bytes: .{detectedExtension}");
            }
            else
            {
                // Fallback para .pdf se não conseguir detectar
                fileName += ".pdf";
            }
        }

        Console.WriteLine($"[CampinasApi] Download concluído: {fileName} ({bytes.Length} bytes, {contentType})");
        
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
