import AppKit
import WebKit

/// A borderless window on the desktop level (above the normal wallpaper, below the desktop icons),
/// on every Space, that never takes clicks or focus. It shows kaleido.html in a WKWebView.
///
/// Flicker protection: the web view never paints its default white background (it is transparent
/// over a black window), a new window stays invisible until the page is running, and a crashed
/// web process or lost WebGL context is reloaded behind a black fade.
final class WallpaperWindow: NSWindow, WKScriptMessageHandler, WKNavigationDelegate {
    private let webView: WKWebView
    private let page: URL
    private(set) var isReady = false
    let displayID: CGDirectDisplayID
    var onReady: ((WallpaperWindow) -> Void)?

    init(screen: NSScreen, page: URL) {
        self.page = page
        displayID = WallpaperWindow.displayID(of: screen)
        let config = WKWebViewConfiguration()
        webView = WKWebView(frame: NSRect(origin: .zero, size: screen.frame.size), configuration: config)
        super.init(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
        config.userContentController.add(self, name: "ready")

        setFrame(screen.frame, display: false)
        level = NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopWindow)))
        collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        ignoresMouseEvents = true
        isOpaque = true
        backgroundColor = .black
        hasShadow = false
        isReleasedWhenClosed = false
        alphaValue = 0                                         // shown once the page is running

        // never show WebKit's white default background: the black window shows through instead
        webView.setValue(false, forKey: "drawsBackground")
        if #available(macOS 12.0, *) { webView.underPageBackgroundColor = .black }
        webView.navigationDelegate = self
        webView.autoresizingMask = [.width, .height]
        contentView = webView
        load()
        orderFront(nil)
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    static func displayID(of screen: NSScreen) -> CGDirectDisplayID {
        let key = NSDeviceDescriptionKey("NSScreenNumber")
        return (screen.deviceDescription[key] as? NSNumber)?.uint32Value ?? 0
    }

    /// True when at least part of the wallpaper can be seen (not covered by other windows).
    var isVisibleOnScreen: Bool { occlusionState.contains(.visible) }

    func move(to screen: NSScreen) {
        if frame != screen.frame { setFrame(screen.frame, display: true) }
    }

    private func load() {
        isReady = false
        webView.loadFileURL(page, allowingReadAccessTo: page.deletingLastPathComponent())
    }

    private func reloadHidden() {
        alphaValue = 0
        load()
    }

    // MARK: messages from kaleido.html

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        let body = message.body as? String ?? ""
        if body == "contextlost" {                             // WebGL context gone: start over unseen
            reloadHidden()
            return
        }
        isReady = true
        onReady?(self)
        // give WebGL a few frames before fading in, so nothing half-drawn is ever visible
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) { [weak self] in
            guard let self, self.isReady else { return }
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = 0.6
                self.animator().alphaValue = 1
            }
        }
    }

    // MARK: WKNavigationDelegate

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        reloadHidden()                                         // web process crashed or was killed
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in self?.reloadHidden() }
    }

    func run(_ script: String) {
        guard isReady else { return }
        webView.evaluateJavaScript(script, completionHandler: nil)
    }

    func tearDown() {
        webView.configuration.userContentController.removeScriptMessageHandler(forName: "ready")
        webView.navigationDelegate = nil
        orderOut(nil)
        close()
    }
}
