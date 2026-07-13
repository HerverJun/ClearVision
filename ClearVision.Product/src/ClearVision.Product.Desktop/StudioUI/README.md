# ClearVision StudioUI

StudioUI is the independent Vue application root for Studio UI Next.

Prompt 2 adds only the host foundation:

- strict `StudioStartupConfigV1` reading;
- one reviewed WebView2 adapter and an explicit browser-test fake;
- one reviewed GET-only HTTP transport;
- a composition owner that validates startup, creates Platform, then mounts Vue;
- minimal technical diagnostics for startup, Host channel, token presence, `/health`, and `/api/auth/setup-status`.

It still does not implement Design Foundation, Canvas, App Shell, login migration, or any formal business capability/write client.

Generated assets are written to the Desktop `obj` tree (or an explicitly injected `VITE_OUT_DIR`) and are never written to source `wwwroot/studio`.
