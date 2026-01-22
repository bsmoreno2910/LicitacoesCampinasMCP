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
    /// Busca os itens de uma licitação e seus resultados (valores homologados e fornecedor vencedor).
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
                    var itemId = JsonHelper.GetInt(item, "id");
                    var valorTotalEstimado = JsonHelper.GetDecimal(item, "pncp_valor_total");
                    
                    var itemData = new ItemData
                    {
                        Id = itemId,
                        NumeroItem = JsonHelper.GetInt(item, "pncp_numero_item"),
                        CodigoReduzido = JsonHelper.GetString(item, "codigo_reduzido"),
                        Descricao = JsonHelper.GetString(item, "pncp_descricao"),
                        Quantidade = JsonHelper.GetDecimal(item, "pncp_quantidade"),
                        UnidadeMedida = JsonHelper.GetString(item, "pncp_unidade_medida"),
                        ValorUnitarioEstimado = JsonHelper.GetDecimal(item, "pncp_valor_unitario_estimado"),
                        ValorTotalEstimado = valorTotalEstimado,
                        CompraId = JsonHelper.GetNullableInt(item, "compra_id") ?? int.Parse(editalId),
                        CreatedAt = JsonHelper.GetString(item, "created_at"),
                        UpdatedAt = JsonHelper.GetString(item, "updated_at")
                    };
                    
                    // Busca os resultados do item (valores homologados e fornecedor vencedor)
                    await BuscarResultadoItemAsync(itemData, editalId, itemId.ToString(), cancellationToken);
                    
                    licitacao.Itens.Add(itemData);
                    totalEstimado += valorTotalEstimado;
                    
                    // Soma valor homologado se disponível
                    if (itemData.ValorTotalHomologado.HasValue)
                    {
                        totalHomologado += itemData.ValorTotalHomologado.Value;
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
    /// Busca os resultados de um item (valores homologados e dados do fornecedor vencedor).
    /// </summary>
    private async Task BuscarResultadoItemAsync(ItemData itemData, string compraId, string itemId, CancellationToken cancellationToken)
    {
        try
        {
            var resultadoDoc = await _apiService.GetAsync(
                $"/compras/{compraId}/itens/{itemId}/resultados?page[number]=1&page[size]=10",
                cancellationToken);
            
            if (resultadoDoc != null && resultadoDoc.RootElement.TryGetProperty("data", out var resultadoData) && resultadoData.ValueKind == JsonValueKind.Array)
            {
                // Pega o primeiro resultado (fornecedor vencedor)
                foreach (var resultado in resultadoData.EnumerateArray())
                {
                    itemData.QuantidadeHomologada = JsonHelper.GetNullableDecimal(resultado, "pncp_quantidade_homologada");
                    itemData.ValorUnitarioHomologado = JsonHelper.GetNullableDecimal(resultado, "pncp_valor_unitario_homologado");
                    itemData.ValorTotalHomologado = JsonHelper.GetNullableDecimal(resultado, "pncp_valor_total_homologado");
                    itemData.PercentualDesconto = JsonHelper.GetNullableDecimal(resultado, "pncp_percentual_desconto");
                    itemData.DataResultado = JsonHelper.GetString(resultado, "pncp_data_resultado");
                    itemData.TipoFornecedor = JsonHelper.GetString(resultado, "pncp_tipo_pessoa_id");
                    itemData.CnpjCpfFornecedor = JsonHelper.GetString(resultado, "pncp_ni_fornecedor");
                    itemData.NomeFornecedor = JsonHelper.GetString(resultado, "pncp_nome_razao_social_fornecedor");
                    itemData.PorteFornecedorId = JsonHelper.GetNullableInt(resultado, "pncp_porte_fornecedor_id");
                    itemData.SituacaoItemResultadoId = JsonHelper.GetNullableInt(resultado, "pncp_situacao_compra_item_resultado_id");
                    
                    // Define situação do item baseado no resultado
                    if (itemData.ValorTotalHomologado.HasValue && itemData.ValorTotalHomologado > 0)
                    {
                        itemData.SituacaoItem = "Homologado";
                    }
                    
                    break; // Pega apenas o primeiro resultado (vencedor)
                }
            }
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"[LicitacoesRepository] Erro ao buscar resultado do item {itemId}: {ex.Message}"); 
        }
    }

    /// <summary>
    /// Busca os arquivos de uma licitação.
    /// </summary>
    private async Task BuscarArquivosAsync(LicitacaoData licitacao, string editalId, CancellationToken cancellationToken)
    {
        try
        {
            // Inicializa a lista de arquivos se ainda não existir
            licitacao.Arquivos ??= new List<ArquivoData>();
            
            var arqsDoc = await _apiService.GetAsync(
                $"/compras/{editalId}/arquivos?filter[acao][neq]=remover&sort=pncp_titulo_documento&page[number]=1&page[size]=1000",
                cancellationToken);
            
            if (arqsDoc != null && arqsDoc.RootElement.TryGetProperty("data", out var arqsData))
            {
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
                        Nome = JsonHelper.GetString(arq, "pncp_titulo_documento"),
                        Descricao = JsonHelper.GetString(arq, "pncp_titulo_documento"),
                        Tipo = JsonHelper.GetString(arq, "tipo"),
                        Tamanho = JsonHelper.GetNullableLong(arq, "tamanho"),
                        DownloadUrl = linkDownload,
                        LinkPncp = linkDownload,
                        CreatedAt = JsonHelper.GetString(arq, "created_at"),
                        UpdatedAt = JsonHelper.GetString(arq, "updated_at"),
                        Origem = "licitacao",
                        EmpenhoId = null,
                        NumeroEmpenho = null
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
    /// Busca os empenhos de uma licitação com a estrutura completa da API de Campinas.
    /// </summary>
    private async Task BuscarEmpenhosAsync(LicitacaoData licitacao, string editalId, CancellationToken cancellationToken)
    {
        try
        {
            // Inicializa a lista de arquivos se ainda não existir
            licitacao.Arquivos ??= new List<ArquivoData>();
            
            var empDoc = await _apiService.GetAsync(
                $"/compras/{editalId}/empenhos?page[number]=1&page[size]=1000",
                cancellationToken);
            
            if (empDoc != null && empDoc.RootElement.TryGetProperty("data", out var empData))
            {
                licitacao.Empenhos = new List<EmpenhoData>();
                foreach (var emp in empData.EnumerateArray())
                {
                    var empenhoId = JsonHelper.GetInt(emp, "id_empenho");
                    var numeroEmpenho = JsonHelper.GetString(emp, "numero_empenho");
                    
                    var empenho = new EmpenhoData
                    {
                        IdEmpenho = empenhoId,
                        AnoCompra = JsonHelper.GetNullableInt(emp, "ano_compra"),
                        NumeroProcesso = JsonHelper.GetString(emp, "numero_processo"),
                        NumeroEmpenho = numeroEmpenho,
                        TipoEmpenho = JsonHelper.GetString(emp, "tipo_empenho"),
                        Modalidade = JsonHelper.GetString(emp, "modalidade"),
                        AnoContrato = JsonHelper.GetNullableInt(emp, "ano_contrato"),
                        CategoriaProcessoId = JsonHelper.GetNullableInt(emp, "categoria_processo_id"),
                        CodigoUnidade = JsonHelper.GetNullableInt(emp, "codigo_unidade"),
                        TipoFornecedor = JsonHelper.GetString(emp, "tipo_fornecedor"),
                        CgcFornecedor = JsonHelper.GetString(emp, "cgc_fornecedor"),
                        NomeFornecedor = JsonHelper.GetString(emp, "nome_fornecedor"),
                        CodGestora = JsonHelper.GetString(emp, "cod_gestora"),
                        NomeGestora = JsonHelper.GetString(emp, "nome_gestora"),
                        CodUo = JsonHelper.GetString(emp, "cod_uo"),
                        NomeUo = JsonHelper.GetString(emp, "nome_uo"),
                        CodPrograma = JsonHelper.GetString(emp, "cod_programa"),
                        DescrPrograma = JsonHelper.GetString(emp, "descr_programa"),
                        CodDespesa = JsonHelper.GetString(emp, "cod_despesa"),
                        DescrDespesa = JsonHelper.GetString(emp, "descr_despesa"),
                        CodFonte = JsonHelper.GetString(emp, "cod_fonte"),
                        DescrFonte = JsonHelper.GetString(emp, "descr_fonte"),
                        ValorEmpenhado = JsonHelper.GetNullableDecimal(emp, "valor_empenhado"),
                        ValorReforco = JsonHelper.GetNullableDecimal(emp, "valor_reforco"),
                        ValorAnulacao = JsonHelper.GetNullableDecimal(emp, "valor_anulacao"),
                        ValorTotal = JsonHelper.GetNullableDecimal(emp, "valor_total"),
                        DataAssinatura = JsonHelper.GetString(emp, "data_assinatura"),
                        Objeto = JsonHelper.GetString(emp, "objeto")
                    };

                    // Mapeia os itens do empenho
                    if (emp.TryGetProperty("itens", out var itensEmp) && itensEmp.ValueKind == JsonValueKind.Array)
                    {
                        empenho.Itens = new List<EmpenhoItemData>();
                        foreach (var itemEmp in itensEmp.EnumerateArray())
                        {
                            empenho.Itens.Add(new EmpenhoItemData
                            {
                                NumeroItem = JsonHelper.GetInt(itemEmp, "numero_item"),
                                CodReduzido = JsonHelper.GetString(itemEmp, "cod_reduzido"),
                                DescricaoItem = JsonHelper.GetString(itemEmp, "descricao_item"),
                                Unidade = JsonHelper.GetString(itemEmp, "unidade"),
                                Quantidade = JsonHelper.GetNullableDecimal(itemEmp, "quantidade"),
                                ValorUnitario = JsonHelper.GetNullableDecimal(itemEmp, "valor_unitario"),
                                ValorTotal = JsonHelper.GetNullableDecimal(itemEmp, "valor_total")
                            });
                        }
                    }

                    // Busca arquivos do empenho (se houver endpoint específico)
                    // await BuscarArquivosEmpenhoAsync(licitacao, empenho, editalId, empenhoId.ToString(), numeroEmpenho, cancellationToken);

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
    /// Busca os arquivos de um empenho e adiciona ao array principal de arquivos da licitação.
    /// </summary>
    private async Task BuscarArquivosEmpenhoAsync(LicitacaoData licitacao, EmpenhoData empenho, string compraId, string empenhoId, string? numeroEmpenho, CancellationToken cancellationToken)
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
                    var nome = JsonHelper.GetString(arq, "nome");
                    var descricao = JsonHelper.GetString(arq, "descricao");
                    var tipo = JsonHelper.GetString(arq, "tipo");
                    var tamanho = JsonHelper.GetNullableLong(arq, "tamanho");
                    var createdAt = JsonHelper.GetString(arq, "created_at");
                    var updatedAt = JsonHelper.GetString(arq, "updated_at");
                    
                    // Gera o link de download através da nossa API
                    var linkDownload = $"{API_BASE_URL}/api/compra/{compraId}/empenho/{empenhoId}/arquivo/{arquivoId}/download";
                    
                    // Adiciona ao array de arquivos do empenho (para manter compatibilidade)
                    empenho.Arquivos.Add(new EmpenhoArquivoData
                    {
                        Id = arquivoId,
                        Nome = nome,
                        Descricao = descricao,
                        Tipo = tipo,
                        Tamanho = tamanho,
                        EmpenhoId = empenho.IdEmpenho,
                        CreatedAt = createdAt,
                        UpdatedAt = updatedAt,
                        DownloadUrl = linkDownload
                    });
                    
                    // Adiciona ao array principal de arquivos da licitação
                    licitacao.Arquivos.Add(new ArquivoData
                    {
                        Id = arquivoId,
                        TipoDocumento = 0, // Empenho não tem tipo_documento
                        Titulo = nome ?? descricao,
                        Nome = nome,
                        Descricao = descricao,
                        Tipo = tipo,
                        Tamanho = tamanho,
                        DownloadUrl = linkDownload,
                        LinkPncp = linkDownload,
                        CreatedAt = createdAt,
                        UpdatedAt = updatedAt,
                        Origem = "empenho",
                        EmpenhoId = empenho.IdEmpenho,
                        NumeroEmpenho = numeroEmpenho
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
