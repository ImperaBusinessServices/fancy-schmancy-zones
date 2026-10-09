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
        form.Dispose();
        CheckMenuOpening();
        Console.WriteLine("PASS: grid, preferences, themes, layout bounds, visible Save/Cancel, and increased display scaling. No live windows arranged.");
    }
    static void CheckMenuOpening()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var trayType = typeof(Arrange).Assembly.GetType("FancySchmancyZones.TrayContext")!;
        var open = trayType.GetMethod("ShowArrangeSettings", flags)!;
        foreach (string entry in new[] { "Settings", "Arrange windows" })
        {
            // Run the real tray handler with isolated state. No tray icon or global hook,
            // no saved layouts loaded, and Cancel prevents any writes to settings.
            object tray = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(trayType);
            using var sync = new Form { ShowInTaskbar = false };
            _ = sync.Handle;
            using var menu = new ContextMenuStrip();
            trayType.GetField("_sync", flags)!.SetValue(tray, sync);
            trayType.GetField("_menu", flags)!.SetValue(tray, menu);
            trayType.GetField("_state", flags)!.SetValue(tray, new AppState());
            var root = new ToolStripMenuItem(entry);
            var item = new ToolStripMenuItem("Arrangement settings");
            root.DropDownItems.Add(item); menu.Items.Add(root);
            item.Click += (_, _) => open.Invoke(tray, null);
            Exception? failure = null;
            bool observed = false;
            using var inspect = new System.Windows.Forms.Timer { Interval = 60 };
            inspect.Tick += (_, _) =>
            {
                inspect.Stop();
                Form? dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "ArrangeSettingsForm");
                try
                {
                    Require(dialog is { Visible: true }, "first click shows settings");
                    Require(!menu.Visible, "menu closed before settings show");
                    Require(GetWindow(dialog!.Handle, 4) == sync.Handle, "permanent owner, not popup");
                    Require(!dialog.TopMost, "dialog does not stay always-on-top");
                    item.PerformClick();
                    Require(Application.OpenForms.Cast<Form>().Count(f => f.GetType().Name == "ArrangeSettingsForm") == 1, "repeat click reuses dialog");
                    observed = true;
                }
                catch (Exception ex) { failure = ex; }
                finally { if (dialog != null) { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); } }
            };
            menu.Show(new Point(-20000, -20000));
            Application.DoEvents();
            item.PerformClick();
            item.PerformClick(); // second queued click must not produce a second modal window
            Require(!Application.OpenForms.Cast<Form>().Any(f => f.GetType().Name == "ArrangeSettingsForm"), "opening deferred until click returns");
            inspect.Start();
            Application.DoEvents();
            if (failure != null) throw failure;
            Require(observed, "opening inspected");
            Require(trayType.GetField("_arrangeSettingsDialog", flags)!.GetValue(tray) == null, "closed window cleared");
            Console.WriteLine($"PASS: {entry} first click, menu closed, stable owner, duplicate click, refocus, Cancel cleanup.");
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetWindow(IntPtr hwnd, uint command);
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
