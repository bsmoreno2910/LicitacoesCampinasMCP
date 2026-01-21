using Microsoft.Playwright;
using System.Collections.Concurrent;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Gerencia um pool de conexões de browser para suportar múltiplas requisições simultâneas.
/// Mantém sessões abertas e reutiliza conexões para melhor performance.
/// </summary>
public class BrowserPoolService : IAsyncDisposable
{
    private readonly int _maxConnections;
    private readonly string _browserlessUrl;
    private readonly string _proxyServer;
    private readonly SemaphoreSlim _poolSemaphore;
    private readonly ConcurrentBag<BrowserSession> _availableSessions;
    private readonly ConcurrentDictionary<Guid, BrowserSession> _activeSessions;
    private IPlaywright? _playwright;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // Configuração padrão do proxy
    private const string DEFAULT_PROXY_SERVER = "http://proxy-server.vtc.dev.br:3128";

    public BrowserPoolService(int maxConnections = 20, string? browserlessUrl = null, string? proxyServer = null)
    {
        _maxConnections = maxConnections;
        _browserlessUrl = browserlessUrl ?? "wss://browserless.vtc.dev.br/?token=78e69a4812450ad4e2e657ee2cdf90b5";
        _proxyServer = proxyServer ?? DEFAULT_PROXY_SERVER;
        _poolSemaphore = new SemaphoreSlim(maxConnections, maxConnections);
        _availableSessions = new ConcurrentBag<BrowserSession>();
        _activeSessions = new ConcurrentDictionary<Guid, BrowserSession>();
    }

    /// <summary>
    /// Obtém a URL do proxy configurado.
    /// </summary>
    public string ProxyServer => _proxyServer;

    /// <summary>
    /// Inicializa o Playwright se ainda não foi inicializado.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;
            
            Console.WriteLine("[BrowserPool] Inicializando Playwright...");
            Console.WriteLine($"[BrowserPool] Proxy configurado: {_proxyServer}");
            _playwright = await Playwright.CreateAsync();
            _initialized = true;
            Console.WriteLine("[BrowserPool] Playwright inicializado com sucesso.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Adquire uma sessão de browser do pool.
    /// Se não houver sessões disponíveis, cria uma nova (até o limite máximo).
    /// </summary>
    public async Task<BrowserSession> AcquireSessionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        
        // Aguarda até ter uma vaga disponível no pool
        await _poolSemaphore.WaitAsync(cancellationToken);

        try
        {
            // Tenta reutilizar uma sessão existente
            if (_availableSessions.TryTake(out var existingSession))
            {
                if (await existingSession.IsValidAsync())
                {
                    existingSession.MarkAsActive();
                    _activeSessions[existingSession.Id] = existingSession;
                    Console.WriteLine($"[BrowserPool] Reutilizando sessão {existingSession.Id}");
                    return existingSession;
                }
                else
                {
                    // Sessão inválida, descarta
                    Console.WriteLine($"[BrowserPool] Sessão {existingSession.Id} inválida, descartando...");
                    await existingSession.DisposeAsync();
                }
            }

            // Cria uma nova sessão
            var newSession = await CreateSessionAsync();
            _activeSessions[newSession.Id] = newSession;
            Console.WriteLine($"[BrowserPool] Nova sessão criada: {newSession.Id}");
            return newSession;
        }
        catch
        {
            _poolSemaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// Libera uma sessão de volta para o pool.
    /// </summary>
    public async Task ReleaseSessionAsync(BrowserSession session)
    {
        if (session == null) return;

        try
        {
            _activeSessions.TryRemove(session.Id, out _);

            if (await session.IsValidAsync())
            {
                session.MarkAsIdle();
                _availableSessions.Add(session);
                Console.WriteLine($"[BrowserPool] Sessão {session.Id} retornada ao pool.");
            }
            else
            {
                Console.WriteLine($"[BrowserPool] Sessão {session.Id} inválida, descartando...");
                await session.DisposeAsync();
            }
        }
        finally
        {
            _poolSemaphore.Release();
        }
    }

    /// <summary>
    /// Cria uma nova sessão de browser conectando ao Browserless.
    /// </summary>
    private async Task<BrowserSession> CreateSessionAsync()
    {
        if (_playwright == null)
            throw new InvalidOperationException("Playwright não inicializado");

        var browser = await _playwright.Chromium.ConnectOverCDPAsync(_browserlessUrl, new BrowserTypeConnectOverCDPOptions
        {
            Timeout = 60000
        });

        return new BrowserSession(browser, _playwright, _proxyServer);
    }

    /// <summary>
    /// Obtém estatísticas do pool.
    /// </summary>
    public PoolStats GetStats()
    {
        return new PoolStats
        {
            MaxConnections = _maxConnections,
            AvailableSessions = _availableSessions.Count,
            ActiveSessions = _activeSessions.Count,
            WaitingRequests = _maxConnections - _poolSemaphore.CurrentCount - _activeSessions.Count,
            ProxyServer = _proxyServer
        };
    }

    public async ValueTask DisposeAsync()
    {
        Console.WriteLine("[BrowserPool] Encerrando pool de browsers...");

        // Fecha todas as sessões ativas
        foreach (var session in _activeSessions.Values)
        {
            await session.DisposeAsync();
        }
        _activeSessions.Clear();

        // Fecha todas as sessões disponíveis
        while (_availableSessions.TryTake(out var session))
        {
            await session.DisposeAsync();
        }

        _playwright?.Dispose();
        _poolSemaphore.Dispose();
        _initLock.Dispose();

        Console.WriteLine("[BrowserPool] Pool encerrado.");
    }
}

/// <summary>
/// Representa uma sessão de browser individual no pool.
/// </summary>
public class BrowserSession : IAsyncDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public IBrowser Browser { get; }
    public IPlaywright Playwright { get; }
    public string ProxyServer { get; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; private set; } = DateTime.UtcNow;
    public bool IsActive { get; private set; }

    private IAPIRequestContext? _apiContext;
    private readonly SemaphoreSlim _contextLock = new(1, 1);

    public BrowserSession(IBrowser browser, IPlaywright playwright, string proxyServer)
    {
        Browser = browser;
        Playwright = playwright;
        ProxyServer = proxyServer;
        IsActive = true;
    }

    /// <summary>
    /// Verifica se a sessão ainda é válida.
    /// </summary>
    public async Task<bool> IsValidAsync()
    {
        try
        {
            // Verifica se o browser ainda está conectado
            return Browser.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cria uma nova página no browser.
    /// </summary>
    public async Task<IPage> NewPageAsync()
    {
        LastUsedAt = DateTime.UtcNow;
        return await Browser.NewPageAsync();
    }

    /// <summary>
    /// Obtém ou cria um contexto de API com a chave especificada e proxy configurado.
    /// </summary>
    public async Task<IAPIRequestContext> GetOrCreateApiContextAsync(string apiKey, string baseUrl)
    {
        await _contextLock.WaitAsync();
        try
        {
            if (_apiContext != null)
            {
                await _apiContext.DisposeAsync();
            }

            _apiContext = await Playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
            {
                BaseURL = baseUrl,
                Proxy = new Proxy { Server = ProxyServer },
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["x-api-key"] = apiKey,
                    ["Accept"] = "application/json",
                    ["Content-Type"] = "application/json",
                    ["Origin"] = "https://campinas.sp.gov.br"
                }
            });

            return _apiContext;
        }
        finally
        {
            _contextLock.Release();
        }
    }

    /// <summary>
    /// Obtém o contexto de API atual (se existir).
    /// </summary>
    public IAPIRequestContext? GetApiContext() => _apiContext;

    public void MarkAsActive()
    {
        IsActive = true;
        LastUsedAt = DateTime.UtcNow;
    }

    public void MarkAsIdle()
    {
        IsActive = false;
        LastUsedAt = DateTime.UtcNow;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_apiContext != null)
            {
                await _apiContext.DisposeAsync();
            }
            await Browser.CloseAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BrowserSession] Erro ao fechar sessão {Id}: {ex.Message}");
        }
        finally
        {
            _contextLock.Dispose();
        }
    }
}

/// <summary>
/// Estatísticas do pool de browsers.
/// </summary>
public class PoolStats
{
    public int MaxConnections { get; set; }
    public int AvailableSessions { get; set; }
    public int ActiveSessions { get; set; }
    public int WaitingRequests { get; set; }
    public string? ProxyServer { get; set; }
}
