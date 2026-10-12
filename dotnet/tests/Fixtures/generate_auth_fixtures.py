# -*- coding: utf-8 -*-
"""
Gera tests/Fixtures/auth-fixtures.json com a implementação de REFERÊNCIA do Flask
(Werkzeug, PyJWT e cryptography), para provar a compatibilidade do .NET (spec 001, AC-001-26/27/28/34/50/51).

Uso (precisa de Werkzeug==3.1.3, PyJWT==2.8.0, cryptography==42.0.5):
    python generate_auth_fixtures.py

Todas as senhas, chaves e segredos abaixo são FICTÍCIOS (só para teste). A saída é versionada;
o CI não precisa de Python.
"""
import base64
import json
import os
from importlib.metadata import version

import jwt
from cryptography.fernet import Fernet
from werkzeug.security import generate_password_hash

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "auth-fixtures.json")

# --- Hashes de senha (Werkzeug) --------------------------------------------------------------
PASSWORDS = ["SenhaSegura123", "senha com espaço", "çãõ-ünicode-123", "abc123"]
password_hashes = []
for pwd in PASSWORDS:
    # método do /auth/register (iterações padrão do Werkzeug 3.1.3)
    password_hashes.append({"password": pwd, "hash": generate_password_hash(pwd, method="pbkdf2:sha256"),
                            "kind": "pbkdf2-default"})
    # iterações baixas: prova que o .NET lê as iterações da própria string
    password_hashes.append({"password": pwd, "hash": generate_password_hash(pwd, method="pbkdf2:sha256:1000"),
                            "kind": "pbkdf2-1000"})
    # padrão do Werkzeug 3 sem method (definir_senha_manual.py): scrypt:32768:8:1, o formato do usuário real
    password_hashes.append({"password": pwd, "hash": generate_password_hash(pwd),
                            "kind": "scrypt-default"})
password_hashes.append({"password": "abc123", "hash": generate_password_hash("abc123", method="scrypt:16384:8:1"),
                        "kind": "scrypt-16384"})

# --- Fernet ----------------------------------------------------------------------------------
fernet_key = base64.urlsafe_b64encode(bytes(range(32))).decode()  # chave de teste determinística
f = Fernet(fernet_key)
FIXED_TIME = 1_700_000_000
fernet_cases = []
for i, plain in enumerate(["abc", "x" * 43, "ação-é-ü-🔑", ""]):
    iv = bytes([(i * 16 + j) % 256 for j in range(16)])  # IV determinístico por caso
    token = f._encrypt_from_parts(plain.encode(), FIXED_TIME, iv).decode()
    assert f.decrypt(token.encode()).decode() == plain
    fernet_cases.append({"plaintext": plain, "token": token, "timestamp": FIXED_TIME})
# token com ciphertext adulterado (HMAC deve falhar)
tampered = bytearray(base64.urlsafe_b64decode(fernet_cases[0]["token"]))
tampered[30] ^= 0x01
fernet_tampered = base64.urlsafe_b64encode(bytes(tampered)).decode()

# --- JWT (PyJWT, HS256) ----------------------------------------------------------------------
SECRET = "chave-de-teste-com-mais-de-32-caracteres-0123456789"
FAR_FUTURE = 4_102_444_800  # 2100-01-01
PAST = 1_500_000_000        # 2017-07-14
IAT = 1_700_000_000

def enc(payload, key=SECRET, alg="HS256"):
    t = jwt.encode(payload, key, algorithm=alg)
    return t if isinstance(t, str) else t.decode()

jwts = {
    "valid": enc({"user_id": 42, "exp": FAR_FUTURE, "iat": IAT}),
    "expired": enc({"user_id": 42, "exp": PAST, "iat": PAST - 86400}),
    "no_user_id": enc({"exp": FAR_FUTURE, "iat": IAT}),
    "user_id_zero": enc({"user_id": 0, "exp": FAR_FUTURE, "iat": IAT}),
    "no_exp": enc({"user_id": 42, "iat": IAT}),
    "alg_none": enc({"user_id": 42, "exp": FAR_FUTURE, "iat": IAT}, key=None, alg="none"),
    "hs512": enc({"user_id": 42, "exp": FAR_FUTURE, "iat": IAT}, alg="HS512"),
    "wrong_secret": enc({"user_id": 42, "exp": FAR_FUTURE, "iat": IAT}, key="outra-chave-de-teste-diferente-0123456789ab"),
    "sub_only": enc({"sub": "42", "exp": FAR_FUTURE, "iat": IAT}),
}
valid_parts = jwts["valid"].split(".")
jwts["tampered_payload"] = ".".join([valid_parts[0], base64.urlsafe_b64encode(
    json.dumps({"user_id": 1, "exp": FAR_FUTURE, "iat": IAT}).encode()).decode().rstrip("="), valid_parts[2]])

data = {
    "generated_by": {
        "werkzeug": version("werkzeug"), "pyjwt": version("pyjwt"), "cryptography": version("cryptography"),
        "note": "valores fictícios; regenere com generate_auth_fixtures.py",
    },
    "passwords": password_hashes,
    "fernet": {"key": fernet_key, "cases": fernet_cases, "tampered_token": fernet_tampered},
    "jwt": {"secret": SECRET, "far_future_exp": FAR_FUTURE, "tokens": jwts, "user_id": 42},
}
with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    json.dump(data, fh, ensure_ascii=False, indent=2)
    fh.write("\n")
print("gerado:", OUT, "| hashes:", len(password_hashes), "| fernet:", len(fernet_cases), "| jwt:", len(jwts))
