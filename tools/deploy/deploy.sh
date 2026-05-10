#!/usr/bin/env bash
# MEPAuto deploy script — chạy trên VPS sau khi rsync code.
#
# Tiền điều kiện (đã hoàn tất qua B.0 inventory):
#   - tools/deploy/VPS-INVENTORY.md commit, 3 quyết định nginx/cert/data đã rõ
#   - .env đã tạo (copy từ .env.example, điền JWT_SIGNING_KEY)
#   - /var/mepauto-data tồn tại (hoặc tạo mới nếu fresh deploy)
#
# Workflow:
#   1. Chọn compose file theo quyết định nginx (system vs container)
#   2. Build image
#   3. Up service
#   4. Smoke test /health
#
# Shared VPS (với EPAuto): dùng variant "system" (default cho VPS này):
#   bash deploy.sh system
# VPS mới không có nginx: dùng variant "container":
#   bash deploy.sh container

set -euo pipefail

cd "$(dirname "$0")"

if [ ! -f .env ]; then
    echo "ERR: .env không tồn tại. Copy .env.example → .env và điền JWT_SIGNING_KEY."
    exit 1
fi

# Default: system-nginx (vì VPS này đã có EPAuto trên nginx system)
NGINX_VARIANT="${1:-system}"

case "$NGINX_VARIANT" in
    container)
        COMPOSE_FILE="docker-compose.yml"
        ;;
    system)
        COMPOSE_FILE="docker-compose.system-nginx.yml"
        ;;
    *)
        echo "Usage: $0 [system|container]"
        exit 1
        ;;
esac

echo "==> Variant: $NGINX_VARIANT (compose: $COMPOSE_FILE)"

# Tạo data dir nếu chưa có
if [ ! -d /var/mepauto-data ]; then
    echo "==> Tạo /var/mepauto-data"
    mkdir -p /var/mepauto-data
    chown 1000:1000 /var/mepauto-data
fi

# Build context = repo root (../..)
echo "==> Build image"
docker compose -f "$COMPOSE_FILE" --env-file .env build

echo "==> Up service"
docker compose -f "$COMPOSE_FILE" --env-file .env up -d

echo "==> Wait 5s for healthcheck"
sleep 5

echo "==> Smoke test /health (port 8081 — system-nginx variant)"
HEALTH_PORT="8081"
if [ "$NGINX_VARIANT" = "container" ]; then
    HEALTH_PORT="8080"
fi

if curl --fail --silent --max-time 5 "http://127.0.0.1:${HEALTH_PORT}/health" > /dev/null; then
    echo "    OK"
else
    echo "    FAIL — check log"
    docker compose -f "$COMPOSE_FILE" logs api | tail -50
    exit 1
fi

echo ""
echo "Deploy DONE. Service status:"
docker compose -f "$COMPOSE_FILE" ps

echo ""
echo "Verify từ ngoài:"
echo "  curl https://\${DOMAIN}/health"
echo ""
echo "Service cũ trên VPS (kiểm tra không bị ảnh hưởng):"
docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' | grep -v mepauto || echo "(no other services)"
