#!/usr/bin/env bash
# Runs the whole Phase-1 stack locally WITHOUT Azure Functions Core Tools or a Service Bus
# emulator: the four mocks + the dev-gateway (re-hosts the middleware services; runs the
# writeback in-process) + the BFF. Seeds a little sample data, then waits.
#
# Then run the SPA in another terminal:
#   cd storefront/web && npm install && npm run dev   # http://localhost:5173 (proxies /api → BFF)
#
# The dev auth uses the "X-Dev-Customer" header; the SPA sends C-DEV. This script seeds C-DEV.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

dotnet build PartsPortal.slnx -c Release

PIDS=()
trap 'echo; echo "stopping…"; kill "${PIDS[@]}" 2>/dev/null || true; pkill -f PartsPortal 2>/dev/null || true' EXIT
run() { ASPNETCORE_URLS="$1" dotnet run --no-build -c Release --project "$2" >"/tmp/pp-$3.log" 2>&1 & PIDS+=($!); }

run http://localhost:5101 mocks/ivs-sim ivs
run http://localhost:5102 mocks/odata-sim odata
run http://localhost:5103 mocks/pricing-credit-sim pricing
run http://localhost:5104 mocks/shopify-sim shopify
run http://localhost:7080 tools/dev-gateway gateway
ASPNETCORE_URLS=http://localhost:5080 ASPNETCORE_ENVIRONMENT=Production \
  Bff__MiddlewareBaseUrl=http://localhost:7080/ Bff__CatalogBaseUrl=http://localhost:5104/ \
  dotnet run --no-build -c Release --project storefront/bff >/tmp/pp-bff.log 2>&1 & PIDS+=($!)

echo "waiting for services…"
for u in 5101 5102 5103 5104 7080 5080; do
  curl -sf --retry 90 --retry-delay 1 --retry-connrefused "http://localhost:$u/health" >/dev/null
done

echo "seeding sample data (customer C-DEV)…"
curl -s -X PUT http://localhost:5104/admin/products/PART-1 -H 'content-type: application/json' \
  -d '{"sku":"PART-1","title":"Front Brake Pad Set","bodyHtml":"Ceramic front pads","productType":"Brakes","status":"active","metafields":{"unit":"ea","orderMultiple":1,"minOrderQty":1,"backorderable":false}}' >/dev/null
curl -s -X PUT http://localhost:5104/admin/products/PART-2 -H 'content-type: application/json' \
  -d '{"sku":"PART-2","title":"Spin-on Oil Filter","bodyHtml":"","productType":"Filtration","status":"active","metafields":{"unit":"ea","orderMultiple":1,"minOrderQty":1,"backorderable":true}}' >/dev/null
curl -s -X POST http://localhost:5101/admin/seed -H 'content-type: application/json' \
  -d '{"items":[{"productId":"PART-1","site":"1","location":"11","afr":50,"atp":50},{"productId":"PART-2","site":"1","location":"11","afr":3,"atp":3}]}' >/dev/null
curl -s -X POST http://localhost:5102/admin/seed -H 'content-type: application/json' \
  -d '{"items":["PART-1","PART-2"],"customers":["C-DEV"]}' >/dev/null
curl -s -X POST http://localhost:5103/admin/seed -H 'content-type: application/json' \
  -d '{"prices":[{"itemNumber":"PART-1","unitPrice":24.50},{"itemNumber":"PART-2","unitPrice":9.95}],"credit":[{"customerAccount":"C-DEV","status":"OK"}]}' >/dev/null

echo
echo "Stack is up:"
echo "  BFF        http://localhost:5080   (dev auth: X-Dev-Customer header)"
echo "  dev-gateway http://localhost:7080  mocks 5101-5104"
echo "  SPA        cd storefront/web && npm run dev   → http://localhost:5173"
echo "Press Ctrl+C to stop."
wait
