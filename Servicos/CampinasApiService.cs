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
/// Usa Playwright LOCAL para downloads de arquivos, salvando em disco via streaming.
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
    private readonly SemaphoreSlim _localPlaywrightLock = new(1, 1);
    
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
    
    // Mapeamento de magic bytes para extensões (fallback manual)
    private static readonly Dictionary<string, (string extension, string mimeType)> MagicBytesMap = new()
    {
        // PDF
        { "255044462D", ("pdf", "application/pdf") },
        // ZIP
        { "504B0304", ("zip", "application/zip") },
        { "504B0506", ("zip", "application/zip") },
        { "504B0708", ("zip", "application/zip") },
        // RAR
        { "526172211A07", ("rar", "application/x-rar-compressed") },
        // 7Z
        { "377ABCAF271C", ("7z", "application/x-7z-compressed") },
        // PNG
        { "89504E470D0A1A0A", ("png", "image/png") },
        // JPEG
        { "FFD8FF", ("jpg", "image/jpeg") },
        // GIF
        { "474946383761", ("gif", "image/gif") },
        { "474946383961", ("gif", "image/gif") },
        // DOCX/XLSX/PPTX (são ZIPs)
        // Word DOC
        { "D0CF11E0A1B11AE1", ("doc", "application/msword") },
        // XML
        { "3C3F786D6C", ("xml", "application/xml") },
    };
    
    private const string BASE_URL = "https://contratacoes-api.campinas.sp.gov.br";
    private const int REQUEST_TIMEOUT_MS = 600000; // 10 minutos para arquivos grandes

    public CampinasApiService(BrowserPoolService browserPool, ApiKeyService apiKeyService)
    {
        _browserPool = browserPool;
        _apiKeyService = apiKeyService;
        
        // Inicializa o MagicBytesValidator
        _mapping = new Mapping();
        _fileTypeProvider = new StreamFileTypeProvider(_mapping);
    }

    /// <summary>
    /// Obtém ou cria uma instância do Playwright e Browser local para downloads.
    /// </summary>
    private async Task<IBrowser> GetLocalBrowserAsync(CancellationToken cancellationToken = default)
    {
        await _localPlaywrightLock.WaitAsync(cancellationToken);
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
            _localPlaywrightLock.Release();
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
    /// Busca informações de um arquivo específico da API.
    /// </summary>
    private async Task<string?> GetArquivoNomeAsync(string compraId, string arquivoId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await GetAsync($"/compras/{compraId}/arquivos/{arquivoId}", cancellationToken);
            if (result != null)
            {
                if (result.RootElement.TryGetProperty("nome", out var nomeElement))
                {
                    var nome = nomeElement.GetString();
                    Console.WriteLine($"[CampinasApi] Nome do arquivo obtido da API: {nome}");
                    return nome;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CampinasApi] Erro ao buscar nome do arquivo: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Identifica a extensão do arquivo usando magic bytes (fallback manual).
    /// </summary>
    private (string extension, string mimeType) IdentifyFileTypeByMagicBytes(byte[] headerBytes)
    {
        if (headerBytes == null || headerBytes.Length < 4)
            return ("", "application/octet-stream");
            
        var hex = BitConverter.ToString(headerBytes).Replace("-", "").ToUpperInvariant();
        Console.WriteLine($"[MagicBytes] Primeiros bytes (hex): {hex.Substring(0, Math.Min(20, hex.Length))}...");
        
        foreach (var (signature, result) in MagicBytesMap)
        {
            if (hex.StartsWith(signature))
            {
                Console.WriteLine($"[MagicBytes] Tipo identificado: {result.mimeType}, extensão: .{result.extension}");
                return result;
            }
        }
        
        Console.WriteLine("[MagicBytes] Tipo não identificado pelos magic bytes");
        return ("", "application/octet-stream");
    }

    /// <summary>
    /// Identifica a extensão do arquivo usando MagicBytesValidator a partir de um arquivo.
    /// Se não conseguir, usa fallback manual.
    /// </summary>
    private async Task<(string extension, string mimeType)> IdentifyFileTypeFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Primeiro tenta com MagicBytesValidator
            using (var stream = File.OpenRead(filePath))
            {
                var fileType = await _fileTypeProvider.TryFindUnambiguousAsync(stream, cancellationToken);
                
                if (fileType != null)
                {
                    var extension = fileType.Extensions.FirstOrDefault() ?? "";
                    var mimeType = fileType.MimeTypes.FirstOrDefault() ?? "application/octet-stream";
                    
                    if (!string.IsNullOrEmpty(extension))
                    {
                        Console.WriteLine($"[MagicBytes] Tipo identificado via MagicBytesValidator: {mimeType}, extensão: .{extension}");
                        return (extension, mimeType);
                    }
                }
            }
            
            // Fallback: lê os primeiros bytes e identifica manualmente
            Console.WriteLine("[MagicBytes] MagicBytesValidator não identificou, tentando fallback manual...");
            var headerBytes = new byte[16];
            using (var stream = File.OpenRead(filePath))
            {
                await stream.ReadAsync(headerBytes, 0, headerBytes.Length, cancellationToken);
            }
            
            return IdentifyFileTypeByMagicBytes(headerBytes);
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
    /// Faz download de um arquivo usando Playwright via página, salvando em disco.
    /// Suporta arquivos de qualquer tamanho pois não carrega na memória.
    /// </summary>
    private async Task<ArquivoDownload> DownloadToFileAsync(string url, string fileName, CancellationToken cancellationToken = default)
    {
        var browser = await GetLocalBrowserAsync(cancellationToken);
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        
        // Cria diretório temporário para downloads
        var downloadPath = Path.Combine(Path.GetTempPath(), $"campinas_download_{Guid.NewGuid()}");
        Directory.CreateDirectory(downloadPath);
        
        IBrowserContext? context = null;
        IPage? page = null;
        string? downloadedFilePath = null;
        
        try
        {
            Console.WriteLine($"[CampinasApi] DOWNLOAD VIA PÁGINA: {url}");
            Console.WriteLine($"[CampinasApi] Nome do arquivo: {fileName}");
            Console.WriteLine($"[CampinasApi] Diretório de download: {downloadPath}");
            
            // Cria contexto com diretório de download configurado
            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                AcceptDownloads = true
            });
            
            page = await context.NewPageAsync();
            
            // Intercepta requisições para adicionar headers
            await page.RouteAsync("**/*", async route =>
            {
                var request = route.Request;
                if (request.Url.Contains("contratacoes-api.campinas.sp.gov.br"))
                {
                    var headers = new Dictionary<string, string>(request.Headers)
                    {
                        ["x-api-key"] = apiKey,
                        ["Origin"] = "https://campinas.sp.gov.br"
                    };
                    await route.ContinueAsync(new RouteContinueOptions { Headers = headers });
                }
                else
                {
                    await route.ContinueAsync();
                }
            });
            
            // Configura handler para capturar download
            var downloadTcs = new TaskCompletionSource<IDownload>();
            page.Download += (_, download) => downloadTcs.TrySetResult(download);
            
            // Navega para a URL - isso vai disparar o download
            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    Timeout = REQUEST_TIMEOUT_MS,
                    WaitUntil = WaitUntilState.Commit
                });
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("net::ERR_ABORTED"))
            {
                // Isso é esperado para downloads - a navegação é abortada quando o download começa
                Console.WriteLine("[CampinasApi] Navegação abortada (esperado para download)");
            }
            
            // Aguarda o download com timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS));
            
            IDownload download;
            try
            {
                download = await downloadTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new Exception("Timeout aguardando início do download");
            }
            
            // Salva o arquivo em disco com nome temporário
            var tempFileName = $"temp_{Guid.NewGuid()}";
            downloadedFilePath = Path.Combine(downloadPath, tempFileName);
            await download.SaveAsAsync(downloadedFilePath);
            
            var fileInfo = new FileInfo(downloadedFilePath);
            Console.WriteLine($"[CampinasApi] Arquivo salvo: {downloadedFilePath} ({fileInfo.Length} bytes)");
            
            // Identifica o tipo do arquivo usando magic bytes
            var (detectedExtension, detectedMimeType) = await IdentifyFileTypeFromFileAsync(downloadedFilePath, cancellationToken);
            
            var contentType = !string.IsNullOrEmpty(detectedMimeType) && detectedMimeType != "application/octet-stream"
                ? detectedMimeType
                : "application/octet-stream";
            
            // Usa o nome fornecido e adiciona extensão se necessário
            var finalFileName = fileName;
            Console.WriteLine($"[CampinasApi] Nome antes de adicionar extensão: {finalFileName}");
            Console.WriteLine($"[CampinasApi] HasValidExtension: {HasValidExtension(finalFileName)}");
            Console.WriteLine($"[CampinasApi] Extensão detectada: {detectedExtension}");
            
            if (!HasValidExtension(finalFileName) && !string.IsNullOrEmpty(detectedExtension))
            {
                finalFileName += $".{detectedExtension}";
                Console.WriteLine($"[CampinasApi] Extensão adicionada: .{detectedExtension}");
            }
            
            // Lê o arquivo do disco
            var bytes = await File.ReadAllBytesAsync(downloadedFilePath, cancellationToken);
            
            Console.WriteLine($"[CampinasApi] Download concluído: {finalFileName} ({bytes.Length} bytes, {contentType})");
            
            return new ArquivoDownload
            {
                Bytes = bytes,
                ContentType = contentType,
                FileName = finalFileName
            };
        }
        finally
        {
            // Limpa recursos
            if (page != null) 
            {
                try { await page.CloseAsync(); } catch { }
            }
            if (context != null) 
            {
                try { await context.CloseAsync(); } catch { }
            }
            
            // Remove diretório temporário
            try
            {
                if (Directory.Exists(downloadPath))
                    Directory.Delete(downloadPath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CampinasApi] Erro ao limpar diretório temporário: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Faz download de um arquivo da API de Campinas.
    /// Busca o nome do arquivo da API antes de fazer o download.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoAsync(string compraId, string arquivoId, CancellationToken cancellationToken = default)
    {
        // Busca o nome do arquivo da API
        var nomeArquivo = await GetArquivoNomeAsync(compraId, arquivoId, cancellationToken);
        var fileName = !string.IsNullOrEmpty(nomeArquivo) ? nomeArquivo : $"arquivo_{arquivoId}";
        
        var url = $"{BASE_URL}/compras/{compraId}/arquivos/{arquivoId}/blob";
        return await DownloadToFileAsync(url, fileName, cancellationToken);
    }

    /// <summary>
    /// Faz download de um arquivo de empenho da API de Campinas.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoEmpenhoAsync(string compraId, string empenhoId, string arquivoId, CancellationToken cancellationToken = default)
    {
        var url = $"{BASE_URL}/compras/{compraId}/empenhos/{empenhoId}/arquivos/{arquivoId}/blob";
        return await DownloadToFileAsync(url, $"empenho_{empenhoId}_arquivo_{arquivoId}", cancellationToken);
    }

    /// <summary>
    /// Faz download direto do PDF de um empenho da API de Campinas.
    /// O endpoint /compras/{compraId}/empenhos/{empenhoId} retorna diretamente o arquivo PDF.
    /// </summary>
    public async Task<ArquivoDownload> DownloadEmpenhoAsync(string compraId, string empenhoId, CancellationToken cancellationToken = default)
    {
        var url = $"{BASE_URL}/compras/{compraId}/empenhos/{empenhoId}";
        var result = await DownloadToFileAsync(url, $"empenho_{empenhoId}", cancellationToken);
        
        // Se não conseguiu detectar extensão, assume PDF
        if (!HasValidExtension(result.FileName))
        {
            result.FileName += ".pdf";
        }
        
        return result;
    }
    
    /// <summary>
    /// Retorna a URL direta para download de um arquivo, junto com a API key.
    /// Útil para arquivos muito grandes que não podem ser baixados via proxy.
    /// </summary>
    public async Task<(string Url, string ApiKey)> GetDownloadUrlAsync(string compraId, string arquivoId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        var url = $"{BASE_URL}/compras/{compraId}/arquivos/{arquivoId}/blob";
        return (url, apiKey);
    }

    public async ValueTask DisposeAsync()
    {
        if (_localBrowser != null)
        {
            try { await _localBrowser.CloseAsync(); } catch { }
            _localBrowser = null;
        }
        
        _localPlaywright?.Dispose();
        _localPlaywright = null;
        
        _localPlaywrightLock.Dispose();
    }
}
