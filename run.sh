#!/usr/bin/env bash
#
# Interruptor dev/producao do servico (para TESTE local, no Ubuntu de dev).
#
#   ./run.sh dev    -> roda como Development (appsettings.json + appsettings.Local.json)
#   ./run.sh prod   -> roda como Production  (+ appsettings.Production.json)
#
# No SERVIDOR nao se usa este script: la o servico roda como Servico do Windows,
# que ja sobe como Production por padrao (ver README, secao de deploy).
#
set -e

MODE="${1:-dev}"
case "$MODE" in
  dev)  ASPNETCORE_ENVIRONMENT=Development ;;
  prod) ASPNETCORE_ENVIRONMENT=Production ;;
  *)    echo "Uso: $0 [dev|prod]"; exit 1 ;;
esac
export ASPNETCORE_ENVIRONMENT

echo ">> Ambiente: $ASPNETCORE_ENVIRONMENT"
echo ">> URL:      http://0.0.0.0:5080  (docs em /docs)"
echo

# --no-launch-profile: ignora o launchSettings (que fixa Development) para o
# ASPNETCORE_ENVIRONMENT acima valer tanto em dev quanto em prod.
exec dotnet run --project src/SensattaReports.Api --no-launch-profile
