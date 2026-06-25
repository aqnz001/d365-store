// availability-display — Phase 1 skeleton (T1). Real logic lands in T8.
//
// Golden Rule #4: availability is shown as a BAND, never as a raw on-hand or
// exact stock count. Bands are computed upstream from max(0, ATP - classBuffer)
// and published to a product metafield by the sync job.
// Golden Rule #11: NO browser storage in this extension.

import {
  reactExtension,
  useAppMetafields,
  Badge,
} from "@shopify/ui-extensions-react/checkout";

// Advisory bands only — no numeric quantities exposed to the buyer.
const BANDS = {
  in_stock: { label: "In stock", tone: "success" },
  low: { label: "Low", tone: "warning" },
  backorder: { label: "Backorder", tone: "info" },
  mto: { label: "Made to order", tone: "subdued" },
};

export default reactExtension(
  "purchase.checkout.cart-line-item.render-after",
  () => <AvailabilityBand />,
);

function AvailabilityBand() {
  // TODO(T8): read the band from the product metafield published by the sync
  // job (e.g. namespace "partsportal", key "availability_band") and map the
  // current cart line to its band. Never read or render a raw count.
  const metafields = useAppMetafields();

  // Placeholder until T8 wiring; defaults to a safe, non-numeric band.
  const bandKey = "in_stock"; // TODO(T8): derive from metafields for this line.
  const band = BANDS[bandKey] ?? BANDS.mto;

  return <Badge tone={band.tone}>{band.label}</Badge>;
}
