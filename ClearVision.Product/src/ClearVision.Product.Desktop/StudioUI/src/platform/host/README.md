# Host boundary

`webView2HostAdapter.ts` is the only production owner of the WebView2 web-message channel. It exposes a narrow post/subscribe/dispose contract and removes its global listener when the final subscriber leaves or the adapter is disposed.

`browserHostFake.ts` is an explicit browser-test fake. Its `browser-fake` kind and diagnostics never claim that WebView2 is available.
