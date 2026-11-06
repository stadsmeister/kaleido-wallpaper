import AppKit

/// Small window with four colour wells for the custom palette. Every change applies immediately.
final class ColorEditor: NSObject, NSWindowDelegate {
    var onChange: (([String]) -> Void)?
    private var window: NSWindow?
    private var wells: [NSColorWell] = []

    func show(colors: [String]) {
        if window == nil { build() }
        for (i, well) in wells.enumerated() where i < colors.count {
            well.color = ColorEditor.color(fromHex: colors[i])
        }
        NSApp.activate(ignoringOtherApps: true)            // menu-bar app: bring the window to the front
        window?.center()
        window?.makeKeyAndOrderFront(nil)
    }

    private func build() {
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 380, height: 150),
                         styleMask: [.titled, .closable], backing: .buffered, defer: false)
        w.title = "Custom colors"
        w.isReleasedWhenClosed = false
        w.delegate = self

        let label = NSTextField(wrappingLabelWithString:
            "Pick four colors. The kaleidoscope uses them instead of the album colors.")
        label.preferredMaxLayoutWidth = 340

        let row = NSStackView()
        row.orientation = .horizontal
        row.spacing = 14
        for _ in 0..<4 {
            let well = NSColorWell(frame: NSRect(x: 0, y: 0, width: 64, height: 40))
            well.isContinuous = true                          // live while dragging in the color panel
            well.target = self
            well.action = #selector(colorChanged(_:))
            well.translatesAutoresizingMaskIntoConstraints = false
            well.widthAnchor.constraint(equalToConstant: 64).isActive = true
            well.heightAnchor.constraint(equalToConstant: 40).isActive = true
            wells.append(well)
            row.addArrangedSubview(well)
        }

        let stack = NSStackView(views: [label, row])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 14
        stack.edgeInsets = NSEdgeInsets(top: 18, left: 20, bottom: 18, right: 20)
        w.contentView = stack
        window = w
    }

    @objc private func colorChanged(_ sender: NSColorWell) {
        onChange?(wells.map { ColorEditor.hex(from: $0.color) })
    }

    func windowWillClose(_ notification: Notification) {
        NSColorPanel.shared.orderOut(nil)                     // don't leave the shared color panel behind
    }

    static func hex(from color: NSColor) -> String {
        let c = color.usingColorSpace(.sRGB) ?? color
        func byte(_ v: CGFloat) -> Int { max(0, min(255, Int((v * 255).rounded()))) }
        return String(format: "#%02X%02X%02X", byte(c.redComponent), byte(c.greenComponent), byte(c.blueComponent))
    }

    static func color(fromHex hex: String) -> NSColor {
        let (r, g, b) = Settings.rgb(fromHex: hex)
        return NSColor(srgbRed: r, green: g, blue: b, alpha: 1)
    }

    /// 16×16 menu icon showing the four colours as quadrants.
    static func swatch(_ colors: [String]) -> NSImage {
        NSImage(size: NSSize(width: 16, height: 16), flipped: false) { rect in
            let half = rect.width / 2
            let cells = [NSRect(x: 0, y: half, width: half, height: half), NSRect(x: half, y: half, width: half, height: half),
                         NSRect(x: 0, y: 0, width: half, height: half), NSRect(x: half, y: 0, width: half, height: half)]
            NSBezierPath(roundedRect: rect, xRadius: 3, yRadius: 3).addClip()
            for (i, cell) in cells.enumerated() {
                color(fromHex: i < colors.count ? colors[i] : "#FFFFFF").setFill()
                cell.fill()
            }
            return true
        }
    }
}
