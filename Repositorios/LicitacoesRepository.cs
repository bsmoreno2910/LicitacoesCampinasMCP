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
            $"/compras/{editalId}?include=unidade,situacao_compra,modalidade,amparo_legal,instrumento_convocatorio,modo_disputa,compra_historicos.usuario",
            cancellationToken);
        
        if (compraDoc == null)
            return null;

        var root = compraDoc.RootElement;
        
        var licitacao = new LicitacaoData
        {
            Id = JsonHelper.GetInt(root, "id"),
            PncpAnoCompra = JsonHelper.GetNullableInt(root, "pncp_ano_compra"),
            PncpTipoInstrumentoConvocatorioId = JsonHelper.GetNullableInt(root, "pncp_tipo_instrumento_convocatorio_id"),
            PncpModalidadeId = JsonHelper.GetNullableInt(root, "pncp_modalidade_id"),
            PncpModoDisputaId = JsonHelper.GetNullableInt(root, "pncp_modo_disputa_id"),
            PncpNumeroCompra = JsonHelper.GetString(root, "pncp_numero_compra"),
            PncpNumeroProcesso = JsonHelper.GetString(root, "pncp_numero_processo"),
            PncpObjetoCompra = JsonHelper.GetString(root, "pncp_objeto_compra"),
            PncpInformacaoComplementar = JsonHelper.GetString(root, "pncp_informacao_complementar"),
            PncpSrp = JsonHelper.GetNullableBool(root, "pncp_srp"),
            PncpOrcamentoSigiloso = JsonHelper.GetNullableBool(root, "pncp_orcamento_sigiloso"),
            PncpAmparoLegalId = JsonHelper.GetNullableInt(root, "pncp_amparo_legal_id"),
            PncpDataAberturaProposta = JsonHelper.GetString(root, "pncp_data_abertura_proposta"),
            PncpDataEncerramentoProposta = JsonHelper.GetString(root, "pncp_data_encerramento_proposta"),
            PncpCodigoUnidadeCompradora = JsonHelper.GetString(root, "pncp_codigo_unidade_compradora"),
            PncpLinkSistemaOrigem = JsonHelper.GetString(root, "pncp_link_sistema_origem"),
            OrgaoId = JsonHelper.GetNullableInt(root, "orgao_id"),
            Situacao = JsonHelper.GetString(root, "situacao"),
            MotivoReprova = JsonHelper.GetString(root, "motivo_reprova"),
            RetornoPncp = JsonHelper.GetString(root, "retorno_pncp"),
            PncpSequencialCompra = JsonHelper.GetNullableInt(root, "pncp_sequencial_compra"),
            PncpSituacaoCompraId = JsonHelper.GetNullableInt(root, "pncp_situacao_compra_id"),
            PncpJustificativa = JsonHelper.GetString(root, "pncp_justificativa"),
            CreatedAt = JsonHelper.GetString(root, "created_at"),
            UpdatedAt = JsonHelper.GetString(root, "updated_at"),
            PncpJustificativaPresencial = JsonHelper.GetString(root, "pncp_justificativa_presencial"),
            NumeroControlePncp = JsonHelper.GetString(root, "numero_controle_pncp"),
            Status = JsonHelper.GetString(root, "status"),
            DataExtracao = DateTime.UtcNow.ToString("o")
        };

        // Unidade
        if (root.TryGetProperty("unidade", out var unidade) && unidade.ValueKind != JsonValueKind.Null)
        {
            licitacao.Unidade = new UnidadeData
            {
                Id = JsonHelper.GetInt(unidade, "id"),
                PncpCodigoIBGE = JsonHelper.GetString(unidade, "pncp_codigo_IBGE"),
                PncpCodigoUnidade = JsonHelper.GetString(unidade, "pncp_codigo_unidade"),
                PncpNomeUnidade = JsonHelper.GetString(unidade, "pncp_nome_unidade"),
                OrgaoId = JsonHelper.GetNullableInt(unidade, "orgao_id"),
                CreatedAt = JsonHelper.GetString(unidade, "created_at"),
                UpdatedAt = JsonHelper.GetString(unidade, "updated_at")
            };
        }

        // Modalidade
        if (root.TryGetProperty("modalidade", out var modalidade) && modalidade.ValueKind != JsonValueKind.Null)
        {
            licitacao.Modalidade = MapDominio(modalidade);
        }

        // Amparo Legal
        if (root.TryGetProperty("amparo_legal", out var amparoLegal) && amparoLegal.ValueKind != JsonValueKind.Null)
        {
            licitacao.AmparoLegal = MapDominio(amparoLegal);
        }

        // Instrumento Convocatório
        if (root.TryGetProperty("instrumento_convocatorio", out var instrConv) && instrConv.ValueKind != JsonValueKind.Null)
        {
            licitacao.InstrumentoConvocatorio = MapDominio(instrConv);
        }

        // Modo de Disputa
        if (root.TryGetProperty("modo_disputa", out var modoDisputa) && modoDisputa.ValueKind != JsonValueKind.Null)
        {
            licitacao.ModoDisputa = MapDominio(modoDisputa);
        }

        // Situação da Compra
        if (root.TryGetProperty("situacao_compra", out var sitCompra) && sitCompra.ValueKind != JsonValueKind.Null)
        {
            licitacao.SituacaoCompra = MapDominio(sitCompra);
        }

        // Compra Históricos
        if (root.TryGetProperty("compra_historicos", out var historicos) && historicos.ValueKind == JsonValueKind.Array)
        {
            licitacao.CompraHistoricos = new List<CompraHistoricoData>();
            foreach (var hist in historicos.EnumerateArray())
            {
                var historicoData = new CompraHistoricoData
                {
                    Id = JsonHelper.GetInt(hist, "id"),
                    Descricao = JsonHelper.GetString(hist, "descricao"),
                    CreatedAt = JsonHelper.GetString(hist, "created_at"),
                    UpdatedAt = JsonHelper.GetString(hist, "updated_at"),
                    CompraId = JsonHelper.GetNullableInt(hist, "compra_id")
                };

                // Usuario
                if (hist.TryGetProperty("usuario", out var usuario) && usuario.ValueKind != JsonValueKind.Null)
                {
                    historicoData.Usuario = new UsuarioData
                    {
                        Id = JsonHelper.GetInt(usuario, "id"),
                        Nome = JsonHelper.GetString(usuario, "nome"),
                        Email = JsonHelper.GetString(usuario, "email")
                    };
                }

                licitacao.CompraHistoricos.Add(historicoData);
            }
        }

        // Busca itens
        await BuscarItensAsync(licitacao, editalId, cancellationToken);

        // Busca arquivos
        await BuscarArquivosAsync(licitacao, editalId, cancellationToken);

        return licitacao;
    }

    /// <summary>
    /// Mapeia um elemento JSON para DominioData com todos os campos.
    /// </summary>
    private DominioData MapDominio(JsonElement element)
    {
        return new DominioData
        {
            Id = JsonHelper.GetInt(element, "id"),
            RefDominio = JsonHelper.GetString(element, "ref_dominio"),
            DescricaoDominio = JsonHelper.GetString(element, "descricao_dominio"),
            ItemId = JsonHelper.GetString(element, "item_id"),
            ItemTitulo = JsonHelper.GetString(element, "item_titulo"),
            ItemDescricao = JsonHelper.GetString(element, "item_descricao"),
            Observacao = JsonHelper.GetString(element, "observacao"),
            Ativo = JsonHelper.GetNullableBool(element, "ativo"),
            CreatedAt = JsonHelper.GetString(element, "created_at"),
            UpdatedAt = JsonHelper.GetString(element, "updated_at")
        };
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
