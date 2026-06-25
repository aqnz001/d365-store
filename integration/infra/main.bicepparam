// ---------------------------------------------------------------------------
// Non-secret defaults for the Phase-1 deployment. NO secret values here
// (Golden Rule #9) — all secrets live in Key Vault and are populated
// out-of-band. Override per environment via additional .bicepparam files or
// pipeline parameters.
// ---------------------------------------------------------------------------
using './main.bicep'

param namePrefix = 'partsportal'
param environment = 'dev'
param redisEnabled = true

// Publisher contact surfaced by API Management — non-secret placeholders.
// Replace with real owner contact before a real deployment.
param apimPublisherEmail = 'platform-team@example.com'
param apimPublisherName = 'Parts Portal Platform Team'

// location intentionally omitted — defaults to resourceGroup().location.
