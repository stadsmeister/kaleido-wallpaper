import AppKit
import ServiceManagement

/// Menu-bar app (no Dock icon) that shows the kaleidoscope as a live wallpaper on every screen
/// and feeds it the sound of the app chosen in the menu.
@main
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        withExtendedLifetime(delegate) { app.run() }
    }

    private var settings = Settings.load()
    private let analyzer = SpectrumAnalyzer()
    private lazy var capture = AudioCapture(analyzer: analyzer)
    private let artwork = NowPlayingArtwork()
    private let colorEditor = ColorEditor()
    private var windows: [WallpaperWindow] = []
    private var statusItem: NSStatusItem!
    private var feedTimer: Timer?
    private var status = "Starting…"
    private var paused = false

    // MARK: lifecycle

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "Kaleido Wallpaper")
        let menu = NSMenu()
        menu.delegate = self
        menu.autoenablesItems = false                      // we decide which items are greyed out
        statusItem.menu = menu

        capture.onStatus = { [weak self] text in self?.status = text }
        colorEditor.onChange = { [weak self] colors in
            self?.update { $0.customColors = colors; $0.useCustomColors = true }
        }
        artwork.onArtwork = { [weak self] url in
            guard let self else { return }
            let script = NowPlayingArtwork.script(for: url)
            self.windows.forEach { $0.run(script) }
        }

        let center = NotificationCenter.default
        center.addObserver(self, selector: #selector(screensChanged),
                           name: NSApplication.didChangeScreenParametersNotification, object: nil)
        let workspace = NSWorkspace.shared.notificationCenter
        workspace.addObserver(self, selector: #selector(appLaunched(_:)),
                              name: NSWorkspace.didLaunchApplicationNotification, object: nil)
        workspace.addObserver(self, selector: #selector(appTerminated(_:)),
                              name: NSWorkspace.didTerminateApplicationNotification, object: nil)
        workspace.addObserver(self, selector: #selector(screensSlept),
                              name: NSWorkspace.screensDidSleepNotification, object: nil)
        workspace.addObserver(self, selector: #selector(screensWoke),
                              name: NSWorkspace.screensDidWakeNotification, object: nil)

        analyzer.configure(fftSize: settings.fftSize, smoothing: settings.smoothing)
        buildWindows()
        if !CGPreflightScreenCaptureAccess() {
            CGRequestScreenCaptureAccess()          // shows the macOS permission prompt once
            status = "Allow Screen Recording, then restart the app"
        }
        restartCapture()
        updateArtworkMode()
        startFeeding()
    }

    /// Album art mode runs only while reacting to Apple Music or Spotify (and not paused).
    private var artworkWanted: Bool {
        settings.albumArt && NowPlayingArtwork.supports(settings.targetBundleID) && !paused
    }

    private func updateArtworkMode() {
        if artworkWanted, let id = settings.targetBundleID { artwork.start(bundleID: id) } else { artwork.stop() }
    }

    // MARK: wallpaper windows

    /// Keeps exactly one wallpaper window per screen. Existing windows are kept and only moved,
    /// because a new web view needs a moment to start (that used to show up as a flash).
    private func buildWindows() {
        guard !paused, let page = Bundle.main.url(forResource: "kaleido", withExtension: "html") else {
            windows.forEach { $0.tearDown() }
            windows = []
            return
        }
        let screens = NSScreen.screens
        let ids = Set(screens.map { WallpaperWindow.displayID(of: $0) })
        for window in windows where !ids.contains(window.displayID) { window.tearDown() }
        windows.removeAll { !ids.contains($0.displayID) }

        for screen in screens {
            let id = WallpaperWindow.displayID(of: screen)
            if let existing = windows.first(where: { $0.displayID == id }) {
                existing.move(to: screen)
                continue
            }
            let window = WallpaperWindow(screen: screen, page: page)
            window.onReady = { [weak self] w in
                guard let self else { return }
                w.run("psyInit({sampleRate:\(AudioCapture.sampleRate),fftSize:\(self.analyzer.fftSize)})")
                w.run("psySettings(\(self.settings.javaScriptObject))")
                if self.artworkWanted { w.run(NowPlayingArtwork.script(for: self.artwork.currentDataURL)) }
            }
            windows.append(window)
        }
    }

    @objc private func screensChanged() { buildWindows() }

    // MARK: audio → web views

    private func startFeeding() {
        feedTimer?.invalidate()
        let timer = Timer(timeInterval: 1.0 / 60.0, target: self, selector: #selector(feed),
                          userInfo: nil, repeats: true)
        RunLoop.main.add(timer, forMode: .common)       // keeps running while the menu is open
        feedTimer = timer
    }

    @objc private func feed() {
        guard !paused, analyzer.hasRecentInput else { return }
        // skip the work entirely while every wallpaper is hidden behind windows
        guard windows.contains(where: { $0.isVisibleOnScreen }) else { return }
        let base64 = Data(analyzer.byteSpectrum()).base64EncodedString()
        let script = "psyFeed('\(base64)')"
        windows.forEach { $0.run(script) }
    }

    private func restartCapture() {
        let target = settings.targetBundleID, name = settings.targetName
        Task { @MainActor in
            if self.paused { await self.capture.stop(); return }
            await self.capture.start(bundleID: target, name: name)
        }
    }

    @objc private func appLaunched(_ note: Notification) {
        guard let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication,
              app.bundleIdentifier != nil, app.bundleIdentifier == settings.targetBundleID else { return }
        // give the app a moment to register with the window server before capturing it
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            self.restartCapture()
        }
    }

    @objc private func appTerminated(_ note: Notification) {
        guard let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication,
              app.bundleIdentifier != nil, app.bundleIdentifier == settings.targetBundleID else { return }
        Task { @MainActor in await self.capture.stop() }
        status = "Waiting for \(settings.targetName ?? "the app") to start"
    }

    @objc private func screensSlept() { Task { @MainActor in await self.capture.stop() } }
    @objc private func screensWoke() { restartCapture() }

    // MARK: settings

    private func update(_ change: (inout Settings) -> Void, recapture: Bool = false) {
        let oldFFT = settings.fftSize
        change(&settings)
        settings.save()
        analyzer.configure(fftSize: settings.fftSize, smoothing: settings.smoothing)
        if settings.fftSize != oldFFT {
            windows.forEach { $0.run("psyInit({sampleRate:\(AudioCapture.sampleRate),fftSize:\(settings.fftSize)})") }
        }
        windows.forEach { $0.run("psySettings(\(settings.javaScriptObject))") }
        if recapture { restartCapture() }
        updateArtworkMode()
    }

    // MARK: menu

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()

        let header = NSMenuItem(title: status, action: nil, keyEquivalent: "")
        header.isEnabled = false
        menu.addItem(header)
        menu.addItem(.separator())

        menu.addItem(targetMenu())
        let art = ActionItem("Album art mode (Apple Music, Spotify)", checked: settings.albumArt) {
            self.update { $0.albumArt.toggle() }
        }
        art.isEnabled = NowPlayingArtwork.supports(settings.targetBundleID)
        art.toolTip = "Cover in the centre, kaleidoscope in the cover's colours. Choose Music or Spotify under \"React to\" first."
        menu.addItem(art)
        let colors = choiceMenu("Album colors", [("Natural", 0), ("Vibrant", 0.6), ("Max", 1.0)],
                                current: settings.albumVibrancy) { v in self.update { $0.albumVibrancy = v } }
        colors.isEnabled = art.isEnabled && settings.albumArt && !settings.useCustomColors
        menu.addItem(colors)
        menu.addItem(colorsMenu())
        menu.addItem(choiceMenu("Response", [("Smooth (like the browser)", 0), ("Fast", 1), ("Instant", 2)],
                                current: Double(settings.responsiveness)) { v in self.update { $0.responsiveness = Int(v) } })
        menu.addItem(beatEffectMenu())
        menu.addItem(choiceMenu("Sensitivity", [("Low", 0.8), ("Normal", 1.2), ("High", 1.8), ("Extreme", 2.5)],
                                current: settings.sensitivity) { v in self.update { $0.sensitivity = v } })
        menu.addItem(choiceMenu("Trails", [("Short", 0.4), ("Normal", 0.72), ("Long", 0.88)],
                                current: settings.trails) { v in self.update { $0.trails = v } })
        menu.addItem(choiceMenu("Ring speed", [("Calm", 0.5), ("Normal", 1.0), ("Fast", 2.0)],
                                current: settings.ringSpeed) { v in self.update { $0.ringSpeed = v } })
        menu.addItem(ActionItem("Reverse ring rotation", checked: settings.reverseRings) {
            self.update { $0.reverseRings.toggle() }
        })
        menu.addItem(choiceMenu("Quality", [("Battery saver", 0.4), ("Normal", 0.6), ("Sharp", 1.0)],
                                current: settings.scale) { v in self.update { $0.scale = v } })
        menu.addItem(choiceMenu("Frame rate", [("30 fps", 30), ("60 fps", 60)],
                                current: Double(settings.maxFPS)) { v in self.update { $0.maxFPS = Int(v) } })
        menu.addItem(.separator())

        menu.addItem(ActionItem("Pause", checked: paused) { self.togglePause() })
        menu.addItem(ActionItem("Launch at login", checked: SMAppService.mainApp.status == .enabled) {
            self.toggleLaunchAtLogin()
        })
        menu.addItem(ActionItem("Open Screen Recording settings…") {
            if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
                NSWorkspace.shared.open(url)
            }
        })
        menu.addItem(.separator())
        menu.addItem(ActionItem("Quit Kaleido Wallpaper", key: "q") { NSApp.terminate(nil) })
    }

    /// Ready-made custom palettes.
    private static let presets: [(String, [String])] = [
        ("Sunset", ["#FF5E3A", "#FF2A68", "#FFB13B", "#8E2DE2"]),
        ("Ocean", ["#00C6FF", "#0072FF", "#00F5D4", "#3A0CA3"]),
        ("Neon", ["#FF00E5", "#00FFF0", "#ADFF00", "#7B00FF"]),
        ("Forest", ["#2ECC71", "#A8E063", "#16A085", "#F4D03F"]),
        ("Ice", ["#E0F7FF", "#7FDBFF", "#5B8CFF", "#B388FF"])
    ]

    /// "Colors ▸": album colours by default, or the user's own four colours.
    private func colorsMenu() -> NSMenuItem {
        let item = NSMenuItem(title: "Colors", action: nil, keyEquivalent: "")
        let sub = NSMenu()
        let album = ActionItem("From album (default)", checked: !settings.useCustomColors) {
            self.update { $0.useCustomColors = false }
        }
        album.toolTip = "The cover's colors in album art mode, the rainbow otherwise."
        sub.addItem(album)
        let custom = ActionItem("Custom", checked: settings.useCustomColors) { self.update { $0.useCustomColors = true } }
        custom.image = ColorEditor.swatch(settings.customColors)
        sub.addItem(custom)
        sub.addItem(.separator())
        for (name, colors) in AppDelegate.presets {
            let preset = ActionItem(name, checked: settings.useCustomColors && settings.customColors == colors) {
                self.update { $0.customColors = colors; $0.useCustomColors = true }
            }
            preset.image = ColorEditor.swatch(colors)
            sub.addItem(preset)
        }
        sub.addItem(.separator())
        sub.addItem(ActionItem("Edit custom colors…") { self.colorEditor.show(colors: self.settings.customColors) })
        item.submenu = sub
        return item
    }

    /// "Beat effect ▸": what happens on a sudden hit. Color switch replaces the bounce.
    private func beatEffectMenu() -> NSMenuItem {
        let item = NSMenuItem(title: "Beat effect", action: nil, keyEquivalent: "")
        let sub = NSMenu()
        let s = settings
        let options: [(String, Bool, Double, Bool)] = [          // title, checked, bounce, color switch
            ("Off", !s.beatColorSwitch && s.beatBounce < 0.01, 0, false),
            ("Subtle bounce", !s.beatColorSwitch && abs(s.beatBounce - 0.4) < 0.01, 0.4, false),
            ("Bounce", !s.beatColorSwitch && abs(s.beatBounce - 1.0) < 0.01, 1.0, false),
            ("Color switch", s.beatColorSwitch, s.beatBounce, true)
        ]
        for (title, checked, bounce, colorSwitch) in options {
            sub.addItem(ActionItem(title, checked: checked) {
                self.update { $0.beatBounce = bounce; $0.beatColorSwitch = colorSwitch }
            })
        }
        item.submenu = sub
        return item
    }

    /// "React to ▸" with all running apps that have a user interface.
    private func targetMenu() -> NSMenuItem {
        let item = NSMenuItem(title: "React to", action: nil, keyEquivalent: "")
        let sub = NSMenu()
        sub.addItem(ActionItem("All computer audio", checked: settings.targetBundleID == nil) {
            self.update({ $0.targetBundleID = nil; $0.targetName = nil }, recapture: true)
        })
        sub.addItem(.separator())

        let myID = Bundle.main.bundleIdentifier
        var seen = Set<String>()
        let apps = NSWorkspace.shared.runningApplications
            .filter { $0.activationPolicy == .regular && $0.bundleIdentifier != nil && $0.bundleIdentifier != myID }
            .sorted { ($0.localizedName ?? "").localizedCaseInsensitiveCompare($1.localizedName ?? "") == .orderedAscending }

        for app in apps {
            guard let id = app.bundleIdentifier, seen.insert(id).inserted else { continue }
            let name = app.localizedName ?? id
            let entry = ActionItem(name, checked: settings.targetBundleID == id) {
                self.update({ $0.targetBundleID = id; $0.targetName = name }, recapture: true)
            }
            if let icon = app.icon {
                icon.size = NSSize(width: 16, height: 16)
                entry.image = icon
            }
            sub.addItem(entry)
        }
        // the chosen app is not running right now: keep it visible (and checked)
        if let id = settings.targetBundleID, !seen.contains(id) {
            sub.addItem(ActionItem("\(settings.targetName ?? id) (not running)", checked: true) {})
        }
        item.submenu = sub
        return item
    }

    private func choiceMenu(_ title: String, _ options: [(String, Double)], current: Double,
                            set: @escaping (Double) -> Void) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        let sub = NSMenu()
        for (name, value) in options {
            sub.addItem(ActionItem(name, checked: abs(current - value) < 0.001) { set(value) })
        }
        item.submenu = sub
        return item
    }

    private func togglePause() {
        paused.toggle()
        buildWindows()                      // paused: no windows, so the normal wallpaper shows again
        restartCapture()
        updateArtworkMode()
        if paused { status = "Paused" }
    }

    private func toggleLaunchAtLogin() {
        let service = SMAppService.mainApp
        do {
            if service.status == .enabled { try service.unregister() } else { try service.register() }
        } catch {
            let alert = NSAlert()
            alert.messageText = "Couldn't turn on Launch at login"
            alert.informativeText = "Move the app to your Applications folder first, then try again.\n\n\(error.localizedDescription)"
            alert.runModal()
        }
    }
}

/// NSMenuItem that runs a closure.
final class ActionItem: NSMenuItem {
    private let handler: () -> Void

    init(_ title: String, checked: Bool = false, key: String = "", handler: @escaping () -> Void) {
        self.handler = handler
        super.init(title: title, action: #selector(run), keyEquivalent: key)
        target = self
        state = checked ? .on : .off
    }

    required init(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    @objc private func run() { handler() }
}
