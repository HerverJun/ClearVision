using Microsoft.Web.WebView2.WinForms;

namespace ClearVision.Product.Desktop.Handlers;

internal interface IDesktopWebMessageOwner : IDisposable
{
    string Surface { get; }

    int ActiveSubscriptionCount { get; }

    void Initialize(WebView2 webViewControl);
}
