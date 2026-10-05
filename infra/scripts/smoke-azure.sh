#!/usr/bin/env bash
# Smoke da demonstração na Azure (Fase 17), depois do deploy e do reset semanal.
#
# Só confere o que não toca o banco: acordá-lo à toa gastaria a cota gratuita. O F1 dorme
# sem uso, então a primeira resposta pode levar um bom tempo.
#
#   bash infra/scripts/smoke-azure.sh https://cartola-varzea.azurewebsites.net

set -euo pipefail

url="${1:?uso: smoke-azure.sh https://<app>.azurewebsites.net}"
cabecalhos=$(mktemp)
trap 'rm -f "$cabecalhos"' EXIT

for tentativa in $(seq 1 20); do
  if curl -fsS --max-time 60 -o /dev/null "$url/health/live"; then
    break
  fi
  if [ "$tentativa" -eq 20 ]; then
    echo "A API não respondeu em $url/health/live." >&2
    exit 1
  fi
  sleep 15
done

pagina=$(curl -fsS --max-time 60 -D "$cabecalhos" "$url/")
grep -q '<app-root>' <<< "$pagina" || { echo 'O index.html do Angular não veio.' >&2; exit 1; }
grep -qi '^content-security-policy:' "$cabecalhos" || { echo 'O index.html veio sem CSP.' >&2; exit 1; }

# HSTS só sai quando a API sabe que a conexão é HTTPS: sem ele, os cabeçalhos do proxy
# estão desligados, e o antiforgery vai derrubar as telas com 500.
grep -qi '^strict-transport-security:' "$cabecalhos" \
  || { echo 'Sem HSTS: ASPNETCORE_FORWARDEDHEADERS_ENABLED está ligado?' >&2; exit 1; }

entrada=$(curl -fsS --max-time 60 "$url/api/v1/auth/demo")
grep -q '"available":true' <<< "$entrada" || { echo 'A entrada de visitante está desligada.' >&2; exit 1; }
grep -q '"selfService":false' <<< "$entrada" || { echo 'O cadastro está aberto sem e-mail.' >&2; exit 1; }

echo "Smoke ok em $url"
