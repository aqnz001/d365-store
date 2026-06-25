# D365 FinOps / Inventory Visibility — Configuration Runbook

| | |
|---|---|
| **Status** | Draft for the functional team |
| **Audience** | D365 functional consultant / SCM admin / environment admin |
| **Companion docs** | SAD, TDD (§4, §10), Phased Delivery Plan (§3.2 "needs sandbox") |
| **Target environment** | Tier-1 sandbox first, then production-like |

> **Scope.** This runbook covers the FinOps-side **configuration** the integration depends on. It is mostly settings and feature enablement, not code. Navigation paths are version-sensitive — confirm each against your deployed FinOps version and Microsoft Learn. Items marked **[DECISION]** require sign-off before configuring.

---

## 0. Pre-flight — licensing & gating check

This is the load-bearing prerequisite for the whole availability design.

1. **Confirm Supply Chain Management is licensed.** The Inventory Visibility Service (IVS) is included with SCM. The soft-reservation and allocation features the portal relies on require it.
   - If **Finance-only** (no SCM): IVS is available as a standalone service but must be provisioned and fed inventory data explicitly. **Stop and escalate** — this changes the design and effort. _(SAD Open Question #1.)_
2. **Confirm environment provisioning route.** Add-in/environment management historically ran through Lifecycle Services (LCS); newer guidance routes this through the **Power Platform admin center**. Confirm which applies to your tenant before provisioning the IVS add-in.
3. **Confirm FinOps version** supports the features used: real-time ATP, **forward-dated ATP** (future-period), soft reservation, and allocation. Note the version for the integration team.

**Exit:** SCM/IVS confirmed available; provisioning route known; version recorded.

---

## A. Inventory Visibility Service (IVS)

### A1. Install / provision the IVS add-in
- Provision the Inventory Visibility add-in for the environment (via the confirmed route — Power Platform admin center or LCS).
- Confirm the IVS endpoint base URL and environment ID; record for the integration team.

### A2. App registration & auth (for API access)
- Register (or reuse) an **Entra ID application** the middleware will use to call IVS.
- Grant least-privilege access for inventory query + reservation operations.
- **Record:** tenant ID, client (app) ID, and the **scope/audience** for IVS. **Do not** share the client secret in documents — hand it over via Key Vault (§G).

### A3. Data source / index configuration
- Configure the IVS data source so FinOps on-hand and reservation data flow into the IVS index.
- Verify on-hand quantities and existing reservations are reflected in IVS queries.

### A4. Dimension mappings
- Map the inventory dimensions the storefront needs: **site**, **warehouse/location**, and any others used for availability (e.g., batch/serial only if relevant to parts).
- **[DECISION]** Decide the granularity exposed to the storefront (e.g., site-level vs location-level). Ties to multi-warehouse display (§A8, SAD OQ#6).

### A5. ATP calculation formula
- Configure the **available-to-promise** formula: which physical and ordered quantities count, how reservations/allocations are deducted, and which inbound supply (POs, transfers) contributes.
- **[DECISION]** Confirm the AFR (available-for-reservation) definition used for the storefront — this is the number the portal treats as authoritative.
- Validate against a known item: IVS ATP should equal expected on-hand minus committed.

### A6. Soft reservation feature
- Enable **soft reservation**.
- Confirm reserve behavior: `ifCheckAvailForReserv` validates AFR before reserving and returns a **reservation ID**.
- Confirm the **soft → physical** conversion: when a sales order carrying the reservation ID is created in FinOps, the soft reservation converts to a physical reservation (no double consumption).
- **[DECISION]** Reservation TTL / expiry handling — agree how long a soft reservation is held and how release is triggered (SAD OQ; TDD §7.1).

### A7. Allocation (ring-fencing) — for advance/block orders
- Enable **allocation** to ring-fence on-hand into protected pools (by channel / customer group / location).
- Confirm allocated quantity reduces AFR for other channels.
- **[DECISION]** Which item classes support advance/block orders, and the hold/expiry policy (SAD OQ#4).

### A8. Partitioning (multi-site)
- If multiple sites/warehouses: configure the IVS partition rule (typically by location) for performant queries.
- **[DECISION]** Storefront shows **aggregate** availability or **branch-specific** (SAD OQ#6).

### A9. Forward-dated ATP
- Enable/confirm **future-period ATP** so the portal can promise dates for backorders and reserve against scheduled inbound (TDD §6.4–6.5).

**Exit (Section A):** IVS returns correct ATP/AFR; soft reservation places, returns an ID, and converts on order; allocation reduces AFR; forward ATP available.

---

## B. Sales order integration

### B1. Number sequences
- **[DECISION]** Sales-order number ownership: **FinOps-generated** (recommended) vs portal-supplied (SAD OQ#5).
- Configure the sales-order number sequence accordingly; record the format for the integration team.

### B2. OData entity availability
- Confirm `SalesOrderHeadersV2` and `SalesOrderLines` are enabled and accessible via OData in the environment.
- Confirm required vs optional fields for header and line creation in your version; share the field list (feeds TDD §4.4 / §5.5).

### B3. Service account for order writeback
- Register (or reuse) an **Entra ID app** for OData order creation with least-privilege (create sales orders; read pricing/credit if via custom service).
- **Record:** app ID + scope; secret via Key Vault only.

**Exit (Section B):** A header→lines order can be created via OData in the sandbox; number sequence behaves as agreed.

---

## C. Business events

### C1. Enable events
Enable the business events the integration consumes:
- Inventory threshold crossing (drives event-based availability push)
- Price change
- Shipment confirmed
- Invoice posted
- Sales order status change

### C2. Endpoint routing
- Configure the business-event endpoint to route to Azure (**Event Grid** or **Service Bus**, per the integration team's choice).
- Validate at least one event fires end-to-end into the Azure endpoint.

**Exit (Section C):** Subscribed events fire and route to Azure with expected payloads.

---

## D. BYOD catalog export

### D1. Confirm catalog entities
- Confirm the catalog data entities feeding the storefront are exported to the Azure SQL **BYOD** (product, description, category, UoM, order multiple/min-qty, backorderable flag, lifecycle state — TDD §5.1).

### D2. Refresh schedule
- Confirm the BYOD refresh schedule and that incremental export is enabled.
- Validate the serving dataset matches FinOps for a sample of items.

**Exit (Section D):** Catalog fields the storefront needs are present and refreshing in BYOD.

---

## E. Master data readiness

Order creation fails on missing references, so confirm before order flows:
- Customer accounts (B2B companies) exist with delivery addresses.
- Items exist with valid units of measure and any order multiples/min-qty.
- Price lists / trade agreements configured for the relevant customers (feeds live price resolution).
- Credit limits / hold status configured where credit checks apply.

**Exit (Section E):** A representative customer can be ordered against end-to-end without master-data errors.

---

## F. Validation & acceptance (Phase 2 exit)

Run these against the sandbox and record results:

| # | Check | Expected |
|---|---|---|
| F1 | IVS ATP query for a known item | Matches on-hand minus committed |
| F2 | Soft reservation via API | Returns reservation ID; AFR drops |
| F3 | Create order carrying the reservation ID | Soft reservation converts to physical |
| F4 | Allocation for a channel | AFR reduced for other channels |
| F5 | Forward-dated ATP | Returns future-period availability |
| F6 | OData header→lines create | Order created; number sequence correct |
| F7 | Line with bad master data | Rejected with a clear error (validation path works) |
| F8 | Business events | Inventory/price/shipment/invoice events route to Azure |
| F9 | BYOD catalog sample | Matches FinOps |
| F10 | Credit hold customer | Credit status returned as on-hold |

**Exit (Section F):** all checks pass; results shared with the integration team to replace mocks with sandbox endpoints.

---

## G. Handover to the integration team

Provide (secrets via Key Vault, never in documents):
- IVS endpoint base URL + environment ID.
- Entra app IDs + scopes for IVS and OData (secrets in Key Vault).
- Confirmed OData field lists (header/lines) and required-field notes.
- Number-sequence format.
- Business-event endpoint + sample payloads.
- BYOD connection/refresh details.
- Any deviations from the TDD's representative contracts (so contracts can be updated once, centrally).

---

## Appendix — Parameter sheet (fill in)

| Parameter | Value |
|---|---|
| FinOps version | _____ |
| SCM/IVS licensed? | _____ |
| Provisioning route (PPAC / LCS) | _____ |
| IVS base URL / environment ID | _____ |
| IVS Entra app ID / scope | _____ |
| OData Entra app ID / scope | _____ |
| Sales-order number ownership **[DECISION]** | _____ |
| Number-sequence format | _____ |
| AFR definition for storefront **[DECISION]** | _____ |
| Dimension granularity exposed **[DECISION]** | _____ |
| Reservation TTL **[DECISION]** | _____ |
| Advance-order item classes + hold policy **[DECISION]** | _____ |
| Multi-site: aggregate vs branch **[DECISION]** | _____ |
| Business-event endpoint (Event Grid / Service Bus) | _____ |
| BYOD refresh schedule | _____ |
