using KaleidoWallpaper.Core;

namespace KaleidoWallpaper;

/// <summary>Small window with four colour buttons for the custom palette. Each pick applies immediately.</summary>
internal sealed class ColorEditorForm : Form
{
    private readonly Button[] _buttons = new Button[4];
    private readonly ColorDialog _dialog = new() { FullOpen = true, AnyColor = true };

    public event Action<List<string>>? ColorsChanged;

    public ColorEditorForm()
    {
        Text = "Custom colors";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(380, 140);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var label = new Label
        {
            Text = "Pick four colors. The kaleidoscope uses them instead of the album colors.",
            AutoSize = false, Location = new Point(18, 14), Size = new Size(344, 36)
        };
        Controls.Add(label);

        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var button = new Button
            {
                Location = new Point(18 + i * 88, 60), Size = new Size(76, 52),
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, TabIndex = i
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            button.Click += (_, _) => Pick(index);
            _buttons[i] = button;
            Controls.Add(button);
        }
    }

    public void ShowWith(IReadOnlyList<string> colors)
    {
        for (int i = 0; i < 4; i++) _buttons[i].BackColor = ToColor(i < colors.Count ? colors[i] : "#FFFFFF");
        if (!Visible) Show();
        Activate();
    }

    private void Pick(int index)
    {
        _dialog.Color = _buttons[index].BackColor;
        if (_dialog.ShowDialog(this) != DialogResult.OK) return;
        _buttons[index].BackColor = _dialog.Color;
        ColorsChanged?.Invoke(_buttons.Select(b => ToHex(b.BackColor)).ToList());
    }

    // keep the window for next time instead of disposing it
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color ToColor(string hex)
    {
        var (r, g, b) = Settings.Rgb(hex);
        return Color.FromArgb((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }

    /// <summary>16×16 menu icon showing the four colours as quadrants.</summary>
    public static Bitmap Swatch(IReadOnlyList<string> colors)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        var cells = new[] { new Rectangle(0, 0, 8, 8), new Rectangle(8, 0, 8, 8), new Rectangle(0, 8, 8, 8), new Rectangle(8, 8, 8, 8) };
        for (int i = 0; i < 4; i++)
        {
            using var brush = new SolidBrush(ToColor(i < colors.Count ? colors[i] : "#FFFFFF"));
            g.FillRectangle(brush, cells[i]);
        }
        return bmp;
    }
}
