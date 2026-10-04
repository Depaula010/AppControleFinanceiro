import json, sys

# Reproduz o que app/middleware/security.py + redis_service.set_with_ttl gravam: json.dumps(json.dumps(obj)).
blacklist = {"ip": "192.0.2.10", "reason": "Manual block via API", "blacklisted_at": "2026-10-04T10:00:00.123456", "permanent": True}
blocked = {"blocked_until": "2026-10-04T11:00:00.123456", "count": 2, "first_blocked": "2026-10-04T10:00:00.123456"}
attempts = [
    {"timestamp": "2026-10-04T10:05:00.123456", "reason": "Unknown endpoint: /x"},
    {"timestamp": "2026-10-04T10:06:00", "reason": "Suspicious URL pattern: /\\.env"},
]

def case(name, key, obj):
    inner = json.dumps(obj)
    return {"name": name, "key": key, "raw": json.dumps(inner), "inner": inner}

cases = [
    case("blacklist", "security:blacklist:192.0.2.10", blacklist),
    case("blocked", "security:blocked:192.0.2.11", blocked),
    case("attempts", "security:attempts:192.0.2.12", attempts),
]
json.dump({"cases": cases}, open(sys.argv[1], "w", encoding="utf-8"), indent=2, ensure_ascii=False)
