# ClearVision StudioUI

StudioUI is the independent Vue build foundation for Studio UI Next.

Prompt 1 contains only the toolchain, route skeleton, build diagnostics, and placeholder routes. It does not implement a Desktop startup entry, business capability, HostBridge, API transport, Design Lab, or Canvas host.

Generated assets are written to the Desktop `obj` tree (or an explicitly injected `VITE_OUT_DIR`) and are never written to source `wwwroot/studio`.
