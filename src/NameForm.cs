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

        // Every Left/Top/Width below is hand-picked for a 96 DPI (100% scaling) screen. Without
        // this, Windows renders the dialog's font bigger on a scaled-up monitor (125%/150%/200%)
        // but leaves these raw pixel positions untouched, so the prompt text, box, and buttons
        // overlap. AutoScaleMode.Dpi tells WinForms to scale the whole form — size, control
        // positions, and font — together to match whatever monitor it opens on.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(360, 120);

        var label = new Label { Text = prompt, Left = 14, Top = 14, Width = 332, AutoSize = false, Height = 20 };
        _box = new TextBox { Left = 14, Top = 40, Width = 332, Text = initial };
        _box.SelectAll();

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 186, Top = 78, Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 271, Top = 78, Width = 75 };

        Controls.AddRange(new Control[] { label, _box, ok, cancel });
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
