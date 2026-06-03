# Desktop Studio Boundary

Desktop remains the Studio shell. The following stay Desktop-only:

- WebView2 host
- Kestrel boot path
- `wwwroot` static assets
- web/auth endpoint mapping
- Studio-side AI flow authoring
- Studio project browsing/editor interactions

Runtime/Station must not introduce:

- `Microsoft.Web.WebView2`
- `WebApplication`
- `Kestrel`
- `wwwroot`
- `MapVisionApiEndpoints`
- `ClearVision.Product.Desktop` project references

Review rule:

- If a new Runtime/Station change needs browser hosting or Desktop-only web plumbing, the change belongs in Desktop, not Station.
