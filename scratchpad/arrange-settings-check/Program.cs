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
        Console.WriteLine("PASS: 80 grid scenarios, fill direction, existing defaults, dialog save, persistence, both themes. No live windows arranged.");
    }
    static void Require(bool passed, string name) { if (!passed) throw new Exception(name); }
}
