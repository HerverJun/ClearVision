# Startup boundary

`studioStartupConfig.ts` defines and validates the exact frozen `StudioStartupConfigV1` contract. Desktop reads only `window.__CLEARVISION_STARTUP__`; browser tests must pass an explicit fixture.

This boundary performs no port discovery and never falls back to query strings, storage, `window.__API_BASE_URL__`, or retired FrontendV2 fields.
