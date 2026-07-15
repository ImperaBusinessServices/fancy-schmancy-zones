using System.Drawing;
using System.Windows.Forms;

namespace FancySchmancyZones;

/// <summary>A tiny "give this layout a name" dialog.</summary>
public sealed class NameForm : Form
{
    private readonly TextBox _box;

    public string LayoutName => _box.Text.Trim();

    public NameForm(string title, string prompt, string initial = "")
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        // Nothing here is placed by hand. Every earlier version pinned each Left/Top/Width/Height to
        // pixels measured on a 100% monitor, so on a scaled-up screen the font grew but those numbers
        // didn't: the prompt overlapped the box and the Save/Cancel buttons fell off the bottom edge.
        // Telling the panel to measure the real controls and the form to size itself around them means
        // it cannot clip -- at any scaling, in any font, in any language.
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7f, 15f);   // Segoe UI 9pt at 100% -- the baseline this was drawn at
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var label = new Label { Text = prompt, AutoSize = true, Margin = new Padding(0, 0, 0, 7) };
        _box = new TextBox
        {
            Text = initial,
            Width = 340,
            // A name has to stay readable on a picker card; the card shrinks long ones to fit, but
            // there's no size at which a paragraph is a label. Generous enough never to bite a real
            // name (Keith's longest is 36).
            MaxLength = 60,
            Margin = new Padding(0, 0, 0, 14),
        };
        _box.SelectAll();

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(84, 0), Margin = new Padding(7, 0, 0, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(84, 0), Margin = new Padding(7, 0, 0, 0) };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            Location = new Point(0, 0),
        };
        layout.Controls.Add(label);
        layout.Controls.Add(_box);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>Show the dialog and return the typed name, or null if cancelled/blank.</summary>
    public static string? Ask(string title, string prompt, string initial = "")
    {
        using var f = new NameForm(title, prompt, initial);
        return f.ShowDialog() == DialogResult.OK && f.LayoutName.Length > 0 ? f.LayoutName : null;
    }
}
