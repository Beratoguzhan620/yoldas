#!/usr/bin/env bash
# Demo tooling: build the one-container image, run it against PostGIS like Render + Neon would, and look at it.
set -uo pipefail
ROOT=$(pwd)
(cd web && npm ci --no-audit --no-fund > "$ROOT/out/web-install.log" 2>&1 && npm run check > "$ROOT/out/web-check.log" 2>&1; echo "web check: $?")
docker network create demo > /dev/null
docker run -d --name db --network demo -e POSTGRES_PASSWORD=pw -e POSTGRES_DB=neondb postgis/postgis:17-3.5-alpine > /dev/null
docker build -f deploy/demo/Dockerfile -t yoldas-demo . > out/demo-build.log 2>&1; echo "demo build: $?"
for i in $(seq 1 30); do docker exec db pg_isready -U postgres > /dev/null 2>&1 && break; sleep 2; done
sleep 3
docker run -d --name app --network demo -p 8080:8080 \
  -e "ConnectionStrings__Yoldas=postgresql://postgres:pw@db:5432/neondb?sslmode=disable" \
  -e Jwt__SigningKey=demo-tooling-signing-key-0123456789abcdef0123 -e Demo__Enabled=true -e Database__MigrateOnStartup=true -e PORT=8080 \
  yoldas-demo > /dev/null
for i in $(seq 1 60); do curl -sf localhost:8080/health/ready > /dev/null && break; sleep 2; done
echo "ready: $(curl -s localhost:8080/health/ready)"
echo "index: $(curl -s -o /dev/null -w '%{http_code} %{content_type}' localhost:8080/rides)"
echo "csp: $(curl -sI localhost:8080/ | grep -i content-security | cut -c1-60)"
echo "api 404: $(curl -s -o /dev/null -w '%{http_code}' localhost:8080/api/nothing)"
echo "demo: $(curl -s localhost:8080/api/demo | head -c 300)"
cd tests/e2e && npm install --no-audit --no-fund > "$ROOT/out/e2e-install.log" 2>&1 && npx playwright install --with-deps chromium >> "$ROOT/out/e2e-install.log" 2>&1
cp "$ROOT/.tooling/media.mjs" ./look.mjs && node look.mjs "$ROOT/out/shots/" 2>&1 | tee "$ROOT/out/look.log"
cd "$ROOT"
docker logs app > out/app.log 2>&1
