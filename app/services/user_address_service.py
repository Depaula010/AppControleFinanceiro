# app/services/user_address_service.py
from app import db
from app.services.travel_time_service import TravelTimeService
from sqlalchemy import text

class UserAddressService:
    """Gerencia endereços favoritos do usuário"""

    LABEL_EMOJIS = {
        'casa': '🏠',
        'trabalho': '💼',
        'outro': '➕'
    }

    LABEL_NAMES = {
        'casa': 'Casa',
        'trabalho': 'Trabalho',
        'outro': 'Outro'
    }

    @staticmethod
    def save_favorite_address(usuario_id, label, endereco_completo):
        """
        Salva/atualiza endereço favorito.

        Flow:
        1. Valida label (casa/trabalho/outro)
        2. Geocode via TravelTimeService
        3. Se falhar, retorna erro pedindo esclarecimento
        4. UPSERT em EnderecosFavoritos

        Args:
            usuario_id: ID do usuário
            label: 'casa', 'trabalho' ou 'outro'
            endereco_completo: Endereço completo

        Returns:
            tuple: (sucesso: bool, mensagem: str)
        """
        try:
            # Validar label
            label = label.lower()
            if label not in ['casa', 'trabalho', 'outro']:
                return False, f"❌ Label inválido: '{label}'. Use: casa, trabalho ou outro"

            # Geocodificar endereço
            print(f"[USER-ADDRESS] Salvando endereço '{label}' para usuário {usuario_id}")
            lat, lon, endereco_formatado = TravelTimeService.geocode_address(endereco_completo)

            if lat is None or lon is None:
                return False, (
                    f"❌ Não consegui localizar o endereço:\n"
                    f"'{endereco_completo}'\n\n"
                    f"Por favor, seja mais específico e inclua:\n"
                    f"• Rua e número\n"
                    f"• Bairro\n"
                    f"• Cidade e Estado\n\n"
                    f"Exemplo: Av Paulista 1000, Consolação, São Paulo-SP"
                )

            # UPSERT no banco
            # Se já existe, atualiza. Se não, insere.
            upsert_sql = text("""
                INSERT INTO EnderecosFavoritos (usuario_id, label, endereco_completo, latitude, longitude, updated_at)
                VALUES (:usuario_id, :label, :endereco, :lat, :lon, CURRENT_TIMESTAMP)
                ON CONFLICT (usuario_id, label)
                DO UPDATE SET
                    endereco_completo = EXCLUDED.endereco_completo,
                    latitude = EXCLUDED.latitude,
                    longitude = EXCLUDED.longitude,
                    updated_at = CURRENT_TIMESTAMP
            """)

            db.session.execute(upsert_sql, {
                'usuario_id': usuario_id,
                'label': label,
                'endereco': endereco_formatado or endereco_completo,
                'lat': lat,
                'lon': lon
            })
            db.session.commit()

            emoji = UserAddressService.LABEL_EMOJIS.get(label, '📍')
            label_nome = UserAddressService.LABEL_NAMES.get(label, label.capitalize())

            mensagem = (
                f"✅ Endereço *'{label_nome}'* salvo com sucesso!\n\n"
                f"{emoji} {endereco_formatado or endereco_completo}\n\n"
                f"Você pode usar este endereço ao calcular tempo de deslocamento."
            )

            print(f"[USER-ADDRESS] ✅ Endereço salvo: {label} -> {endereco_formatado}")
            return True, mensagem

        except Exception as e:
            print(f"[USER-ADDRESS] ERRO ao salvar endereço: {e}")
            db.session.rollback()
            return False, f"❌ Erro ao salvar endereço: {str(e)}"

    @staticmethod
    def get_user_addresses(usuario_id):
        """
        Retorna endereços favoritos do usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            list: [
                {'label': 'casa', 'endereco': 'Rua X, 123', 'lat': ..., 'lon': ...},
                ...
            ]
        """
        try:
            query_sql = text("""
                SELECT label, endereco_completo, latitude, longitude
                FROM EnderecosFavoritos
                WHERE usuario_id = :usuario_id
                ORDER BY
                    CASE label
                        WHEN 'casa' THEN 1
                        WHEN 'trabalho' THEN 2
                        WHEN 'outro' THEN 3
                    END
            """)

            result = db.session.execute(query_sql, {'usuario_id': usuario_id})
            rows = result.fetchall()

            addresses = []
            for row in rows:
                addresses.append({
                    'label': row[0],
                    'endereco': row[1],
                    'lat': float(row[2]) if row[2] else None,
                    'lon': float(row[3]) if row[3] else None
                })

            print(f"[USER-ADDRESS] Usuário {usuario_id} tem {len(addresses)} endereços")
            return addresses

        except Exception as e:
            print(f"[USER-ADDRESS] ERRO ao buscar endereços: {e}")
            return []

    @staticmethod
    def delete_address(usuario_id, label):
        """
        Remove endereço favorito.

        Args:
            usuario_id: ID do usuário
            label: 'casa', 'trabalho' ou 'outro'

        Returns:
            tuple: (sucesso: bool, mensagem: str)
        """
        try:
            label = label.lower()

            # Verificar se o endereço existe
            check_sql = text("""
                SELECT endereco_completo FROM EnderecosFavoritos
                WHERE usuario_id = :usuario_id AND label = :label
            """)

            result = db.session.execute(check_sql, {'usuario_id': usuario_id, 'label': label})
            row = result.fetchone()

            if not row:
                label_nome = UserAddressService.LABEL_NAMES.get(label, label.capitalize())
                return False, f"❌ Você não tem endereço '{label_nome}' cadastrado."

            # Deletar
            delete_sql = text("""
                DELETE FROM EnderecosFavoritos
                WHERE usuario_id = :usuario_id AND label = :label
            """)

            db.session.execute(delete_sql, {'usuario_id': usuario_id, 'label': label})
            db.session.commit()

            emoji = UserAddressService.LABEL_EMOJIS.get(label, '📍')
            label_nome = UserAddressService.LABEL_NAMES.get(label, label.capitalize())

            mensagem = f"✅ Endereço *'{label_nome}'* removido com sucesso! {emoji}"

            print(f"[USER-ADDRESS] ✅ Endereço deletado: {label}")
            return True, mensagem

        except Exception as e:
            print(f"[USER-ADDRESS] ERRO ao deletar endereço: {e}")
            db.session.rollback()
            return False, f"❌ Erro ao deletar endereço: {str(e)}"

    @staticmethod
    def format_address_list_message(usuario_id):
        """
        Formata mensagem WhatsApp com endereços configurados.

        Args:
            usuario_id: ID do usuário

        Returns:
            str: Mensagem formatada
        """
        addresses = UserAddressService.get_user_addresses(usuario_id)

        if not addresses:
            return (
                "📍 *Seus Endereços Favoritos*\n\n"
                "Você ainda não tem endereços cadastrados.\n\n"
                "Para adicionar um endereço, envie:\n"
                "*'Configurar endereço casa: [endereço completo]'*\n\n"
                "Exemplo:\n"
                "Configurar endereço casa: Av Paulista 1000, Consolação, São Paulo-SP"
            )

        msg = "📍 *Seus Endereços Favoritos*\n\n"

        for addr in addresses:
            label = addr['label']
            endereco = addr['endereco']
            emoji = UserAddressService.LABEL_EMOJIS.get(label, '📍')
            label_nome = UserAddressService.LABEL_NAMES.get(label, label.capitalize())

            msg += f"{emoji} *{label_nome}:* {endereco}\n"

        msg += "\n━━━━━━━━━━━━━━━━\n"
        msg += "\n*Para adicionar/alterar:*\n"
        msg += "Configurar endereço casa: [endereço]\n\n"
        msg += "*Para remover:*\n"
        msg += "Deletar endereço casa"

        return msg

    @staticmethod
    def get_address_by_label(usuario_id, label):
        """
        Busca endereço específico por label.

        Args:
            usuario_id: ID do usuário
            label: 'casa', 'trabalho' ou 'outro'

        Returns:
            dict ou None
        """
        addresses = UserAddressService.get_user_addresses(usuario_id)

        for addr in addresses:
            if addr['label'] == label.lower():
                return addr

        return None

    @staticmethod
    def has_any_address(usuario_id):
        """
        Verifica se usuário tem pelo menos um endereço cadastrado.

        Args:
            usuario_id: ID do usuário

        Returns:
            bool
        """
        addresses = UserAddressService.get_user_addresses(usuario_id)
        return len(addresses) > 0
