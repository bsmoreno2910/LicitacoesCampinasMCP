using LicitacoesCampinasMCP.Dominio;
using LicitacoesCampinasMCP.Servicos;
using System.Text.Json;

namespace LicitacoesCampinasMCP.Repositorios;

/// <summary>
/// Repositório responsável por buscar dados de licitações da API de Campinas.
/// </summary>
public class LicitacoesRepository
{
    private readonly CampinasApiService _apiService;
    private const string API_BASE_URL = "https://srvs-mcp-licitacoes-campinas.vtc.dev.br";

    public LicitacoesRepository(CampinasApiService apiService)
    {
        _apiService = apiService;
    }

    /// <summary>
    /// Busca os detalhes completos de uma licitação pelo ID.
    /// </summary>
    public async Task<LicitacaoData?> BuscarPorIdAsync(string editalId, CancellationToken cancellationToken = default)
    {
        var compraDoc = await _apiService.GetAsync(
            $"/compras/{editalId}?include=unidade,situacao_compra,modalidade,amparo_legal,instrumento_convocatorio,modo_disputa",
            cancellationToken);
        
        if (compraDoc == null)
            return null;

        var root = compraDoc.RootElement;
        
        var licitacao = new LicitacaoData
        {
            Id = JsonHelper.GetInt(root, "id"),
            NumeroCompra = JsonHelper.GetString(root, "pncp_numero_compra"),
            Processo = JsonHelper.GetString(root, "pncp_numero_processo"),
            Objeto = JsonHelper.GetString(root, "pncp_objeto_compra"),
            InformacaoComplementar = JsonHelper.GetString(root, "pncp_informacao_complementar"),
            DataAberturaProposta = JsonHelper.GetString(root, "pncp_data_abertura_proposta"),
            DataEncerramentoProposta = JsonHelper.GetString(root, "pncp_data_encerramento_proposta"),
            LinkSistemaOrigem = JsonHelper.GetString(root, "pncp_link_sistema_origem"),
            SequencialCompra = JsonHelper.GetInt(root, "pncp_sequencial_compra"),
            NumeroControlePncp = JsonHelper.GetString(root, "numero_controle_pncp"),
            Status = JsonHelper.GetString(root, "status"),
            UpdatedAt = JsonHelper.GetString(root, "updated_at"),
            DataExtracao = DateTime.UtcNow.ToString("o")
        };

        // Unidade
        if (root.TryGetProperty("unidade", out var unidade))
        {
            licitacao.Unidade = new UnidadeData
            {
                Id = JsonHelper.GetInt(unidade, "id"),
                Codigo = JsonHelper.GetString(unidade, "pncp_codigo_unidade"),
                Nome = JsonHelper.GetString(unidade, "pncp_nome_unidade")
            };
        }

        // Modalidade
        if (root.TryGetProperty("modalidade", out var modalidade))
        {
            licitacao.Modalidade = new DominioData
            {
                Id = JsonHelper.GetInt(modalidade, "id"),
                Titulo = JsonHelper.GetString(modalidade, "item_titulo")
            };
        }

        // Amparo Legal
        if (root.TryGetProperty("amparo_legal", out var amparoLegal))
        {
            licitacao.AmparoLegal = new DominioData
            {
                Id = JsonHelper.GetInt(amparoLegal, "id"),
                Titulo = JsonHelper.GetString(amparoLegal, "item_titulo")
            };
        }

        // Instrumento Convocatório
        if (root.TryGetProperty("instrumento_convocatorio", out var instrConv))
        {
            licitacao.InstrumentoConvocatorio = new DominioData
            {
                Id = JsonHelper.GetInt(instrConv, "id"),
                Titulo = JsonHelper.GetString(instrConv, "item_titulo")
            };
        }

        // Modo de Disputa
        if (root.TryGetProperty("modo_disputa", out var modoDisputa))
        {
            licitacao.ModoDisputa = new DominioData
            {
                Id = JsonHelper.GetInt(modoDisputa, "id"),
                Titulo = JsonHelper.GetString(modoDisputa, "item_titulo")
            };
        }

        // Situação da Compra
        if (root.TryGetProperty("situacao_compra", out var sitCompra))
        {
            licitacao.SituacaoCompra = new DominioData
            {
                Id = JsonHelper.GetInt(sitCompra, "id"),
                Titulo = JsonHelper.GetString(sitCompra, "item_titulo")
            };
        }

        // Busca itens
        await BuscarItensAsync(licitacao, editalId, cancellationToken);

        // Busca arquivos
        await BuscarArquivosAsync(licitacao, editalId, cancellationToken);

        return licitacao;
    }

    /// <summary>
    /// Busca os itens de uma licitação.
    /// </summary>
    private async Task BuscarItensAsync(LicitacaoData licitacao, string editalId, CancellationToken cancellationToken)
    {
        try
        {
            var itensDoc = await _apiService.GetAsync(
                $"/compras/{editalId}/itens/?page[number]=1&page[size]=1000&sort=pncp_numero_item",
                cancellationToken);
            
            if (itensDoc != null && itensDoc.RootElement.TryGetProperty("data", out var itensData))
            {
                licitacao.Itens = new List<ItemData>();
                decimal totalEstimado = 0;
                
                foreach (var item in itensData.EnumerateArray())
                {
                    var itemData = new ItemData
                    {
                        Id = JsonHelper.GetInt(item, "id"),
                        NumeroItem = JsonHelper.GetInt(item, "pncp_numero_item"),
                        CodigoReduzido = JsonHelper.GetString(item, "codigo_reduzido"),
                        Descricao = JsonHelper.GetString(item, "pncp_descricao"),
                        Quantidade = JsonHelper.GetDecimal(item, "pncp_quantidade"),
                        UnidadeMedida = JsonHelper.GetString(item, "pncp_unidade_medida"),
                        ValorUnitarioEstimado = JsonHelper.GetDecimal(item, "pncp_valor_unitario_estimado"),
                        ValorTotal = JsonHelper.GetDecimal(item, "pncp_valor_total")
                    };
                    licitacao.Itens.Add(itemData);
                    totalEstimado += itemData.ValorTotal;
                }
                
                licitacao.ValorTotalEstimado = totalEstimado;
            }
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"[LicitacoesRepository] Erro ao buscar itens: {ex.Message}"); 
        }
    }

    /// <summary>
    /// Busca os arquivos de uma licitação.
    /// </summary>
    private async Task BuscarArquivosAsync(LicitacaoData licitacao, string editalId, CancellationToken cancellationToken)
    {
        try
        {
            var arqsDoc = await _apiService.GetAsync(
                $"/compras/{editalId}/arquivos?filter[acao][neq]=remover&sort=pncp_titulo_documento&page[number]=1&page[size]=1000",
                cancellationToken);
            
            if (arqsDoc != null && arqsDoc.RootElement.TryGetProperty("data", out var arqsData))
            {
                licitacao.Arquivos = new List<ArquivoData>();
                foreach (var arq in arqsData.EnumerateArray())
                {
                    var arquivoId = JsonHelper.GetInt(arq, "id");
                    // Gera o link de download através da nossa API
                    var linkDownload = $"{API_BASE_URL}/api/compra/{editalId}/arquivo/{arquivoId}/download";
                    
                    licitacao.Arquivos.Add(new ArquivoData
                    {
                        Id = arquivoId,
                        TipoDocumento = JsonHelper.GetInt(arq, "pncp_tipo_documento"),
                        Titulo = JsonHelper.GetString(arq, "pncp_titulo_documento"),
                        LinkPncp = linkDownload,
                        CreatedAt = JsonHelper.GetString(arq, "created_at")
                    });
                }
            }
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"[LicitacoesRepository] Erro ao buscar arquivos: {ex.Message}"); 
        }
    }

    /// <summary>
    /// Lista licitações com paginação.
    /// </summary>
    public async Task<ListaLicitacoesResponse> ListarAsync(int pagina = 1, int itensPorPagina = 100, CancellationToken cancellationToken = default)
    {
        var doc = await _apiService.GetAsync(
            $"/compras?page[number]={pagina}&page[size]={itensPorPagina}&sort=-id&include=objeto,unidade,modalidade,situacao_compra",
            cancellationToken);
        
        var response = new ListaLicitacoesResponse
        {
            Pagina = pagina,
            ItensPorPagina = itensPorPagina
        };

        if (doc == null)
            return response;
             
        var root = doc.RootElement;
        
        if (root.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = JsonHelper.GetInt(item, "id");
                
                var lic = new LicitacaoResumo
                {
                    Id = id,
                    NumeroCompra = JsonHelper.GetString(item, "pncp_numero_compra"),
                    Processo = JsonHelper.GetString(item, "pncp_numero_processo"),
                    Objeto = JsonHelper.GetString(item, "pncp_objeto_compra"),
                    Status = JsonHelper.GetString(item, "status"),
                    Url = $"https://campinas.sp.gov.br/licitacoes/edital/{id}"
                };

                // Relacionamentos inline
                if (item.TryGetProperty("modalidade", out var mod))
                    lic.Modalidade = JsonHelper.GetString(mod, "item_titulo");
                
                if (item.TryGetProperty("unidade", out var unid))
                    lic.Unidade = JsonHelper.GetString(unid, "pncp_nome_unidade");
                
                if (item.TryGetProperty("situacao_compra", out var sit))
                    lic.Status = JsonHelper.GetString(sit, "item_titulo");

                response.Licitacoes.Add(lic);
            }
        }

        if (root.TryGetProperty("meta", out var meta))
        {
            response.Total = JsonHelper.GetInt(meta, "total_count");
            response.ProximaPagina = JsonHelper.GetNullableInt(meta, "next_page");
        }

        return response;
    }

    /// <summary>
    /// Busca licitações por filtro (processo ou objeto).
    /// </summary>
    public async Task<BuscaLicitacoesResponse> BuscarPorFiltroAsync(string? processo = null, string? objeto = null, CancellationToken cancellationToken = default)
    {
        var response = new BuscaLicitacoesResponse
        {
            Filtros = new FiltrosBusca { Processo = processo, Objeto = objeto }
        };

        // Monta os filtros
        var filtros = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(processo))
        {
            filtros.Add($"filter[termo]={Uri.EscapeDataString(processo)}");
        }
        
        if (filtros.Count == 0)
        {
            return response;
        }
        
        var queryString = string.Join("&", filtros);
        var doc = await _apiService.GetAsync(
            $"/compras?{queryString}&page[number]=1&page[size]=100&sort=-id",
            cancellationToken);
        
        if (doc == null)
            return response;

        var root = doc.RootElement;
        
        if (root.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = JsonHelper.GetInt(item, "id");
                
                var lic = new LicitacaoResumo
                {
                    Id = id,
                    NumeroCompra = JsonHelper.GetString(item, "pncp_numero_compra"),
                    Processo = JsonHelper.GetString(item, "pncp_numero_processo"),
                    Objeto = JsonHelper.GetString(item, "pncp_objeto_compra"),
                    Status = JsonHelper.GetString(item, "status"),
                    Url = $"https://campinas.sp.gov.br/licitacoes/edital/{id}"
                };

                if (item.TryGetProperty("modalidade", out var mod))
                    lic.Modalidade = JsonHelper.GetString(mod, "item_titulo");
                
                if (item.TryGetProperty("unidade", out var unid))
                    lic.Unidade = JsonHelper.GetString(unid, "pncp_nome_unidade");
                
                if (item.TryGetProperty("situacao_compra", out var sit))
                    lic.Status = JsonHelper.GetString(sit, "item_titulo");

                response.Licitacoes.Add(lic);
            }
        }

        if (root.TryGetProperty("meta", out var meta))
        {
            response.Total = JsonHelper.GetInt(meta, "total_count");
        }

        return response;
    }
}
