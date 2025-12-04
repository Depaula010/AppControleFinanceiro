# app/services/servico_consentimento_lgpd.py
"""
Serviço de Compliance LGPD (Lei Geral de Proteção de Dados)
Implementa:
- Gestão de consentimentos
- Direito de portabilidade (export de dados)
- Direito ao esquecimento (deleção de conta)
"""

from datetime import datetime
from typing import Dict, Optional, List
from sqlalchemy import text
from app import db_engine
from app.services.encryption_service import encryption_service


class ServicoConsentimentoLGPD:
    """
    Serviço para gestão de consentimentos e direitos LGPD.
    """

    # Tipos de consentimento disponíveis
    TIPOS_CONSENTIMENTO = {
        'uso_dados_pessoais': 'Uso de dados pessoais para funcionamento do sistema',
        'armazenamento_chaves_api': 'Armazenamento de chaves de API criptografadas',
        'rastreamento_uso': 'Rastreamento de uso para billing e analytics',
        'comunicacao_whatsapp': 'Envio de notificações via WhatsApp',
        'compartilhamento_terceiros': 'Compartilhamento com APIs de terceiros (Gemini, Weather, etc)',
    }

    # Versão atual dos termos (incrementar quando houver mudanças)
    VERSAO_TERMOS_ATUAL = '1.0'

    @staticmethod
    def registrar_consentimento(
        usuario_id: int,
        tipo_consentimento: str,
        consentimento_dado: bool,
        versao_termos: Optional[str] = None
    ) -> Dict:
        """
        Registra ou atualiza consentimento do usuário.

        Args:
            usuario_id: ID do usuário
            tipo_consentimento: Tipo do consentimento (chave de TIPOS_CONSENTIMENTO)
            consentimento_dado: True se usuário consentiu, False se revogou
            versao_termos: Versão dos termos (usa atual se não fornecida)

        Returns:
            dict: {
                'status': 'sucesso',
                'consentimento_id': 123,
                'mensagem': 'Consentimento registrado'
            }

        Raises:
            ValueError: Se tipo de consentimento inválido
        """
        if tipo_consentimento not in ServicoConsentimentoLGPD.TIPOS_CONSENTIMENTO:
            raise ValueError(
                f"Tipo de consentimento inválido: {tipo_consentimento}. "
                f"Válidos: {list(ServicoConsentimentoLGPD.TIPOS_CONSENTIMENTO.keys())}"
            )

        versao = versao_termos or ServicoConsentimentoLGPD.VERSAO_TERMOS_ATUAL

        try:
            with db_engine.connect() as conn:
                # Inserir novo registro de consentimento
                query = text("""
                    INSERT INTO ConsentimentoUsuario (
                        usuario_id,
                        tipo_consentimento,
                        consentimento_dado,
                        versao_termos,
                        data_consentimento,
                        ip_endereco,
                        user_agent
                    ) VALUES (
                        :usuario_id,
                        :tipo_consentimento,
                        :consentimento_dado,
                        :versao_termos,
                        NOW(),
                        :ip_endereco,
                        :user_agent
                    )
                    RETURNING id
                """)

                result = conn.execute(query, {
                    'usuario_id': usuario_id,
                    'tipo_consentimento': tipo_consentimento,
                    'consentimento_dado': consentimento_dado,
                    'versao_termos': versao,
                    'ip_endereco': None,  # TODO: Capturar do request
                    'user_agent': None    # TODO: Capturar do request
                })
                conn.commit()

                consentimento_id = result.fetchone()[0]

                print(f"[LGPD] ✅ Consentimento registrado: Usuario {usuario_id} | "
                      f"Tipo: {tipo_consentimento} | Consentiu: {consentimento_dado}")

                return {
                    'status': 'sucesso',
                    'consentimento_id': consentimento_id,
                    'mensagem': 'Consentimento registrado com sucesso',
                    'tipo': tipo_consentimento,
                    'consentimento_dado': consentimento_dado
                }

        except Exception as e:
            print(f"[LGPD] ❌ Erro ao registrar consentimento: {e}")
            raise

    @staticmethod
    def verificar_consentimento(usuario_id: int, tipo_consentimento: str) -> bool:
        """
        Verifica se usuário possui consentimento ativo para determinado tipo.

        Args:
            usuario_id: ID do usuário
            tipo_consentimento: Tipo do consentimento

        Returns:
            bool: True se consentimento ativo, False caso contrário
        """
        try:
            with db_engine.connect() as conn:
                # Buscar último consentimento do tipo
                query = text("""
                    SELECT consentimento_dado
                    FROM ConsentimentoUsuario
                    WHERE usuario_id = :usuario_id
                      AND tipo_consentimento = :tipo_consentimento
                    ORDER BY data_consentimento DESC
                    LIMIT 1
                """)

                result = conn.execute(query, {
                    'usuario_id': usuario_id,
                    'tipo_consentimento': tipo_consentimento
                })

                row = result.fetchone()

                if row is None:
                    return False

                return row[0]  # consentimento_dado

        except Exception as e:
            print(f"[LGPD] ⚠️ Erro ao verificar consentimento: {e}")
            return False

    @staticmethod
    def listar_consentimentos(usuario_id: int) -> List[Dict]:
        """
        Lista histórico de consentimentos do usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            list: [
                {
                    'id': 123,
                    'tipo_consentimento': 'uso_dados_pessoais',
                    'descricao': 'Uso de dados pessoais...',
                    'consentimento_dado': True,
                    'versao_termos': '1.0',
                    'data_consentimento': '2025-12-04T10:30:00'
                },
                ...
            ]
        """
        try:
            with db_engine.connect() as conn:
                query = text("""
                    SELECT
                        id,
                        tipo_consentimento,
                        consentimento_dado,
                        versao_termos,
                        data_consentimento
                    FROM ConsentimentoUsuario
                    WHERE usuario_id = :usuario_id
                    ORDER BY data_consentimento DESC
                """)

                result = conn.execute(query, {'usuario_id': usuario_id})
                rows = result.fetchall()

                consentimentos = []
                for row in rows:
                    consentimentos.append({
                        'id': row[0],
                        'tipo_consentimento': row[1],
                        'descricao': ServicoConsentimentoLGPD.TIPOS_CONSENTIMENTO.get(
                            row[1],
                            'Descrição não disponível'
                        ),
                        'consentimento_dado': row[2],
                        'versao_termos': row[3],
                        'data_consentimento': row[4].isoformat() if row[4] else None
                    })

                return consentimentos

        except Exception as e:
            print(f"[LGPD] ❌ Erro ao listar consentimentos: {e}")
            return []

    @staticmethod
    def exportar_dados_usuario(usuario_id: int) -> Dict:
        """
        Exporta TODOS os dados do usuário (Direito de Portabilidade - Art. 18, V LGPD).

        Args:
            usuario_id: ID do usuário

        Returns:
            dict: {
                'usuario_id': 123,
                'data_exportacao': '2025-12-04T10:30:00',
                'dados': {
                    'chaves_api': [...],
                    'preferencias': [...],
                    'uso_mensal': [...],
                    'logs_acesso': [...],
                    'consentimentos': [...],
                    'assinatura': {...}
                }
            }
        """
        try:
            dados_exportados = {
                'usuario_id': usuario_id,
                'data_exportacao': datetime.now().isoformat(),
                'dados': {}
            }

            with db_engine.connect() as conn:
                # 1. Chaves de API (DESCRIPTOGRAFADAS - direito do usuário)
                query_chaves = text("""
                    SELECT
                        id,
                        provedor,
                        chave_api_criptografada,
                        ativo,
                        ultimo_uso_em,
                        criado_em,
                        atualizado_em
                    FROM ChavesApiUsuario
                    WHERE usuario_id = :usuario_id
                """)

                result = conn.execute(query_chaves, {'usuario_id': usuario_id})
                chaves = []
                for row in result.fetchall():
                    chave_descriptografada = encryption_service.decrypt(row[2])
                    chaves.append({
                        'id': row[0],
                        'provedor': row[1],
                        'chave_api': chave_descriptografada,  # DESCRIPTOGRAFADA
                        'ativo': row[3],
                        'ultimo_uso_em': row[4].isoformat() if row[4] else None,
                        'criado_em': row[5].isoformat() if row[5] else None,
                        'atualizado_em': row[6].isoformat() if row[6] else None
                    })
                dados_exportados['dados']['chaves_api'] = chaves

                # 2. Preferências de Chave
                query_prefs = text("""
                    SELECT
                        id,
                        provedor,
                        usar_chave_propria,
                        atualizado_em
                    FROM PreferenciasChaveApi
                    WHERE usuario_id = :usuario_id
                """)

                result = conn.execute(query_prefs, {'usuario_id': usuario_id})
                preferencias = []
                for row in result.fetchall():
                    preferencias.append({
                        'id': row[0],
                        'provedor': row[1],
                        'usar_chave_propria': row[2],
                        'atualizado_em': row[3].isoformat() if row[3] else None
                    })
                dados_exportados['dados']['preferencias'] = preferencias

                # 3. Rastreamento de Uso
                query_uso = text("""
                    SELECT
                        provedor,
                        tipo_chave,
                        quantidade_chamadas,
                        mes_ano
                    FROM RastreamentoUsoApi
                    WHERE usuario_id = :usuario_id
                    ORDER BY mes_ano DESC
                """)

                result = conn.execute(query_uso, {'usuario_id': usuario_id})
                uso_mensal = []
                for row in result.fetchall():
                    uso_mensal.append({
                        'provedor': row[0],
                        'tipo_chave': row[1],
                        'quantidade_chamadas': row[2],
                        'mes_ano': row[3]
                    })
                dados_exportados['dados']['uso_mensal'] = uso_mensal

                # 4. Logs de Acesso
                query_logs = text("""
                    SELECT
                        provedor,
                        tipo_chave,
                        sucesso,
                        mensagem_erro,
                        data_acesso
                    FROM LogAcessoChaveApi
                    WHERE usuario_id = :usuario_id
                    ORDER BY data_acesso DESC
                    LIMIT 1000
                """)

                result = conn.execute(query_logs, {'usuario_id': usuario_id})
                logs = []
                for row in result.fetchall():
                    logs.append({
                        'provedor': row[0],
                        'tipo_chave': row[1],
                        'sucesso': row[2],
                        'mensagem_erro': row[3],
                        'data_acesso': row[4].isoformat() if row[4] else None
                    })
                dados_exportados['dados']['logs_acesso'] = logs

                # 5. Consentimentos
                dados_exportados['dados']['consentimentos'] = ServicoConsentimentoLGPD.listar_consentimentos(usuario_id)

                # 6. Assinatura/Plano
                query_assinatura = text("""
                    SELECT
                        a.id,
                        p.nome,
                        p.preco_mensal,
                        p.limite_mensal_gemini,
                        p.limite_mensal_weather,
                        p.limite_mensal_openroute,
                        a.data_inicio,
                        a.data_fim,
                        a.ativo
                    FROM AssinaturasUsuario a
                    JOIN Planos p ON a.plano_id = p.id
                    WHERE a.usuario_id = :usuario_id
                    ORDER BY a.data_inicio DESC
                    LIMIT 1
                """)

                result = conn.execute(query_assinatura, {'usuario_id': usuario_id})
                row = result.fetchone()

                if row:
                    dados_exportados['dados']['assinatura'] = {
                        'id': row[0],
                        'plano': row[1],
                        'preco_mensal': float(row[2]) if row[2] else 0,
                        'limites': {
                            'gemini': row[3],
                            'weather': row[4],
                            'openroute': row[5]
                        },
                        'data_inicio': row[6].isoformat() if row[6] else None,
                        'data_fim': row[7].isoformat() if row[7] else None,
                        'ativo': row[8]
                    }
                else:
                    dados_exportados['dados']['assinatura'] = None

                print(f"[LGPD] ✅ Dados exportados para usuário {usuario_id}")
                print(f"[LGPD] - Chaves API: {len(chaves)}")
                print(f"[LGPD] - Preferências: {len(preferencias)}")
                print(f"[LGPD] - Registros de uso: {len(uso_mensal)}")
                print(f"[LGPD] - Logs de acesso: {len(logs)}")

                return dados_exportados

        except Exception as e:
            print(f"[LGPD] ❌ Erro ao exportar dados: {e}")
            raise

    @staticmethod
    def deletar_conta_usuario(usuario_id: int, confirmacao: str) -> Dict:
        """
        Deleta TODOS os dados do usuário (Direito ao Esquecimento - Art. 18, VI LGPD).

        ATENÇÃO: Esta operação é IRREVERSÍVEL!

        Args:
            usuario_id: ID do usuário
            confirmacao: Deve ser exatamente "CONFIRMO_DELECAO" para prosseguir

        Returns:
            dict: {
                'status': 'sucesso',
                'usuario_id': 123,
                'registros_deletados': {
                    'chaves_api': 3,
                    'preferencias': 3,
                    'logs': 150,
                    'uso': 12,
                    'consentimentos': 5,
                    'assinaturas': 1
                },
                'mensagem': 'Conta deletada com sucesso'
            }

        Raises:
            ValueError: Se confirmação inválida
        """
        if confirmacao != "CONFIRMO_DELECAO":
            raise ValueError(
                "Confirmação inválida. Para deletar a conta, você deve passar "
                "confirmacao='CONFIRMO_DELECAO' explicitamente."
            )

        try:
            registros_deletados = {}

            with db_engine.connect() as conn:
                # 1. Deletar Chaves de API
                query = text("DELETE FROM ChavesApiUsuario WHERE usuario_id = :usuario_id")
                result = conn.execute(query, {'usuario_id': usuario_id})
                registros_deletados['chaves_api'] = result.rowcount

                # 2. Deletar Preferências
                query = text("DELETE FROM PreferenciasChaveApi WHERE usuario_id = :usuario_id")
                result = conn.execute(query, {'usuario_id': usuario_id})
                registros_deletados['preferencias'] = result.rowcount

                # 3. Deletar Logs de Acesso
                query = text("DELETE FROM LogAcessoChaveApi WHERE usuario_id = :usuario_id")
                result = conn.execute(query, {'usuario_id': usuario_id})
                registros_deletados['logs'] = result.rowcount

                # 4. Deletar Rastreamento de Uso
                query = text("DELETE FROM RastreamentoUsoApi WHERE usuario_id = :usuario_id")
                result = conn.execute(query, {'usuario_id': usuario_id})
                registros_deletados['uso'] = result.rowcount

                # 5. Deletar Consentimentos
                query = text("DELETE FROM ConsentimentoUsuario WHERE usuario_id = :usuario_id")
                result = conn.execute(query, {'usuario_id': usuario_id})
                registros_deletados['consentimentos'] = result.rowcount

                # 6. Deletar Assinaturas
                query = text("DELETE FROM AssinaturasUsuario WHERE usuario_id = :usuario_id")
                result = conn.execute(query, {'usuario_id': usuario_id})
                registros_deletados['assinaturas'] = result.rowcount

                # TODO: Deletar outros dados relacionados ao usuário
                # (transações, eventos, etc.)

                conn.commit()

                total_registros = sum(registros_deletados.values())

                print(f"[LGPD] ⚠️ CONTA DELETADA: Usuario {usuario_id}")
                print(f"[LGPD] - Total de registros deletados: {total_registros}")
                for tabela, count in registros_deletados.items():
                    print(f"[LGPD]   • {tabela}: {count}")

                return {
                    'status': 'sucesso',
                    'usuario_id': usuario_id,
                    'registros_deletados': registros_deletados,
                    'total_registros': total_registros,
                    'mensagem': f'Conta e todos os dados do usuário {usuario_id} foram deletados com sucesso'
                }

        except Exception as e:
            print(f"[LGPD] ❌ Erro ao deletar conta: {e}")
            raise

    @staticmethod
    def solicitar_consentimentos_iniciais(usuario_id: int, ip_endereco: Optional[str] = None) -> Dict:
        """
        Solicita todos os consentimentos necessários para novo usuário.

        Args:
            usuario_id: ID do usuário
            ip_endereco: IP do usuário (opcional)

        Returns:
            dict: {
                'consentimentos_necessarios': [
                    {
                        'tipo': 'uso_dados_pessoais',
                        'descricao': '...',
                        'obrigatorio': True
                    },
                    ...
                ]
            }
        """
        consentimentos_necessarios = []

        for tipo, descricao in ServicoConsentimentoLGPD.TIPOS_CONSENTIMENTO.items():
            # Definir quais são obrigatórios
            obrigatorio = tipo in ['uso_dados_pessoais', 'armazenamento_chaves_api']

            consentimentos_necessarios.append({
                'tipo': tipo,
                'descricao': descricao,
                'obrigatorio': obrigatorio,
                'versao_termos': ServicoConsentimentoLGPD.VERSAO_TERMOS_ATUAL
            })

        return {
            'usuario_id': usuario_id,
            'consentimentos_necessarios': consentimentos_necessarios,
            'mensagem': 'Para continuar, você precisa consentir com os termos abaixo'
        }
