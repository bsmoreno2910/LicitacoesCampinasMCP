using LicitacoesCampinasMCP.Dominio;
using Microsoft.Playwright;
using System.Text.Json;
using MagicBytesValidator.Services;
using MagicBytesValidator.Services.Streams;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Serviço responsável por fazer requisições à API de contratações de Campinas.
/// Usa Playwright/Browserless para todas as requisições (API e downloads).
/// Utiliza MagicBytesValidator para identificar o tipo correto de arquivo pelos magic bytes.
/// </summary>
public class CampinasApiService : IAsyncDisposable
{
    private readonly BrowserPoolService _browserPool;
    private readonly ApiKeyService _apiKeyService;
    private readonly StreamFileTypeProvider _fileTypeProvider;
    private readonly Mapping _mapping;
    
    private const string BASE_URL = "https://contratacoes-api.campinas.sp.gov.br";
    private const int REQUEST_TIMEOUT_MS = 300000; // 5 minutos

    public CampinasApiService(BrowserPoolService browserPool, ApiKeyService apiKeyService)
    {
        _browserPool = browserPool;
        _apiKeyService = apiKeyService;
        
        // Inicializa o MagicBytesValidator
        _mapping = new Mapping();
        _fileTypeProvider = new StreamFileTypeProvider(_mapping);
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
    /// Cria um contexto de API para download de arquivos (aceita qualquer tipo).
    /// </summary>
    private async Task<IAPIRequestContext> CreateDownloadContextAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        
        Console.WriteLine("[CampinasApi] Criando contexto de download via Browserless...");
        return await session.Playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL = BASE_URL,
            Timeout = REQUEST_TIMEOUT_MS,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["x-api-key"] = apiKey,
                ["Accept"] = "*/*",
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
    /// Extrai o nome do arquivo do header Content-Disposition.
    /// </summary>
    private string ExtractFileNameFromHeaders(IAPIResponse response, string defaultName)
    {
        var headers = response.Headers;
        
        if (headers.TryGetValue("content-disposition", out var contentDisposition) && !string.IsNullOrEmpty(contentDisposition))
        {
            Console.WriteLine($"[CampinasApi] Content-Disposition: {contentDisposition}");
            
            // Tenta extrair filename*=UTF-8''nome ou filename="nome"
            var parts = contentDisposition.Split(';');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                
                // filename*=UTF-8''nome
                if (trimmed.StartsWith("filename*=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed.Substring(10);
                    // Remove encoding prefix se existir (ex: UTF-8'')
                    var idx = value.IndexOf("''");
                    if (idx >= 0)
                    {
                        value = Uri.UnescapeDataString(value.Substring(idx + 2));
                    }
                    if (!string.IsNullOrEmpty(value))
                    {
                        Console.WriteLine($"[CampinasApi] Nome extraído (filename*): {value}");
                        return value;
                    }
                }
                // filename="nome" ou filename=nome
                else if (trimmed.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed.Substring(9).Trim('"');
                    if (!string.IsNullOrEmpty(value))
                    {
                        Console.WriteLine($"[CampinasApi] Nome extraído (filename): {value}");
                        return value;
                    }
                }
            }
        }
        
        return defaultName;
    }

    /// <summary>
    /// Faz download de um arquivo da API de Campinas usando Playwright/Browserless.
    /// Usa MagicBytesValidator para identificar a extensão correta do arquivo.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoAsync(string compraId, string arquivoId, CancellationToken cancellationToken = default)
    {
        BrowserSession? session = null;
        IAPIRequestContext? apiContext = null;
        
        try
        {
            session = await _browserPool.AcquireSessionAsync(cancellationToken);
            apiContext = await CreateDownloadContextAsync(session, cancellationToken);
            
            var endpoint = $"/compras/{compraId}/arquivos/{arquivoId}/blob";
            Console.WriteLine($"[CampinasApi] DOWNLOAD {endpoint} (via Browserless)");
            
            var response = await apiContext.GetAsync(endpoint);
            
            // Se a chave expirou, tenta renovar
            if (!response.Ok && (response.Status == 401 || response.Status == 403))
            {
                Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
                _apiKeyService.InvalidateApiKey();
                
                await apiContext.DisposeAsync();
                apiContext = await CreateDownloadContextAsync(session, cancellationToken);
                response = await apiContext.GetAsync(endpoint);
            }

            if (!response.Ok)
            {
                var errorBody = await response.TextAsync();
                throw new Exception($"Erro ao baixar arquivo: {response.Status} - {errorBody}");
            }

            // Lê o arquivo como bytes
            var bytes = await response.BodyAsync();
            
            // Identifica o tipo real do arquivo usando magic bytes
            var (detectedExtension, detectedMimeType) = await IdentifyFileTypeAsync(bytes, cancellationToken);
            
            // Usa o content-type detectado pelos magic bytes, ou fallback para o header
            var headerContentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "application/octet-stream";
            var contentType = !string.IsNullOrEmpty(detectedMimeType) && detectedMimeType != "application/octet-stream"
                ? detectedMimeType
                : headerContentType;
            
            // Extrai nome do arquivo do header
            var fileName = ExtractFileNameFromHeaders(response, $"arquivo_{arquivoId}");
            
            // Adiciona extensão detectada pelos magic bytes se não tiver extensão
            if (!fileName.Contains(".") && !string.IsNullOrEmpty(detectedExtension))
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
        finally
        {
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
    /// Faz download de um arquivo de empenho da API de Campinas usando Playwright/Browserless.
    /// Usa MagicBytesValidator para identificar a extensão correta do arquivo.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoEmpenhoAsync(string compraId, string empenhoId, string arquivoId, CancellationToken cancellationToken = default)
    {
        BrowserSession? session = null;
        IAPIRequestContext? apiContext = null;
        
        try
        {
            session = await _browserPool.AcquireSessionAsync(cancellationToken);
            apiContext = await CreateDownloadContextAsync(session, cancellationToken);
            
            var endpoint = $"/compras/{compraId}/empenhos/{empenhoId}/arquivos/{arquivoId}/blob";
            Console.WriteLine($"[CampinasApi] DOWNLOAD EMPENHO ARQUIVO {endpoint} (via Browserless)");
            
            var response = await apiContext.GetAsync(endpoint);
            
            if (!response.Ok && (response.Status == 401 || response.Status == 403))
            {
                Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
                _apiKeyService.InvalidateApiKey();
                
                await apiContext.DisposeAsync();
                apiContext = await CreateDownloadContextAsync(session, cancellationToken);
                response = await apiContext.GetAsync(endpoint);
            }

            if (!response.Ok)
            {
                var errorBody = await response.TextAsync();
                throw new Exception($"Erro ao baixar arquivo de empenho: {response.Status} - {errorBody}");
            }

            var bytes = await response.BodyAsync();
            var (detectedExtension, detectedMimeType) = await IdentifyFileTypeAsync(bytes, cancellationToken);
            
            var headerContentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "application/octet-stream";
            var contentType = !string.IsNullOrEmpty(detectedMimeType) && detectedMimeType != "application/octet-stream"
                ? detectedMimeType
                : headerContentType;
            
            var fileName = ExtractFileNameFromHeaders(response, $"empenho_{empenhoId}_arquivo_{arquivoId}");
            
            if (!fileName.Contains(".") && !string.IsNullOrEmpty(detectedExtension))
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
        finally
        {
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
    /// Faz download direto do PDF de um empenho da API de Campinas usando Playwright/Browserless.
    /// O endpoint /compras/{compraId}/empenhos/{empenhoId} retorna diretamente o arquivo PDF.
    /// Usa MagicBytesValidator para identificar a extensão correta do arquivo.
    /// </summary>
    public async Task<ArquivoDownload> DownloadEmpenhoAsync(string compraId, string empenhoId, CancellationToken cancellationToken = default)
    {
        BrowserSession? session = null;
        IAPIRequestContext? apiContext = null;
        
        try
        {
            session = await _browserPool.AcquireSessionAsync(cancellationToken);
            apiContext = await CreateDownloadContextAsync(session, cancellationToken);
            
            var endpoint = $"/compras/{compraId}/empenhos/{empenhoId}";
            Console.WriteLine($"[CampinasApi] DOWNLOAD EMPENHO DIRETO {endpoint} (via Browserless)");
            
            var response = await apiContext.GetAsync(endpoint);
            
            if (!response.Ok && (response.Status == 401 || response.Status == 403))
            {
                Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
                _apiKeyService.InvalidateApiKey();
                
                await apiContext.DisposeAsync();
                apiContext = await CreateDownloadContextAsync(session, cancellationToken);
                response = await apiContext.GetAsync(endpoint);
            }

            if (!response.Ok)
            {
                var errorBody = await response.TextAsync();
                throw new Exception($"Erro ao baixar empenho: {response.Status} - {errorBody}");
            }

            var bytes = await response.BodyAsync();
            var (detectedExtension, detectedMimeType) = await IdentifyFileTypeAsync(bytes, cancellationToken);
            
            var headerContentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "application/pdf";
            var contentType = !string.IsNullOrEmpty(detectedMimeType) && detectedMimeType != "application/octet-stream"
                ? detectedMimeType
                : headerContentType;
            
            var fileName = ExtractFileNameFromHeaders(response, $"empenho_{empenhoId}");
            
            // Adiciona extensão se não tiver
            if (!fileName.Contains("."))
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
        finally
        {
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

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
