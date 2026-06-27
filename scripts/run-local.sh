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

echo "seeding sample catalog (customer C-DEV)…"

seed_product() { curl -s -X PUT "http://localhost:5104/admin/products/$1" -H 'content-type: application/json' -d "$2" >/dev/null; }

seed_product BRK-PAD-0420 '{"sku":"BRK-PAD-0420","title":"Ceramic Brake Pad Set (Front)","bodyHtml":"Low-dust ceramic front pads with wear-indicator shims.","productType":"Brakes","status":"active","metafields":{"unit":"set","orderMultiple":1,"minOrderQty":1,"backorderable":true}}'
seed_product BRK-DSC-0890 '{"sku":"BRK-DSC-0890","title":"Vented Brake Disc 280mm","bodyHtml":"Vented front brake disc, corrosion-protected.","productType":"Brakes","status":"active","metafields":{"unit":"ea","orderMultiple":2,"minOrderQty":2,"backorderable":false}}'
seed_product OIL-FLT-1015 '{"sku":"OIL-FLT-1015","title":"Spin-On Oil Filter","bodyHtml":"Full-flow spin-on filter with anti-drainback valve.","productType":"Filtration","status":"active","metafields":{"unit":"ea","orderMultiple":6,"minOrderQty":6,"backorderable":true}}'
seed_product AIR-FLT-2203 '{"sku":"AIR-FLT-2203","title":"Panel Air Filter","bodyHtml":"High-flow panel air filter element.","productType":"Filtration","status":"active","metafields":{"unit":"ea","orderMultiple":1,"minOrderQty":1,"backorderable":false}}'
seed_product BLT-V-A1240 '{"sku":"BLT-V-A1240","title":"V-Belt A-Section (per metre)","bodyHtml":"Industrial wrapped V-belt, A-section profile.","productType":"Power Transmission","status":"active","metafields":{"unit":"m","orderMultiple":1,"minOrderQty":2,"backorderable":false}}'
seed_product BRG-6205-2RS '{"sku":"BRG-6205-2RS","title":"Deep Groove Ball Bearing 6205-2RS","bodyHtml":"Sealed deep-groove ball bearing, 25mm bore.","productType":"Bearings","status":"active","metafields":{"unit":"ea","orderMultiple":2,"minOrderQty":2,"backorderable":false}}'
seed_product GLV-NIT-0009 '{"sku":"GLV-NIT-0009","title":"Nitrile Mechanic Gloves (Large)","bodyHtml":"Powder-free 5-mil nitrile gloves, 100-count box.","productType":"Workshop Consumables","status":"active","metafields":{"unit":"box","orderMultiple":1,"minOrderQty":1,"backorderable":true}}'
seed_product WPR-BLD-0185 '{"sku":"WPR-BLD-0185","title":"Wiper Blade Refill 18-inch","bodyHtml":"Metal-frame wiper blade refill.","productType":"Wipers","status":"active","metafields":{"unit":"ea","orderMultiple":2,"minOrderQty":2,"backorderable":false}}'
seed_product ELE-ALT-3300 '{"sku":"ELE-ALT-3300","title":"Alternator 12V 120A","bodyHtml":"Remanufactured 12V 120A alternator, exchange unit.","productType":"Electrical","status":"active","metafields":{"unit":"ea","orderMultiple":1,"minOrderQty":1,"backorderable":true}}'
seed_product FAS-BLT-M10 '{"sku":"FAS-BLT-M10","title":"Hex Bolt M10x50 (Box of 50)","bodyHtml":"Zinc-plated hex set bolts, grade 8.8.","productType":"Fasteners","status":"active","metafields":{"unit":"box","orderMultiple":1,"minOrderQty":1,"backorderable":false}}'
seed_product FLU-COL-5L '{"sku":"FLU-COL-5L","title":"Coolant Concentrate 5L","bodyHtml":"OAT antifreeze/coolant concentrate, 5 litre.","productType":"Fluids","status":"active","metafields":{"unit":"ea","orderMultiple":1,"minOrderQty":1,"backorderable":false}}'

# IVS availability — spread of bands: in stock, low stock, and backorder (afr 0 + backorderable).
curl -s -X POST http://localhost:5101/admin/seed -H 'content-type: application/json' -d '{"items":[
  {"productId":"BRK-PAD-0420","site":"1","location":"11","afr":50,"atp":50},
  {"productId":"BRK-DSC-0890","site":"1","location":"11","afr":8,"atp":8},
  {"productId":"OIL-FLT-1015","site":"1","location":"11","afr":0,"atp":0},
  {"productId":"AIR-FLT-2203","site":"1","location":"11","afr":24,"atp":24},
  {"productId":"BLT-V-A1240","site":"1","location":"11","afr":40,"atp":40},
  {"productId":"BRG-6205-2RS","site":"1","location":"11","afr":4,"atp":4},
  {"productId":"GLV-NIT-0009","site":"1","location":"11","afr":120,"atp":120},
  {"productId":"WPR-BLD-0185","site":"1","location":"11","afr":6,"atp":6},
  {"productId":"ELE-ALT-3300","site":"1","location":"11","afr":0,"atp":0},
  {"productId":"FAS-BLT-M10","site":"1","location":"11","afr":60,"atp":60},
  {"productId":"FLU-COL-5L","site":"1","location":"11","afr":18,"atp":18}
]}' >/dev/null

# FinOps master data (item + customer validation for writeback).
curl -s -X POST http://localhost:5102/admin/seed -H 'content-type: application/json' \
  -d '{"items":["BRK-PAD-0420","BRK-DSC-0890","OIL-FLT-1015","AIR-FLT-2203","BLT-V-A1240","BRG-6205-2RS","GLV-NIT-0009","WPR-BLD-0185","ELE-ALT-3300","FAS-BLT-M10","FLU-COL-5L"],"customers":["C-DEV"]}' >/dev/null

# Pricing + credit standing.
curl -s -X POST http://localhost:5103/admin/seed -H 'content-type: application/json' -d '{"prices":[
  {"itemNumber":"BRK-PAD-0420","unitPrice":34.50},{"itemNumber":"BRK-DSC-0890","unitPrice":58.00},
  {"itemNumber":"OIL-FLT-1015","unitPrice":6.95},{"itemNumber":"AIR-FLT-2203","unitPrice":12.40},
  {"itemNumber":"BLT-V-A1240","unitPrice":4.20},{"itemNumber":"BRG-6205-2RS","unitPrice":9.80},
  {"itemNumber":"GLV-NIT-0009","unitPrice":11.50},{"itemNumber":"WPR-BLD-0185","unitPrice":7.25},
  {"itemNumber":"ELE-ALT-3300","unitPrice":189.00},{"itemNumber":"FAS-BLT-M10","unitPrice":14.00},
  {"itemNumber":"FLU-COL-5L","unitPrice":16.75}
],"credit":[{"customerAccount":"C-DEV","status":"OK"}]}' >/dev/null

echo
echo "Stack is up:"
echo "  BFF        http://localhost:5080   (dev auth: X-Dev-Customer header)"
echo "  dev-gateway http://localhost:7080  mocks 5101-5104"
echo "  SPA        cd storefront/web && npm run dev   → http://localhost:5173"
echo "Press Ctrl+C to stop."
wait
