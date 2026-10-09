using FancySchmancyZones;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

internal static class Check
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var work = new Rectangle(-1600, 0, 1600, 900);
        foreach (bool down in new[] { false, true })
        for (int n = 1; n <= 40; n++)
        {
            var cells = Arrange.Compute(Arrange.Shape.Grid, work, n, down);
            Require(cells.Count == n, "cell count");
            foreach (var cell in cells)
                Require(work.Contains(new Rectangle(cell.X, cell.Y, cell.W, cell.H)), "cell inside monitor");
            Require(cells.Select(c => (c.X, c.Y)).Distinct().Count() == n, "no duplicate cells");
        }
        var across = Arrange.Compute(Arrange.Shape.Grid, work, 6);
        var vertical = Arrange.Compute(Arrange.Shape.Grid, work, 6, true);
        Require(across[1].Y == across[0].Y && across[1].X > across[0].X, "across-first order");
        Require(vertical[1].X == vertical[0].X && vertical[1].Y > vertical[0].Y, "down-first order");
        var settings = new AppSettings();
        Require(!settings.ArrangeGroupByApp && !settings.ArrangeGridDownFirst && settings.ArrangeShortcuts.Count == 0, "existing defaults");
        var type = typeof(Arrange).Assembly.GetType("FancySchmancyZones.ArrangeSettingsForm")!;
        using var form = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { settings }, null)!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((CheckBox)type.GetField("_group", flags)!.GetValue(form)!).Checked = true;
        ((CheckBox)type.GetField("_down", flags)!.GetValue(form)!).Checked = true;
        ((ComboBox)type.GetField("_target", flags)!.GetValue(form)!).SelectedIndex = 1;
        type.GetMethod("Apply", flags)!.Invoke(form, new object[] { settings });
        Require(settings.ArrangeGroupByApp && settings.ArrangeGridDownFirst && settings.ArrangeTarget == "current", "dialog saves preferences");
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Require(restored.ArrangeTarget == "current" && restored.ArrangeGridDownFirst, "preferences round trip");
        var darkField = type.GetField("_dark", flags)!;
        foreach (bool dark in new[] { false, true })
        {
            darkField.SetValue(form, dark);
            type.GetMethod("PaintTheme", flags)!.Invoke(form, new object[] { form });
            Require(dark ? form.BackColor.GetBrightness() < .2 && form.ForeColor.GetBrightness() > .8
                : form.BackColor.GetBrightness() > .9 && form.ForeColor.GetBrightness() < .2, "theme contrast");
        }
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000);
        form.Show();
        Application.DoEvents();
        var targetBox = (ComboBox)type.GetField("_target", flags)!.GetValue(form)!;
        Require(targetBox.Items.Cast<object>().All(item => TextRenderer.MeasureText(item.ToString(), targetBox.Font).Width
            + SystemInformation.VerticalScrollBarWidth < targetBox.Width), "all monitor option labels fit");
        foreach (bool dark in new[] { true, false })
        {
            darkField.SetValue(form, dark);
            type.GetMethod("PaintTheme", flags)!.Invoke(form, new object[] { form });
            form.PerformLayout();
            CheckBounds(form);
            Require(form.AcceptButton is Button save && save.Visible &&
                form.RectangleToClient(save.RectangleToScreen(save.ClientRectangle)).Bottom <= form.ClientSize.Height, "Save visible without scrolling");
            Require(form.CancelButton is Button cancel && cancel.Visible, "Cancel visible");
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, dark ? "preview-dark.png" : "preview-light.png"));
            Console.WriteLine($"Preview {(dark ? "dark" : "light")}: {form.Width} × {form.Height} at {form.DeviceDpi} DPI");
        }
        foreach (float scale in new[] { 1.25f, 1.2f })
        {
            form.Scale(new SizeF(scale, scale));
            form.PerformLayout();
            CheckBounds(form);
        }
        form.Hide();
        Console.WriteLine("PASS: grid, preferences, themes, layout bounds, visible Save/Cancel, and increased display scaling. No live windows arranged.");
    }
    static void CheckBounds(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            Require(parent.ClientRectangle.Contains(child.Bounds), $"clipped {child.GetType().Name}: {child.Text} {child.Bounds} inside {parent.ClientRectangle}");
            Require(child is not FlowLayoutPanel flow || !flow.AutoScroll, "no scrolling required");
            CheckBounds(child);
        }
    }
    static void Require(bool passed, string name) { if (!passed) throw new Exception(name); }
}
