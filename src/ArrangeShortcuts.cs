using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FancySchmancyZones;

internal sealed class ArrangeShortcuts : NativeWindow, IDisposable
{
    internal static readonly (string Key, string Label, int Vk)[] Choices =
    {
        ("menu", "Ctrl+Alt+W — Open menu", 0x57),
        ("cascade", "Ctrl+Alt+C — Cascade current app", 0x43),
        ("grid", "Ctrl+Alt+G — Grid current app", 0x47),
        ("side", "Ctrl+Alt+S — Side by side current app", 0x53),
        ("undo", "Ctrl+Alt+Z — Undo arrangement", 0x5A),
        ("all", "Ctrl+Alt+A — Cascade all windows", 0x41)
    };
    private readonly Action<string> _action;
    private readonly HashSet<int> _registered = new();
    internal ArrangeShortcuts(Action<string> action)
    {
        _action = action;
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
    }
    internal List<string> Apply(IEnumerable<string> enabled)
    {
        foreach (int id in _registered) UnregisterHotKey(Handle, id);
        _registered.Clear();
        var failed = new List<string>();
        var keys = enabled.ToHashSet();
        for (int i = 0; i < Choices.Length; i++)
        {
            if (!keys.Contains(Choices[i].Key)) continue;
            if (RegisterHotKey(Handle, i + 1, 0x4003, Choices[i].Vk)) _registered.Add(i + 1);
            else failed.Add(Choices[i].Label);
        }
        return failed;
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && _registered.Contains(m.WParam.ToInt32()))
            _action(Choices[m.WParam.ToInt32() - 1].Key);
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        foreach (int id in _registered) UnregisterHotKey(Handle, id);
        DestroyHandle();
    }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, int vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
