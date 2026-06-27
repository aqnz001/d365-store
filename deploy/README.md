# deploy/ — configuration templates

Copy-paste starting points for a real deployment. The authoritative, explained list is
[docs/05-Go-Live-Configuration.md](../docs/05-Go-Live-Configuration.md) — read it first.

| File | For |
|---|---|
| `bff.appsettings.Production.sample.json` | The BFF App Service — config (secrets come from Key Vault via `KeyVault:Uri`) |
| `functions.appsettings.sample.env` | Each Function app's **Application settings** (Azure uses `Section__Key` form) |
| `web.env.sample` | SPA dev proxy (`VITE_BFF_URL`) + the future Stripe publishable key |

🔒 = secret → put in **Key Vault**, reference it (e.g. `@Microsoft.KeyVault(SecretUri=…)`), never commit a value.
`ExternalAuth:Scopes:*` left blank ⇒ no outbound token sent (the mocks); fill them to turn on Entra auth for that client.
