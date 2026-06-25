// checkout-validate — Phase 1 skeleton (T1). Real logic lands in T8.
//
// Golden Rule #11: NO browser storage. This extension MUST NOT use
// localStorage / sessionStorage / IndexedDB / document.cookie. All state comes
// from Shopify-supported extension APIs (settings, useApi, useBuyerJourney).
//
// Golden Rule #5: synced stock is advisory; the live check is authoritative.
// At the checkout gate we POST to the middleware: /cart/validate (live
// availability) then /cart/reserve (soft reservation via IVS) and BLOCK on
// shortfall or credit hold before payment.
// Golden Rule #2: inventory is reserved ONLY through the IVS-backed middleware,
// never from the storefront directly.

import {
  reactExtension,
  useApi,
  useSettings,
  useBuyerJourneyIntercept,
  BlockStack,
  Text,
} from "@shopify/ui-extensions-react/checkout";

export default reactExtension(
  "purchase.checkout.delivery-address.render-before",
  () => <CheckoutValidate />,
);

function CheckoutValidate() {
  // Middleware base URL comes from extension settings (configuration),
  // never a hardcoded literal. See shopify.extension.toml settings.fields.
  const { middleware_base_url: middlewareBaseUrl } = useSettings();
  const { lines, sessionToken } = useApi();

  // Block progression past the checkout gate until validate + reserve succeed.
  useBuyerJourneyIntercept(({ canBlockProgress }) => {
    // TODO(T8): call middleware and decide blocking from real responses.
    //   const token = await sessionToken.get();
    //   const validate = await fetch(`${middlewareBaseUrl}/cart/validate`, {
    //     method: "POST",
    //     headers: {
    //       "content-type": "application/json",
    //       authorization: `Bearer ${token}`,
    //       // Correlation ID propagated cart -> reserve -> order -> fulfilment.
    //       "x-correlation-id": correlationId,
    //     },
    //     body: JSON.stringify({ lines }),
    //   });
    //   On shortfall -> block with reason "shortfall".
    //   const reserve = await fetch(`${middlewareBaseUrl}/cart/reserve`, { ... });
    //   reserve carries an idempotency key (Golden Rule #6).
    //   On credit hold -> block with reason "credit".
    if (!canBlockProgress) {
      return { behavior: "allow" };
    }
    return { behavior: "allow" }; // TODO(T8): replace with real validate/reserve outcome.
  });

  return (
    <BlockStack>
      <Text appearance="subdued">
        {/* TODO(T8): surface validate/reserve status (shortfall, credit hold). */}
        Verifying availability…
      </Text>
    </BlockStack>
  );
}
