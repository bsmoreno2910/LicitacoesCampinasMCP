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
        builder.Services.AddSingleton<PlaywrightService>();
        builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "licitacoes-campinas", Version = "1.0.0" })
            .WithStdioServerTransport().WithToolsFromAssembly();
        await builder.Build().RunAsync();
    }

    static async Task RunAsHttpApi(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<PlaywrightService>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        var app = builder.Build();
        app.UseSwagger();
        app.UseSwaggerUI();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
        app.MapGet("/api/edital/{id}", async (string id, PlaywrightService ps) => {
            try { return Results.Content(await LicitacoesTools.BuscarEdital(ps, id), "application/json"); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });
        app.MapGet("/api/licitacoes", async (int? pagina, int? itens, PlaywrightService ps) => {
            try { return Results.Content(await LicitacoesTools.BuscarListaLicitacoes(ps, pagina ?? 1, itens ?? 100), "application/json"); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });
        app.MapPost("/api/edital/{id}/arquivo", async (string id, ArquivoRequest req, PlaywrightService ps) => {
            try { return Results.Content(await LicitacoesTools.BaixarArquivoEdital(ps, id, req.NomeArquivo, req.PastaDestino ?? "/tmp/downloads"), "application/json"); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        Console.WriteLine("API HTTP em http://0.0.0.0:8080");
        await app.RunAsync();
    }
}

public record ArquivoRequest(string NomeArquivo, string? PastaDestino);

public class PlaywrightService : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser != null) return _browser;
        await _semaphore.WaitAsync();
        try
        {
            if (_browser != null) return _browser;
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" }
            });
            return _browser;
        }
        finally { _semaphore.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}

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
    public static async Task<string> BuscarEdital(PlaywrightService ps, [Description("ID do edital (ex: 12043)")] string edital_id)
    {
        try
        {
            var browser = await ps.GetBrowserAsync();
            var page = await browser.NewPageAsync();
            try
            {
                await page.GotoAsync($"https://campinas.sp.gov.br/licitacoes/edital/{edital_id}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
                try
                {
                    await page.WaitForSelectorAsync("text=Processo:", new PageWaitForSelectorOptions { Timeout = 45000 });
                    await page.WaitForTimeoutAsync(3000);
                }
                catch { return JsonSerializer.Serialize(new { erro = "Edital não encontrado", id = edital_id }); }

                var lic = await ExtrairDadosLicitacao(page, edital_id);
                return JsonSerializer.Serialize(lic, new JsonSerializerOptions { WriteIndented = true });
            }
            finally { await page.CloseAsync(); }
        }
        catch (Exception ex) { return JsonSerializer.Serialize(new { erro = ex.Message, id = edital_id }); }
    }

    [McpServerTool, Description("Lista licitações com paginação.")]
    public static async Task<string> BuscarListaLicitacoes(PlaywrightService ps, [Description("Página (padrão: 1)")] int pagina = 1, [Description("Itens por página (padrão: 100)")] int itens_por_pagina = 100)
    {
        try
        {
            var browser = await ps.GetBrowserAsync();
            var page = await browser.NewPageAsync();
            try
            {
                await page.GotoAsync("https://campinas.sp.gov.br/licitacoes/home", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });

                try
                {
                    await page.WaitForFunctionAsync("() => !document.body.innerText.includes('Aguarde carregando')", new PageWaitForFunctionOptions { Timeout = 60000 });
                    await page.WaitForFunctionAsync("() => document.querySelectorAll('table tbody tr').length > 0 || document.body.innerText.includes('Nenhum registro')", new PageWaitForFunctionOptions { Timeout = 30000 });
                    await page.WaitForTimeoutAsync(3000);
                }
                catch (Exception ex) { return JsonSerializer.Serialize(new { erro = $"Timeout: {ex.Message}" }); }

                if (itens_por_pagina != 10)
                {
                    try
                    {
                        var sel = await page.QuerySelectorAsync("mat-select[aria-label*='Itens'], .mat-mdc-paginator-page-size-select");
                        if (sel != null)
                        {
                            await sel.ClickAsync();
                            await page.WaitForTimeoutAsync(500);
                            await page.ClickAsync($"mat-option:has-text('{itens_por_pagina}')");
                            await page.WaitForTimeoutAsync(3000);
                            await page.WaitForFunctionAsync("() => !document.body.innerText.includes('Aguarde carregando')", new PageWaitForFunctionOptions { Timeout = 30000 });
                        }
                    }
                    catch { }
                }

                for (int p = 1; p < pagina; p++)
                {
                    try
                    {
                        var next = await page.QuerySelectorAsync("button[aria-label*='Próxima'], button.mat-mdc-paginator-navigation-next");
                        if (next != null && await next.IsEnabledAsync())
                        {
                            await next.ClickAsync();
                            await page.WaitForTimeoutAsync(3000);
                            await page.WaitForFunctionAsync("() => !document.body.innerText.includes('Aguarde carregando')", new PageWaitForFunctionOptions { Timeout = 30000 });
                        }
                    }
                    catch { break; }
                }

                var lics = await ExtrairListaLicitacoes(page);
                return JsonSerializer.Serialize(new { pagina, itens_por_pagina, total = lics.Count, licitacoes = lics }, new JsonSerializerOptions { WriteIndented = true });
            }
            finally { await page.CloseAsync(); }
        }
        catch (Exception ex) { return JsonSerializer.Serialize(new { erro = ex.Message }); }
    }

    [McpServerTool, Description("Baixa arquivo de um edital.")]
    public static async Task<string> BaixarArquivoEdital(PlaywrightService ps, [Description("ID do edital")] string edital_id, [Description("Nome do arquivo")] string nome_arquivo, [Description("Pasta destino")] string pasta_destino = "./downloads")
    {
        try
        {
            Directory.CreateDirectory(pasta_destino);
            var browser = await ps.GetBrowserAsync();
            var page = await browser.NewPageAsync();
            try
            {
                await page.GotoAsync($"https://campinas.sp.gov.br/licitacoes/edital/{edital_id}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
                await page.WaitForSelectorAsync("text=Processo:", new PageWaitForSelectorOptions { Timeout = 45000 });
                await page.WaitForTimeoutAsync(2000);

                // Clica na aba Arquivos usando o seletor correto
                var aba = await page.QuerySelectorAsync("div[role='tab']:has-text('Arquivos')");
                if (aba != null) { await aba.ClickAsync(); await page.WaitForTimeoutAsync(3000); }

                var btn = await page.QuerySelectorAsync($"tr:has-text('{nome_arquivo}') button");
                if (btn != null)
                {
                    var dl = await page.RunAndWaitForDownloadAsync(async () => await btn.ClickAsync());
                    var path = Path.Combine(pasta_destino, dl.SuggestedFilename);
                    await dl.SaveAsAsync(path);
                    return JsonSerializer.Serialize(new { sucesso = true, arquivo = path });
                }
                return JsonSerializer.Serialize(new { erro = $"Arquivo '{nome_arquivo}' não encontrado" });
            }
            finally { await page.CloseAsync(); }
        }
        catch (Exception ex) { return JsonSerializer.Serialize(new { erro = ex.Message }); }
    }

    private static async Task<LicitacaoData> ExtrairDadosLicitacao(IPage page, string id)
    {
        var lic = new LicitacaoData { Id = id, DataExtracao = DateTime.UtcNow.ToString("o") };

        lic.Titulo = await ExtrairTexto(page, "h1, h2");
        lic.Tipo = await ExtrairCampo(page, "Tipo");
        lic.Processo = await ExtrairCampo(page, "Processo");
        lic.Secretaria = await ExtrairCampo(page, "Secretaria");
        lic.Modalidade = await ExtrairCampo(page, "Modalidade");
        lic.InstrumentoConvocatorio = await ExtrairCampo(page, "Instrumento convocatório");
        lic.ModoDisputa = await ExtrairCampo(page, "Modo de Disputa");
        lic.AmparoLegal = await ExtrairCampo(page, "Amparo legal");
        lic.LinkContratacao = await ExtrairCampo(page, "Link da Contratação");
        lic.Objeto = await ExtrairCampo(page, "Objeto");
        lic.InformacaoComplementar = await ExtrairCampo(page, "Informação complementar");
        lic.IdPncp = await ExtrairCampo(page, "ID PNCP");
        lic.NumeroControlePncp = await ExtrairCampo(page, "Número controle PNCP");
        lic.SituacaoCompra = await ExtrairCampo(page, "Situação compra");
        lic.StatusCompra = await ExtrairCampo(page, "Status da Compra");
        lic.DataInicioPropostas = await ExtrairCampo(page, "Data de início de recebimento de propostas");
        lic.DataFimPropostas = await ExtrairCampo(page, "Data fim de recebimento propostas");
        lic.UltimaAlteracao = await ExtrairCampo(page, "Última alteração");

        // Extrai valores usando seletor específico dos cards
        lic.ValorEstimado = await ExtrairValorCard(page, "Valor total estimado");
        lic.ValorHomologado = await ExtrairValorCard(page, "Valor total homologado");

        // Extrai arquivos
        lic.Arquivos = await ExtrairArquivos(page);
        
        // Extrai itens
        lic.Itens = await ExtrairItens(page);

        return lic;
    }

    private static async Task<string?> ExtrairTexto(IPage page, string sel)
    {
        try { var el = await page.QuerySelectorAsync(sel); return el != null ? (await el.InnerTextAsync())?.Trim() : null; } catch { return null; }
    }

    private static async Task<string?> ExtrairCampo(IPage page, string label)
    {
        try
        {
            var script = $@"(() => {{
                const text = document.body.innerText;
                const lines = text.split('\n');
                for (let i = 0; i < lines.length; i++) {{
                    const line = lines[i].trim();
                    if (line.toLowerCase().startsWith('{label.ToLower()}')) {{
                        const idx = line.indexOf(':');
                        if (idx > -1) return line.substring(idx + 1).trim();
                        if (i + 1 < lines.length) return lines[i + 1].trim();
                    }}
                }}
                return null;
            }})()";
            return await page.EvaluateAsync<string?>(script);
        }
        catch { return null; }
    }

    private static async Task<decimal> ExtrairValorCard(IPage page, string label)
    {
        try
        {
            // Busca o valor dentro do card específico
            var script = $@"(() => {{
                const text = document.body.innerText;
                const regex = new RegExp('{label}[\\s\\S]*?R\\$\\s*([\\d.,]+)', 'i');
                const match = text.match(regex);
                return match ? match[1].trim() : null;
            }})()";
            var valorStr = await page.EvaluateAsync<string?>(script);
            return ConverterValor(valorStr);
        }
        catch { return 0; }
    }

    private static decimal ConverterValor(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return 0;
        v = v.Replace("R$", "").Trim().Replace(".", "").Replace(",", ".");
        return decimal.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0;
    }

    private static async Task<List<ArquivoData>> ExtrairArquivos(IPage page)
    {
        try
        {
            // Clica na aba Arquivos usando o seletor correto do Angular Material
            var aba = await page.QuerySelectorAsync("div[role='tab']:has-text('Arquivos')");
            if (aba != null) 
            { 
                await aba.ClickAsync(); 
                await page.WaitForTimeoutAsync(3000);
                
                // Aguarda a tabela de arquivos aparecer
                try { await page.WaitForSelectorAsync("table", new PageWaitForSelectorOptions { Timeout = 5000 }); }
                catch { }
            }

            // Extrai arquivos da tabela que está visível na aba Arquivos
            var script = @"(() => {
                const arqs = [];
                // Pega a tabela dentro do painel de arquivos
                const tables = document.querySelectorAll('table');
                tables.forEach(t => {
                    const rows = t.querySelectorAll('tbody tr');
                    rows.forEach(r => {
                        const c = r.querySelectorAll('td');
                        if (c.length >= 3) {
                            const nome = c[0]?.innerText?.trim() || '';
                            const tipo = c[1]?.innerText?.trim() || '';
                            const data = c[2]?.innerText?.trim() || '';
                            // Ignora cabeçalhos e linhas vazias
                            if (nome && nome.length > 0 && !nome.toLowerCase().includes('nome') && nome !== 'Ações') {
                                arqs.push({ nome, tipo, data, url_download: '' });
                            }
                        }
                    });
                });
                return arqs;
            })()";
            
            var result = await page.EvaluateAsync<List<ArquivoData>>(script);
            return result ?? new List<ArquivoData>();
        }
        catch { return new List<ArquivoData>(); }
    }

    private static async Task<List<ItemData>> ExtrairItens(IPage page)
    {
        try
        {
            // Clica na aba Itens
            var aba = await page.QuerySelectorAsync("div[role='tab']:has-text('Itens')");
            if (aba != null) 
            { 
                await aba.ClickAsync(); 
                await page.WaitForTimeoutAsync(3000);
            }

            var script = @"(() => {
                const itens = [];
                const tables = document.querySelectorAll('table');
                tables.forEach(t => {
                    const rows = t.querySelectorAll('tbody tr');
                    rows.forEach(r => {
                        const c = r.querySelectorAll('td');
                        if (c.length >= 4) {
                            const num = c[0]?.innerText?.trim() || '';
                            if (/^\d+$/.test(num)) {
                                itens.push({ 
                                    numero: num, 
                                    codigo: c[1]?.innerText?.trim() || '', 
                                    descricao: c[2]?.innerText?.trim() || '', 
                                    quantidade: c[3]?.innerText?.trim() || '', 
                                    valor_unitario: 0, 
                                    valor_total: 0, 
                                    situacao: c.length > 6 ? c[6]?.innerText?.trim() || '' : '' 
                                });
                            }
                        }
                    });
                });
                return itens;
            })()";
            
            var result = await page.EvaluateAsync<List<ItemData>>(script);
            return result ?? new List<ItemData>();
        }
        catch { return new List<ItemData>(); }
    }

    private static async Task<List<LicitacaoResumo>> ExtrairListaLicitacoes(IPage page)
    {
        var script = @"(() => {
            const lics = [];
            const rows = document.querySelectorAll('table tbody tr');
            
            rows.forEach(r => {
                const c = r.querySelectorAll('td');
                
                // Tenta encontrar o ID de várias formas
                let id = '';
                
                // 1. Procura link com href contendo edital
                const links = r.querySelectorAll('a[href*=""edital""]');
                if (links.length > 0) {
                    const href = links[0].getAttribute('href') || '';
                    const match = href.match(/edital\/(\d+)/);
                    if (match) id = match[1];
                }
                
                // 2. Procura botão de visualização
                if (!id) {
                    const viewBtn = r.querySelector('button[mattooltip*=""isualizar""], button mat-icon');
                    if (viewBtn) {
                        // Tenta extrair do onclick ou data attribute
                        const onclick = viewBtn.getAttribute('onclick') || '';
                        const match = onclick.match(/(\d+)/);
                        if (match) id = match[1];
                    }
                }
                
                if (c.length >= 5) {
                    const mod = c[0]?.innerText?.trim() || '';
                    const proc = c[2]?.innerText?.trim() || '';
                    
                    if (mod && proc && !mod.toLowerCase().includes('modalidade')) {
                        lics.push({
                            id: id,
                            modalidade: mod,
                            numero: c[1]?.innerText?.trim() || '',
                            processo: proc,
                            unidade: c[3]?.innerText?.trim() || '',
                            objeto: c[4]?.innerText?.trim() || '',
                            status: c[5]?.innerText?.trim() || '',
                            url: id ? 'https://campinas.sp.gov.br/licitacoes/edital/' + id : ''
                        });
                    }
                }
            });
            
            return lics;
        })()";
        
        var result = await page.EvaluateAsync<List<LicitacaoResumo>>(script);
        return result ?? new List<LicitacaoResumo>();
    }
}
