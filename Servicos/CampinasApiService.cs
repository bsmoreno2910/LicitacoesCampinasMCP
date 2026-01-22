using LicitacoesCampinasMCP.Dominio;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;
using MagicBytesValidator.Services;
using MagicBytesValidator.Services.Streams;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Serviço responsável por fazer requisições à API de contratações de Campinas.
/// Usa Playwright/Browserless para requisições de API JSON.
/// Usa Playwright LOCAL com download via página para arquivos (suporta arquivos grandes).
/// Utiliza MagicBytesValidator para identificar o tipo correto de arquivo pelos magic bytes.
/// </summary>
public class CampinasApiService : IAsyncDisposable
{
    private readonly BrowserPoolService _browserPool;
    private readonly ApiKeyService _apiKeyService;
    private readonly StreamFileTypeProvider _fileTypeProvider;
    private readonly Mapping _mapping;
    
    // Playwright local para downloads
    private IPlaywright? _localPlaywright;
    private IBrowser? _localBrowser;
    private readonly SemaphoreSlim _localBrowserLock = new(1, 1);
    
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
    }

    /// <summary>
    /// Obtém ou cria uma instância do browser local para downloads.
    /// </summary>
    private async Task<IBrowser> GetLocalBrowserAsync(CancellationToken cancellationToken = default)
    {
        await _localBrowserLock.WaitAsync(cancellationToken);
        try
        {
            if (_localPlaywright == null || _localBrowser == null)
            {
                Console.WriteLine("[CampinasApi] Inicializando Playwright LOCAL para downloads...");
                _localPlaywright = await Playwright.CreateAsync();
                _localBrowser = await _localPlaywright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true
                });
                Console.WriteLine("[CampinasApi] Playwright LOCAL inicializado com sucesso!");
            }
            
            return _localBrowser;
        }
        finally
        {
            _localBrowserLock.Release();
        }
    }

    /// <summary>
    /// Cria um contexto de API com a chave atual usando uma sessão do pool (Browserless).
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
    /// Identifica a extensão do arquivo usando MagicBytesValidator a partir de um stream.
    /// </summary>
    private async Task<(string extension, string mimeType)> IdentifyFileTypeFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
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
    /// Faz download de um arquivo usando Playwright via página (suporta arquivos grandes).
    /// O arquivo é baixado para disco e depois lido em streaming.
    /// </summary>
    private async Task<ArquivoDownload> DownloadViaPageAsync(string url, string defaultFileName, CancellationToken cancellationToken = default)
    {
        var browser = await GetLocalBrowserAsync(cancellationToken);
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        
        // Cria diretório temporário para downloads
        var downloadPath = Path.Combine(Path.GetTempPath(), $"download_{Guid.NewGuid()}");
        Directory.CreateDirectory(downloadPath);
        
        IBrowserContext? context = null;
        IPage? page = null;
        
        try
        {
            Console.WriteLine($"[CampinasApi] DOWNLOAD VIA PÁGINA: {url}");
            
            // Cria contexto com diretório de download
            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                AcceptDownloads = true,
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["x-api-key"] = apiKey,
                    ["Origin"] = "https://campinas.sp.gov.br"
                }
            });
            
            page = await context.NewPageAsync();
            
            // Configura handler para interceptar download
            var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions
            {
                Timeout = REQUEST_TIMEOUT_MS
            });
            
            // Navega para a URL (isso vai disparar o download)
            await page.GotoAsync(url, new PageGotoOptions
            {
                Timeout = REQUEST_TIMEOUT_MS,
                WaitUntil = WaitUntilState.Commit
            });
            
            // Aguarda o download
            var download = await downloadTask;
            
            // Obtém o nome sugerido do arquivo
            var suggestedFileName = download.SuggestedFilename;
            Console.WriteLine($"[CampinasApi] Nome sugerido pelo download: {suggestedFileName}");
            
            // Salva o arquivo
            var filePath = Path.Combine(downloadPath, suggestedFileName ?? defaultFileName);
            await download.SaveAsAsync(filePath);
            
            Console.WriteLine($"[CampinasApi] Arquivo salvo em: {filePath}");
            
            // Lê o arquivo
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            
            // Identifica o tipo do arquivo
            using var fileStream = File.OpenRead(filePath);
            var (detectedExtension, detectedMimeType) = await IdentifyFileTypeFromStreamAsync(fileStream, cancellationToken);
            
            var contentType = !string.IsNullOrEmpty(detectedMimeType) && detectedMimeType != "application/octet-stream"
                ? detectedMimeType
                : "application/octet-stream";
            
            var fileName = suggestedFileName ?? defaultFileName;
            
            // Adiciona extensão se não tiver
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
        finally
        {
            // Limpa recursos
            if (page != null) await page.CloseAsync();
            if (context != null) await context.CloseAsync();
            
            // Remove diretório temporário
            try
            {
                if (Directory.Exists(downloadPath))
                    Directory.Delete(downloadPath, true);
            }
            catch { /* Ignora erros de limpeza */ }
        }
    }

    /// <summary>
    /// Faz download de um arquivo da API de Campinas usando Playwright LOCAL via página.
    /// Suporta arquivos grandes salvando em disco primeiro.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoAsync(string compraId, string arquivoId, CancellationToken cancellationToken = default)
    {
        var url = $"{BASE_URL}/compras/{compraId}/arquivos/{arquivoId}/blob";
        return await DownloadViaPageAsync(url, $"arquivo_{arquivoId}", cancellationToken);
    }

    /// <summary>
    /// Faz download de um arquivo de empenho da API de Campinas usando Playwright LOCAL via página.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoEmpenhoAsync(string compraId, string empenhoId, string arquivoId, CancellationToken cancellationToken = default)
    {
        var url = $"{BASE_URL}/compras/{compraId}/empenhos/{empenhoId}/arquivos/{arquivoId}/blob";
        return await DownloadViaPageAsync(url, $"empenho_{empenhoId}_arquivo_{arquivoId}", cancellationToken);
    }

    /// <summary>
    /// Faz download direto do PDF de um empenho da API de Campinas usando Playwright LOCAL via página.
    /// O endpoint /compras/{compraId}/empenhos/{empenhoId} retorna diretamente o arquivo PDF.
    /// </summary>
    public async Task<ArquivoDownload> DownloadEmpenhoAsync(string compraId, string empenhoId, CancellationToken cancellationToken = default)
    {
        var url = $"{BASE_URL}/compras/{compraId}/empenhos/{empenhoId}";
        var result = await DownloadViaPageAsync(url, $"empenho_{empenhoId}", cancellationToken);
        
        // Se não conseguiu detectar extensão, assume PDF
        if (!HasValidExtension(result.FileName))
        {
            result.FileName += ".pdf";
        }
        
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_localBrowser != null)
        {
            await _localBrowser.CloseAsync();
            _localBrowser = null;
        }
        
        _localPlaywright?.Dispose();
        _localPlaywright = null;
        
        _localBrowserLock.Dispose();
    }
}
