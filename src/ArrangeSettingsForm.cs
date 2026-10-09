using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FancySchmancyZones;

internal sealed class ArrangeSettingsForm : Form
{
    private readonly CheckBox _group = new() { Text = "Keep windows of the same app together", AutoSize = true };
    private readonly CheckBox _down = new() { Text = "Grid fills down first, then across", AutoSize = true };
    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 };
    private readonly List<string> _targets = new() { "all", "current" };
    private readonly List<(string Key, CheckBox Box)> _shortcuts = new();
    private bool _dark;
    internal ArrangeSettingsForm(AppSettings settings)
    {
        Text = "Arrange windows — Settings";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(510, 570);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20),
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Controls.Add(panel);
        var theme = new Button { Text = "Switch light / dark", AutoSize = true };
        theme.Click += (_, _) => { _dark = !_dark; PaintTheme(this); };
        panel.Controls.Add(theme);
        panel.Controls.Add(new Label { Text = "Arrangement", AutoSize = true, Margin = new Padding(3, 12, 3, 8) });
        _group.Checked = settings.ArrangeGroupByApp; _down.Checked = settings.ArrangeGridDownFirst;
        panel.Controls.Add(_group); panel.Controls.Add(_down);
        panel.Controls.Add(new Label { Text = "Arrange an app or browser profile on:", AutoSize = true, Margin = new Padding(3, 12, 3, 5) });
        _target.Items.AddRange(new object[] { "Spread across all monitors", "Keep each window on its current monitor" });
        var screens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToList();
        for (int i = 0; i < screens.Count; i++)
        {
            _targets.Add($"monitor:{i}");
            _target.Items.Add($"Monitor {i + 1} only ({screens[i].Bounds.Width} × {screens[i].Bounds.Height})");
        }
        _target.SelectedIndex = Math.Max(0, _targets.IndexOf(settings.ArrangeTarget));
        panel.Controls.Add(_target);
        panel.Controls.Add(new Label { Text = "All windows stays on its existing monitors.", AutoSize = true });
        panel.Controls.Add(new Label { Text = "Keyboard shortcuts", AutoSize = true, Margin = new Padding(3, 16, 3, 8) });
        foreach (var choice in ArrangeShortcuts.Choices)
        {
            var box = new CheckBox { Text = choice.Label, AutoSize = true,
                Checked = settings.ArrangeShortcuts.Contains(choice.Key) };
            _shortcuts.Add((choice.Key, box)); panel.Controls.Add(box);
        }
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(3, 16, 3, 3) };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); panel.Controls.Add(buttons);
        AcceptButton = save; CancelButton = cancel;
        try { _dark = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0; } catch { }
        PaintTheme(this);
    }
    internal void Apply(AppSettings settings)
    {
        settings.ArrangeGroupByApp = _group.Checked;
        settings.ArrangeGridDownFirst = _down.Checked;
        settings.ArrangeTarget = _targets[_target.SelectedIndex];
        settings.ArrangeShortcuts = _shortcuts.Where(s => s.Box.Checked).Select(s => s.Key).ToList();
    }
    private void PaintTheme(Control control)
    {
        control.BackColor = _dark ? Color.FromArgb(28, 31, 40) : Color.FromArgb(247, 248, 250);
        control.ForeColor = _dark ? Color.FromArgb(235, 237, 242) : Color.FromArgb(30, 35, 45);
        if (control is Button button) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = _dark ? Color.Gray : Color.Silver; }
        if (control is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
        foreach (Control child in control.Controls) PaintTheme(child);
    }
}
