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
    private readonly Button _theme = new() { AutoSize = true, MinimumSize = new Size(110, 34) };
    private bool _dark;
    internal ArrangeSettingsForm(AppSettings settings)
    {
        Text = "Arrange windows — Settings";
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7f, 15f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 3, Padding = new Padding(20), Margin = new Padding(0) };
        Controls.Add(layout);
        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 18) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label { Text = "Arrangement settings", AutoSize = true,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold), Margin = new Padding(0, 0, 12, 0) };
        header.Controls.Add(title, 0, 0);
        _theme.Click += (_, _) => { _dark = !_dark; PaintTheme(this); };
        header.Controls.Add(_theme, 1, 0);
        layout.Controls.Add(header, 0, 0);
        var body = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        layout.Controls.Add(body, 0, 1);
        FlowLayoutPanel Card() => new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16), Name = "card" };
        var panel = Card();
        panel.Margin = new Padding(0, 0, 16, 0);
        body.Controls.Add(panel, 0, 0);
        Label Heading(string text) => new() { Text = text, AutoSize = true,
            Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 0, 3, 12) };
        panel.Controls.Add(Heading("Layout"));
        _group.Margin = new Padding(3, 0, 3, 12);
        _down.Margin = new Padding(3, 0, 3, 20);
        _group.Checked = settings.ArrangeGroupByApp; _down.Checked = settings.ArrangeGridDownFirst;
        panel.Controls.Add(_group); panel.Controls.Add(_down);
        panel.Controls.Add(Heading("Monitor"));
        panel.Controls.Add(new Label { Text = "Arrange an app or browser profile on:", AutoSize = true, Margin = new Padding(3, 0, 3, 8) });
        _target.Width = 330;
        _target.DrawMode = DrawMode.OwnerDrawFixed;
        _target.DrawItem += (_, e) =>
        {
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            Color background = selected ? SystemColors.Highlight : _target.BackColor;
            Color foreground = selected ? SystemColors.HighlightText : _target.ForeColor;
            using var brush = new SolidBrush(background);
            e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, _target.Items[e.Index]?.ToString() ?? "", _target.Font,
                    e.Bounds, foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        _target.Items.AddRange(new object[] { "Spread across all monitors", "Keep each window on its current monitor" });
        var screens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToList();
        for (int i = 0; i < screens.Count; i++)
        {
            _targets.Add($"monitor:{i}");
            _target.Items.Add($"Monitor {i + 1} only ({screens[i].Bounds.Width} × {screens[i].Bounds.Height})");
        }
        _target.SelectedIndex = Math.Max(0, _targets.IndexOf(settings.ArrangeTarget));
        panel.Controls.Add(_target);
        panel.Controls.Add(new Label { Text = "“All windows” keeps each window on its current monitor.",
            AutoSize = true, MaximumSize = new Size(330, 0), Name = "hint", Margin = new Padding(3, 10, 3, 0) });
        var shortcutPanel = Card();
        shortcutPanel.Margin = new Padding(0);
        body.Controls.Add(shortcutPanel, 1, 0);
        shortcutPanel.Controls.Add(Heading("Keyboard shortcuts"));
        foreach (var choice in ArrangeShortcuts.Choices)
        {
            var box = new CheckBox { Text = choice.Label, AutoSize = true,
                Checked = settings.ArrangeShortcuts.Contains(choice.Key), Margin = new Padding(3, 0, 3, 12) };
            _shortcuts.Add((choice.Key, box)); shortcutPanel.Controls.Add(box);
        }
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = new Padding(0, 20, 0, 0) };
        var save = new Button { Text = "Save", Name = "primary", DialogResult = DialogResult.OK,
            AutoSize = true, MinimumSize = new Size(96, 36), Margin = new Padding(8, 0, 0, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel,
            AutoSize = true, MinimumSize = new Size(96, 36), Margin = new Padding(0) };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 2);
        AcceptButton = save; CancelButton = cancel;
        try { _dark = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0; } catch { }
        PaintTheme(this);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetTitleTheme();
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        int width = _target.Items.Cast<object>().Max(item => TextRenderer.MeasureText(item.ToString(), _target.Font).Width)
            + SystemInformation.VerticalScrollBarWidth + 24;
        _target.MinimumSize = new Size(width, 0);
        _target.Width = width;
        _target.DropDownWidth = width;
        PerformLayout();
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
        if (control.Name == "card") control.BackColor = _dark ? Color.FromArgb(37, 41, 53) : Color.White;
        else if (control.Parent?.Name == "card") control.BackColor = control.Parent.BackColor;
        if (control.Name == "hint") control.ForeColor = _dark ? Color.FromArgb(185, 194, 213) : Color.FromArgb(82, 91, 110);
        if (control is Button button) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = _dark ? Color.Gray : Color.Silver; }
        if (control.Name == "primary")
        {
            control.BackColor = _dark ? Color.FromArgb(110, 174, 250) : Color.FromArgb(24, 94, 184);
            control.ForeColor = _dark ? Color.FromArgb(12, 25, 42) : Color.White;
        }
        if (control is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
        foreach (Control child in control.Controls) PaintTheme(child);
        if (control == this) { _theme.Text = _dark ? "☀ Light mode" : "☾ Dark mode"; SetTitleTheme(); }
    }
    private void SetTitleTheme()
    {
        if (!IsHandleCreated) return;
        int dark = _dark ? 1 : 0;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
