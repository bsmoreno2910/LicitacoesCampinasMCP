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
            licitacao.Unidade = MapUnidade(unidade);
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

        // Busca empenhos
        await BuscarEmpenhosAsync(licitacao, editalId, cancellationToken);

        return licitacao;
    }

    /// <summary>
    /// Mapeia um elemento JSON para UnidadeData com todos os campos.
    /// </summary>
    private UnidadeData MapUnidade(JsonElement element)
    {
        return new UnidadeData
        {
            Id = JsonHelper.GetInt(element, "id"),
            PncpCodigoIBGE = JsonHelper.GetString(element, "pncp_codigo_IBGE"),
            PncpCodigoUnidade = JsonHelper.GetString(element, "pncp_codigo_unidade"),
            PncpNomeUnidade = JsonHelper.GetString(element, "pncp_nome_unidade"),
            OrgaoId = JsonHelper.GetNullableInt(element, "orgao_id"),
            CreatedAt = JsonHelper.GetString(element, "created_at"),
            UpdatedAt = JsonHelper.GetString(element, "updated_at")
        };
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
    /// Mapeia um elemento JSON para LicitacaoResumo com todos os campos.
    /// </summary>
    private LicitacaoResumo MapLicitacaoResumo(JsonElement item)
    {
        var id = JsonHelper.GetInt(item, "id");
        
        var lic = new LicitacaoResumo
        {
            Id = id,
            PncpAnoCompra = JsonHelper.GetNullableInt(item, "pncp_ano_compra"),
            PncpNumeroCompra = JsonHelper.GetString(item, "pncp_numero_compra"),
            PncpNumeroProcesso = JsonHelper.GetString(item, "pncp_numero_processo"),
            PncpObjetoCompra = JsonHelper.GetString(item, "pncp_objeto_compra"),
            PncpSrp = JsonHelper.GetNullableBool(item, "pncp_srp"),
            PncpDataAberturaProposta = JsonHelper.GetString(item, "pncp_data_abertura_proposta"),
            PncpDataEncerramentoProposta = JsonHelper.GetString(item, "pncp_data_encerramento_proposta"),
            PncpCodigoUnidadeCompradora = JsonHelper.GetString(item, "pncp_codigo_unidade_compradora"),
            OrgaoId = JsonHelper.GetNullableInt(item, "orgao_id"),
            Situacao = JsonHelper.GetString(item, "situacao"),
            Status = JsonHelper.GetString(item, "status"),
            CreatedAt = JsonHelper.GetString(item, "created_at"),
            UpdatedAt = JsonHelper.GetString(item, "updated_at"),
            NumeroControlePncp = JsonHelper.GetString(item, "numero_controle_pncp"),
            Url = $"https://campinas.sp.gov.br/licitacoes/edital/{id}"
        };

        // Unidade
        if (item.TryGetProperty("unidade", out var unidade) && unidade.ValueKind != JsonValueKind.Null)
        {
            lic.Unidade = MapUnidade(unidade);
        }

        // Modalidade
        if (item.TryGetProperty("modalidade", out var modalidade) && modalidade.ValueKind != JsonValueKind.Null)
        {
            lic.Modalidade = MapDominio(modalidade);
        }

        // Situação da Compra
        if (item.TryGetProperty("situacao_compra", out var sitCompra) && sitCompra.ValueKind != JsonValueKind.Null)
        {
            lic.SituacaoCompra = MapDominio(sitCompra);
        }

        // Amparo Legal
        if (item.TryGetProperty("amparo_legal", out var amparoLegal) && amparoLegal.ValueKind != JsonValueKind.Null)
        {
            lic.AmparoLegal = MapDominio(amparoLegal);
        }

        // Instrumento Convocatório
        if (item.TryGetProperty("instrumento_convocatorio", out var instrConv) && instrConv.ValueKind != JsonValueKind.Null)
        {
            lic.InstrumentoConvocatorio = MapDominio(instrConv);
        }

        // Modo de Disputa
        if (item.TryGetProperty("modo_disputa", out var modoDisputa) && modoDisputa.ValueKind != JsonValueKind.Null)
        {
            lic.ModoDisputa = MapDominio(modoDisputa);
        }

        return lic;
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
                decimal totalHomologado = 0;
                
                foreach (var item in itensData.EnumerateArray())
                {
                    var valorTotalEstimado = JsonHelper.GetDecimal(item, "pncp_valor_total");
                    var valorTotalHomologado = JsonHelper.GetNullableDecimal(item, "valor_total_homologado");
                    var situacaoItem = JsonHelper.GetString(item, "situacao_item");
                    
                    var itemData = new ItemData
                    {
                        Id = JsonHelper.GetInt(item, "id"),
                        NumeroItem = JsonHelper.GetInt(item, "pncp_numero_item"),
                        CodigoReduzido = JsonHelper.GetString(item, "codigo_reduzido"),
                        Descricao = JsonHelper.GetString(item, "pncp_descricao"),
                        Quantidade = JsonHelper.GetDecimal(item, "pncp_quantidade"),
                        UnidadeMedida = JsonHelper.GetString(item, "pncp_unidade_medida"),
                        ValorUnitarioEstimado = JsonHelper.GetDecimal(item, "pncp_valor_unitario_estimado"),
                        ValorTotalEstimado = valorTotalEstimado,
                        ValorUnitarioHomologado = JsonHelper.GetNullableDecimal(item, "valor_unitario_homologado"),
                        ValorTotalHomologado = valorTotalHomologado,
                        SituacaoItem = situacaoItem,
                        CompraId = JsonHelper.GetNullableInt(item, "compra_id"),
                        CreatedAt = JsonHelper.GetString(item, "created_at"),
                        UpdatedAt = JsonHelper.GetString(item, "updated_at")
                    };
                    licitacao.Itens.Add(itemData);
                    totalEstimado += valorTotalEstimado;
                    
                    // Soma valor homologado se o item estiver homologado
                    if (valorTotalHomologado.HasValue)
                    {
                        totalHomologado += valorTotalHomologado.Value;
                    }
                    else if (situacaoItem?.ToLower() == "homologado")
                    {
                        // Se não tem valor homologado mas está homologado, usa o valor estimado
                        totalHomologado += valorTotalEstimado;
                    }
                }
                
                licitacao.ValorTotalEstimado = totalEstimado;
                licitacao.ValorTotalHomologado = totalHomologado;
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
            $"/compras?page[number]={pagina}&page[size]={itensPorPagina}&sort=-id&include=unidade,modalidade,situacao_compra,amparo_legal,instrumento_convocatorio,modo_disputa",
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
                response.Licitacoes.Add(MapLicitacaoResumo(item));
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
    /// Retorna dados completos de cada licitação (como a busca por ID).
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
        
        // Coleta os IDs encontrados
        var ids = new List<int>();
        if (root.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                ids.Add(JsonHelper.GetInt(item, "id"));
            }
        }

        if (root.TryGetProperty("meta", out var meta))
        {
            response.Total = JsonHelper.GetInt(meta, "total_count");
        }

        // Busca os dados completos de cada licitação
        foreach (var id in ids)
        {
            var licitacao = await BuscarPorIdAsync(id.ToString(), cancellationToken);
            if (licitacao != null)
            {
                response.Licitacoes.Add(licitacao);
            }
        }

        return response;
    }

    /// <summary>
    /// Busca os empenhos de uma licitação.
    /// </summary>
    private async Task BuscarEmpenhosAsync(LicitacaoData licitacao, string editalId, CancellationToken cancellationToken)
    {
        try
        {
            var empDoc = await _apiService.GetAsync(
                $"/compras/{editalId}/empenhos?page[number]=1&page[size]=1000",
                cancellationToken);
            
            if (empDoc != null && empDoc.RootElement.TryGetProperty("data", out var empData))
            {
                licitacao.Empenhos = new List<EmpenhoData>();
                foreach (var emp in empData.EnumerateArray())
                {
                    var empenhoId = JsonHelper.GetInt(emp, "id");
                    var empenho = new EmpenhoData
                    {
                        Id = empenhoId,
                        Ano = JsonHelper.GetNullableInt(emp, "ano"),
                        Data = JsonHelper.GetString(emp, "data"),
                        NumeroEmpenho = JsonHelper.GetString(emp, "numero_empenho"),
                        CpfCnpjFornecedor = JsonHelper.GetString(emp, "cpf_cnpj_fornecedor"),
                        Fornecedor = JsonHelper.GetString(emp, "fornecedor"),
                        ValorGlobal = JsonHelper.GetNullableDecimal(emp, "valor_global"),
                        Objeto = JsonHelper.GetString(emp, "objeto"),
                        CompraId = JsonHelper.GetNullableInt(emp, "compra_id"),
                        CreatedAt = JsonHelper.GetString(emp, "created_at"),
                        UpdatedAt = JsonHelper.GetString(emp, "updated_at")
                    };

                    // Busca arquivos do empenho
                    await BuscarArquivosEmpenhoAsync(empenho, editalId, empenhoId.ToString(), cancellationToken);

                    licitacao.Empenhos.Add(empenho);
                }
            }
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"[LicitacoesRepository] Erro ao buscar empenhos: {ex.Message}"); 
        }
    }

    /// <summary>
    /// Busca os arquivos de um empenho específico.
    /// </summary>
    private async Task BuscarArquivosEmpenhoAsync(EmpenhoData empenho, string compraId, string empenhoId, CancellationToken cancellationToken)
    {
        try
        {
            var arqDoc = await _apiService.GetAsync(
                $"/compras/{compraId}/empenhos/{empenhoId}?include=arquivos",
                cancellationToken);
            
            if (arqDoc != null && arqDoc.RootElement.TryGetProperty("arquivos", out var arqData) && arqData.ValueKind == JsonValueKind.Array)
            {
                empenho.Arquivos = new List<EmpenhoArquivoData>();
                foreach (var arq in arqData.EnumerateArray())
                {
                    var arquivoId = JsonHelper.GetInt(arq, "id");
                    // Gera o link de download através da nossa API
                    var linkDownload = $"{API_BASE_URL}/api/compra/{compraId}/empenho/{empenhoId}/arquivo/{arquivoId}/download";
                    
                    empenho.Arquivos.Add(new EmpenhoArquivoData
                    {
                        Id = arquivoId,
                        Nome = JsonHelper.GetString(arq, "nome"),
                        Descricao = JsonHelper.GetString(arq, "descricao"),
                        Tipo = JsonHelper.GetString(arq, "tipo"),
                        Tamanho = JsonHelper.GetNullableLong(arq, "tamanho"),
                        EmpenhoId = JsonHelper.GetNullableInt(arq, "empenho_id"),
                        CreatedAt = JsonHelper.GetString(arq, "created_at"),
                        UpdatedAt = JsonHelper.GetString(arq, "updated_at"),
                        DownloadUrl = linkDownload
                    });
                }
            }
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"[LicitacoesRepository] Erro ao buscar arquivos do empenho: {ex.Message}"); 
        }
    }
}
