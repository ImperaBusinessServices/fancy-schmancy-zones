using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FancySchmancyZones;

/// <summary>
/// A full-screen overlay that shows every layout as a clickable card — the "Pick from cards"
/// double-tap-Ctrl mode. Each card shows the layout's name and a little map of where its windows
/// sit, so they're easy to tell apart. Click a card (or press 1–9) to switch to it. Drag a card to
/// a new spot to reorder the layouts for good (the 1–9 keys and the tray list follow). Right-click
/// a card to update, rename, or delete that layout; right-click anywhere else to save the current
/// windows as a brand-new layout. Esc, or clicking away, cancels. It takes focus on purpose
/// (unlike the OsdForm flash) so it can receive the clicks and the Esc key.
/// </summary>
internal sealed class LayoutPickerForm : Form
{
    private static LayoutPickerForm? _current;   // only ever one open at a time

    /// <summary>Is the picker currently on screen? The tray app uses this to suspend background
    /// flips (Ctrl chords, a pending settle) so nothing rearranges windows behind the open picker.</summary>
    public static bool IsOpen => _current is { IsDisposed: false };

    /// <summary>Everything the picker can do to a layout, keyed by layout NAME rather than index —
    /// the picker can delete cards while it's open, so list positions shift under it; a name still
    /// finds the right layout afterward. All callbacks run on the UI thread.</summary>
    public sealed record PickerActions(
        Action<string> Switch,
        Action<string> Update,
        Action<string> Rename,
        Action<string> Delete,
        Action SaveNew,
        Action<IReadOnlyList<string>> Reorder);

    private readonly PickerActions _actions;
    private int _count;
    private readonly Label _title;
    private readonly Label _hint;
    private readonly FlowLayoutPanel _flow;

    // The right-click menus. Created once, closed when the picker closes; disposal is deliberately
    // left to the GC — disposing a menu while its own Click handler is still on the stack is how
    // the v0.9.2 tray right-click silently died, so we never dispose one synchronously here.
    private readonly List<ContextMenuStrip> _menus = new();
    private int _openMenus;      // >0 while a right-click menu is showing (see WantsKey)
    private long _menuGraceUntil; // tick until which a deactivate is blamed on a menu, not the user

    /// <summary>How much bigger this monitor draws things than a 100% one (2.0 at 200%).</summary>
    private readonly float _s;

    private const int MONITOR_DEFAULTTONEAREST = 2, MDT_EFFECTIVE_DPI = 0;
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point pt, int flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);

    /// <summary>The scaling of the monitor the picker is opening on. The overlay's title and hint
    /// are point-sized, so Windows already draws those bigger on a scaled-up display — but the
    /// cards are laid out in raw pixels, so without this they stay physically half-size next to
    /// their own heading on a 200% screen (Keith, looking at exactly that: "this text is so
    /// small"). Everything the cards draw is multiplied by this, so the whole picker grows
    /// together.</summary>
    private static float DpiScaleFor(Screen screen)
    {
        try
        {
            var mon = MonitorFromPoint(new Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1), MONITOR_DEFAULTTONEAREST);
            if (GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96f;
        }
        catch { /* shcore is Windows 8.1+; on anything older 100% is the right answer anyway */ }
        return 1f;
    }

    /// <summary>One card's data: the layout name and the bounds of the windows that are actually
    /// open right now (closed ones are left out so the card previews what a flip would really do).</summary>
    public sealed record CardInfo(string Name, IReadOnlyList<Rect> OpenWindows);

    /// <summary>Show the picker (or re-focus the one already open).</summary>
    public static void Show(IReadOnlyList<CardInfo> cards, int currentIndex, PickerActions actions)
    {
        if (cards.Count == 0) return;
        if (_current is { IsDisposed: false })
        {
            _current.Activate();
            return;
        }
        var f = new LayoutPickerForm(cards, currentIndex, actions);
        _current = f;
        f.FormClosed += (_, _) => { if (ReferenceEquals(_current, f)) _current = null; };
        f.Show();
        f.Activate();
    }

    private LayoutPickerForm(IReadOnlyList<CardInfo> cards, int currentIndex, PickerActions actions)
    {
        _actions = actions;
        _count = cards.Count;

        var screen = Screen.FromPoint(Cursor.Position);
        _s = DpiScaleFor(screen);

        // We scale the cards ourselves against the monitor we're opening on (_s). Say so, so a
        // future AutoScaleDimensions can't quietly scale them a second time on top.
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(16, 16, 20);
        Opacity = 0.95;

        _title = new Label
        {
            Text = "Pick a layout",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize = true
        };
        _hint = new Label
        {
            Text = "Click a card  ·  press 1–9  ·  drag a card to put it in a new spot  ·  Esc to cancel\n" +
                   "Right-click a card to update, rename or delete it  ·  right-click the background to save a new layout",
            Font = new Font("Segoe UI", 11.5f),
            ForeColor = Color.FromArgb(165, 165, 175),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = true
        };
        // NOTE: AutoSize + AutoScroll on a FlowLayoutPanel fight each other (mis-measures and drops
        // the last card behind a phantom scrollbar). Use AutoSize alone; MaximumSize.Width (set in
        // LayoutContents) drives wrapping, so cards flow into as many rows as needed with no scrollbar.
        _flow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            BackColor = Color.Transparent
        };

        for (int i = 0; i < cards.Count; i++)
        {
            var card = new Card(cards[i].Name, cards[i].OpenWindows, i + 1, i == currentIndex, _s);
            // Press-move-release is a REORDER; press-release on the spot is a PICK. JustDragged is
            // what keeps the drop from also counting as "switch to this layout".
            card.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginPress(card, e); };
            card.MouseMove += (_, e) => DragTo(card, e);
            card.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) EndDrag(); };
            card.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left && !JustDragged) PickCard(card); };
            card.ContextMenuStrip = BuildCardMenu(card);
            _flow.Controls.Add(card);
        }

        Controls.Add(_flow);
        Controls.Add(_title);
        Controls.Add(_hint);

        // Left-clicking anywhere that isn't a card cancels (backdrop, title, hint, panel gaps). A
        // child control's clicks don't bubble to the Form, so wire the non-card controls explicitly.
        // (JustDragged: releasing a dragged card out over the backdrop must not also cancel.)
        void CancelOnLeftClick(Control c) =>
            c.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left && !JustDragged) Close(); };
        CancelOnLeftClick(this);
        CancelOnLeftClick(_title);
        CancelOnLeftClick(_hint);
        CancelOnLeftClick(_flow);

        // Right-clicking anything that isn't a card offers "save these windows as a new layout".
        // Set on the Form only: WM_CONTEXTMENU bubbles up from the labels and the flow panel on its
        // own, and the cards intercept it first with their own menus.
        var back = NewMenu();
        back.Items.Add(Item("Save current windows as a new layout…", () => { Close(); _actions.SaveNew(); }));
        ContextMenuStrip = back;

        // If the picker closes while a right-click menu is up (Esc arrives via the global hook),
        // take the menu down with it — an orphaned menu floating over the desktop looks broken.
        // Same for a half-finished drag: the floating card goes with it.
        FormClosed += (_, _) => { foreach (var m in _menus) m.Close(); _menus.Clear(); KillGhost(); };
    }

    // ---- Drag a card to reorder ----
    //
    // Deliberately hand-rolled (mouse capture) rather than WinForms' DoDragDrop: that spins its own
    // modal message loop, which on a topmost overlay owned by a tray app — one Windows usually
    // refuses to give real focus — is exactly where the global keyboard hook and the deactivate
    // guard start fighting each other. Capture keeps the whole gesture inside our own message loop.
    //
    // While the drag runs, the card itself stays IN the flow panel as a dashed "it'll land here"
    // slot that hops between positions, and a snapshot of it (the ghost) floats under the cursor.

    private Card? _pressed;          // the card the mouse went down on — may never become a drag
    private Point _pressScreenPt;    // where the press happened, in SCREEN pixels (see DragTo)
    private Point _grabOffset;       // where inside the card it was grabbed, so the ghost hangs right
    private bool _dragging;          // past the wiggle threshold: a real drag is under way
    private long _dragEndedTick;     // when the last drag finished (see JustDragged)
    private DragGhost? _ghost;

    /// <summary>Did a drag just end? The mouse-up that drops a card is also delivered as a Click,
    /// and that click must not double as "switch to this layout" (or, out over the backdrop, as
    /// "cancel"). Time-boxed rather than a flag so it always clears itself, even if the click the
    /// flag was waiting for never arrives.</summary>
    private bool JustDragged => Environment.TickCount64 - _dragEndedTick < 250;

    private void BeginPress(Card card, MouseEventArgs e)
    {
        _pressed = card;
        _dragging = false;
        _pressScreenPt = Cursor.Position;
        _grabOffset = e.Location;
    }

    private void DragTo(Card card, MouseEventArgs e)
    {
        if (!ReferenceEquals(_pressed, card) || (e.Button & MouseButtons.Left) == 0) return;

        // Screen coordinates throughout: e.Location is relative to the card, and the card moves out
        // from under the cursor mid-drag, so card-relative maths would jitter against itself.
        var now = Cursor.Position;
        if (!_dragging)
        {
            int slop = Math.Max(SystemInformation.DragSize.Width, (int)(6 * _s));
            if (Math.Abs(now.X - _pressScreenPt.X) < slop && Math.Abs(now.Y - _pressScreenPt.Y) < slop) return;
            StartDrag(card);
        }
        var p = PointToClient(now);
        _ghost!.Location = new Point(p.X - _grabOffset.X, p.Y - _grabOffset.Y);
        MoveCardTo(card, DropIndexFor(card, _flow.PointToClient(now)));
    }

    private void StartDrag(Card card)
    {
        _dragging = true;
        card.Capture = true;              // keep the moves coming once the pointer leaves the card
        card.Cursor = Cursors.SizeAll;
        _ghost = DragGhost.Of(card);      // snapshot FIRST — before the card turns into a placeholder
        Controls.Add(_ghost);
        _ghost.BringToFront();
        card.SetPlaceholder(true);
    }

    private void EndDrag()
    {
        var card = _pressed;
        _pressed = null;
        if (card is null || card.IsDisposed) return;
        card.Capture = false;
        if (!_dragging) return;           // a plain click: leave it to the Click handler to pick

        _dragging = false;
        _dragEndedTick = Environment.TickCount64;
        card.Cursor = Cursors.Hand;
        card.SetPlaceholder(false);
        KillGhost();
        // Save the new order. By NAME, like every other picker action — the layout list can have
        // shifted underneath us while the picker sat open.
        _actions.Reorder(_flow.Controls.OfType<Card>().Select(c => c.LayoutName).ToList());
    }

    private void KillGhost()
    {
        if (_ghost is null) return;
        Controls.Remove(_ghost);
        _ghost.Dispose();
        _ghost = null;
    }

    /// <summary>Which slot the dragged card should sit in for this cursor position. Walks the OTHER
    /// cards in display order and stops at the first one the cursor is "before" — past the halfway
    /// line of a card on the cursor's own row, or anywhere on a row below it (the cards wrap onto
    /// several rows once there are enough of them).</summary>
    private int DropIndexFor(Card dragged, Point ptInFlow)
    {
        var others = _flow.Controls.OfType<Card>().Where(c => !ReferenceEquals(c, dragged)).ToList();
        for (int i = 0; i < others.Count; i++)
        {
            var b = others[i].Bounds;
            if (ptInFlow.Y < b.Top || (ptInFlow.Y <= b.Bottom && ptInFlow.X < b.Left + b.Width / 2)) return i;
        }
        return others.Count;   // past everything — drop at the end
    }

    /// <summary>Slide the card into slot n. The flow panel holds nothing but cards, so an index into
    /// "the others" and a child index are the same number once the card is re-inserted.</summary>
    private void MoveCardTo(Card card, int n)
    {
        if (n < 0 || n == _flow.Controls.GetChildIndex(card)) return;
        _flow.SuspendLayout();
        _flow.Controls.SetChildIndex(card, n);
        _flow.ResumeLayout(true);
        Renumber();                       // keep the 1–9 badges honest as the cards shuffle
    }

    /// <summary>Re-badge every card 1, 2, 3… in its current position (after a drag or a delete).</summary>
    private void Renumber()
    {
        int n = 0;
        foreach (var c in _flow.Controls.OfType<Card>()) c.SetNumber(++n <= 9 ? n : 0);
    }

    /// <summary>The card that floats under the cursor while it's being dragged: just a snapshot of
    /// the real one, lifted onto the form above the flow panel. Disabled so it can't swallow a
    /// click, though mouse capture means every message is going to the dragged card anyway.</summary>
    private sealed class DragGhost : Control
    {
        private readonly Bitmap _shot;

        private DragGhost(Bitmap shot)
        {
            _shot = shot;
            Size = shot.Size;
            Enabled = false;
            DoubleBuffered = true;
        }

        public static DragGhost Of(Card card)
        {
            var shot = new Bitmap(card.Width, card.Height);
            card.DrawToBitmap(shot, new Rectangle(0, 0, card.Width, card.Height));
            return new DragGhost(shot);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(_shot, 0, 0);
            using var pen = new Pen(Color.FromArgb(150, 195, 255), 2f);
            e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _shot.Dispose();
            base.Dispose(disposing);
        }
    }

    // ---- Right-click menus ----

    private ContextMenuStrip BuildCardMenu(Card card)
    {
        var m = NewMenu();
        m.Items.Add(new ToolStripMenuItem(card.LayoutName) { Enabled = false });   // header: which card
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item("Switch to this layout", () => PickCard(card)));
        m.Items.Add(Item("Update it to my current windows", () => { Close(); _actions.Update(card.LayoutName); }));
        m.Items.Add(Item("Rename…", () => { Close(); _actions.Rename(card.LayoutName); }));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item("Delete this layout", () => DeleteCard(card)));
        return m;
    }

    private ContextMenuStrip NewMenu()
    {
        var m = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = MenuBack,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f),
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false }
        };
        m.Opened += (_, _) => { _openMenus++; _menuGraceUntil = long.MaxValue; };
        m.Closed += (_, _) => { _openMenus--; _menuGraceUntil = Environment.TickCount64 + 600; };
        _menus.Add(m);
        return m;
    }

    private static ToolStripMenuItem Item(string text, Action onClick)
    {
        var it = new ToolStripMenuItem(text) { ForeColor = Color.White };
        it.Click += (_, _) => onClick();
        return it;
    }

    private static readonly Color MenuBack = Color.FromArgb(34, 34, 40);

    /// <summary>Match the menus to the overlay — the stock white WinForms menu on this near-black
    /// backdrop looked like a rendering glitch, not a feature.</summary>
    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        private static readonly Color Hover = Color.FromArgb(55, 58, 72);
        private static readonly Color Line = Color.FromArgb(70, 70, 82);
        public override Color ToolStripDropDownBackground => MenuBack;
        public override Color ImageMarginGradientBegin => MenuBack;
        public override Color ImageMarginGradientMiddle => MenuBack;
        public override Color ImageMarginGradientEnd => MenuBack;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color MenuBorder => Line;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
    }

    // ---- Actions ----

    // Close BEFORE acting, so the overlay is gone by the time windows shuffle or a dialog opens.
    private void PickCard(Card card) { Close(); _actions.Switch(card.LayoutName); }

    /// <summary>Pick the nth card as currently shown (keyboard 1–9). Goes through the flow panel's
    /// live order, not the list the picker opened with — deletes may have shifted the cards.</summary>
    private void Pick(int n)
    {
        var cards = _flow.Controls.OfType<Card>().ToList();
        if (n >= 0 && n < cards.Count) PickCard(cards[n]);
    }

    /// <summary>Right-click → Delete: remove the layout AND its card, keeping the picker open (so
    /// several can be pruned in one visit). The other cards renumber so the 1–9 keys stay true.</summary>
    private void DeleteCard(Card card)
    {
        _actions.Delete(card.LayoutName);          // the part that matters, done first
        if (IsDisposed || Disposing) return;       // picker somehow closed mid-click — layout's gone, UI's gone
        if (ReferenceEquals(_pressed, card)) { _pressed = null; KillGhost(); _dragging = false; }
        _flow.Controls.Remove(card);
        card.Dispose();
        _count--;
        if (_count <= 0) { Close(); return; }
        Renumber();
        LayoutContents();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutContents();
        // Best-effort only. A tray (background) app is usually DENIED real foreground focus by
        // Windows, so we do NOT depend on focus: Esc and 1–9 are delivered by the app's global
        // keyboard hook (see WantsKey/PressKey), and mouse clicks work on a topmost window anyway.
        BringToFront();
        Activate();
    }

    // If the picker ever does hold focus and then loses it (e.g. a mouse click activated it, then
    // the user clicked another app), don't leave a full-screen overlay stranded — cancel.
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // ...unless a right-click menu is up or just closed: a closing menu can bounce activation
        // to another app before a tray app is allowed to reclaim it (verified live — Esc on a card
        // menu was tearing down the whole picker). The picker works fine without focus anyway (the
        // global hook feeds it keys), so staying open here matches the no-focus case, not a leak.
        if (_openMenus > 0 || Environment.TickCount64 < _menuGraceUntil) return;
        if (!IsDisposed && !Disposing) Close();
    }

    // ---- Keyboard delivered by the app's global hook (works even though we usually lack focus) ----

    private const int VK_ESCAPE = 0x1B, VK_1 = 0x31, VK_9 = 0x39, VK_NUMPAD1 = 0x61, VK_NUMPAD9 = 0x69;

    /// <summary>Does the open picker want this virtual-key? (Esc, or 1–9 / numpad 1–9.) While a
    /// right-click menu is up the answer is NO: the menu has real focus (the click that opened it
    /// activated us), so Esc should close just the menu and digits belong to it — the hook
    /// swallowing them would close the whole picker out from under the open menu.</summary>
    public static bool WantsKey(int vk)
    {
        var f = _current;
        if (f is null || f.IsDisposed || f._openMenus > 0) return false;
        return vk == VK_ESCAPE || (vk >= VK_1 && vk <= VK_9) || (vk >= VK_NUMPAD1 && vk <= VK_NUMPAD9);
    }

    /// <summary>Act on a key the global hook routed to us (call on key-DOWN). Marshals to the UI thread.</summary>
    public static void PressKey(int vk)
    {
        var f = _current;
        if (f is null || f.IsDisposed) return;
        try
        {
            if (vk == VK_ESCAPE) { f.BeginInvoke((Action)f.Close); return; }
            int n = vk >= VK_NUMPAD1 ? vk - VK_NUMPAD1 : vk - VK_1;
            f.BeginInvoke((Action)(() => { if (n >= 0 && n < f._count) f.Pick(n); }));
        }
        catch { /* form may have just closed on another thread */ }
    }

    private void LayoutContents()
    {
        int availW = ClientSize.Width, availH = ClientSize.Height;
        int gap = (int)(10 * _s), side = (int)(120 * _s);        // scaled with the cards they space
        _flow.MaximumSize = new Size(availW - side, 0);   // wrap by width only; never clip vertically
        var flowSize = _flow.PreferredSize;
        _flow.Size = flowSize;

        int blockH = _title.Height + gap + flowSize.Height + gap + _hint.Height;
        int top = Math.Max((int)(24 * _s), (availH - blockH) / 2);

        _title.Location = new Point((availW - _title.Width) / 2, top);
        _flow.Location = new Point(Math.Max(0, (availW - flowSize.Width) / 2), _title.Bottom + gap);
        _hint.Location = new Point((availW - _hint.Width) / 2, _flow.Bottom + gap);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { Close(); return; }

        int n = e.KeyCode switch
        {
            >= Keys.D1 and <= Keys.D9 => e.KeyCode - Keys.D1,
            >= Keys.NumPad1 and <= Keys.NumPad9 => e.KeyCode - Keys.NumPad1,
            _ => -1
        };
        if (n >= 0 && n < _count) Pick(n);
    }

    /// <summary>One layout tile: name, a little scaled map of its window rectangles, and a number.</summary>
    private sealed class Card : Panel
    {
        private readonly string _name;
        private readonly IReadOnlyList<Rect> _openWindows;
        private int _number;               // 1-based; the keyboard shortcut. 0 = none.
        private readonly bool _isCurrent;
        private bool _hover;
        private bool _placeholder;         // true while this card is the one being dragged

        /// <summary>The layout this card stands for — the stable identity the picker's actions use.</summary>
        public string LayoutName => _name;

        public Card(string name, IReadOnlyList<Rect> openWindows, int number, bool isCurrent, float s)
        {
            _name = name;
            _openWindows = openWindows;
            _number = number;
            _isCurrent = isCurrent;
            // The 300x210 design, grown to this monitor. OnPaint derives its own k from the result,
            // so the art, the name, and the number all stay in proportion at any scaling.
            Size = new Size((int)(300 * s), (int)(210 * s));
            Margin = new Padding((int)(14 * s));
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            BackColor = Color.FromArgb(34, 34, 40);
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
        }

        /// <summary>Re-badge the card after a drag or a delete shifted everything (keeps 1–9 truthful).</summary>
        public void SetNumber(int n)
        {
            if (_number == n) return;
            _number = n;
            Invalidate();
        }

        /// <summary>Turn the card into the empty dashed slot that shows where the card being dragged
        /// will land — the card's contents are floating under the cursor while this is on.</summary>
        public void SetPlaceholder(bool on)
        {
            if (_placeholder == on) return;
            _placeholder = on;
            // Coming back from a drag the pointer has been captured elsewhere the whole time, so
            // MouseEnter/MouseLeave never fired — work out the hover state from where it actually is.
            if (!on) _hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_placeholder)
            {
                using var slotPen = new Pen(Color.FromArgb(120, 175, 255), 2f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(slotPen, 2, 2, Width - 5, Height - 5);
                return;
            }

            Color borderColor = _hover ? Color.FromArgb(120, 175, 255)
                              : _isCurrent ? Color.FromArgb(95, 135, 205)
                              : Color.FromArgb(70, 70, 82);
            using (var pen = new Pen(borderColor, _hover || _isCurrent ? 2f : 1f))
                g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);

            // Everything below is drawn against the original 300x210 design and multiplied by k to
            // land on the card's REAL pixel size. Fonts are sized in PIXELS (16px == the old 12pt at
            // 100%) for the same reason: a point-sized font grows on a scaled-up monitor while a
            // hardcoded rectangle doesn't, which is how a name box 42px tall ended up shorter than
            // one 42.6px line of its own text on Keith's 200% screen — and drew nothing at all.
            float k = Height / 210f;
            var nameRect = new RectangleF(12 * k, 9 * k, Width - 46 * k, 42 * k);

            using (var titleFont = FitName(g, _name, nameRect, k))
            using (var titleBrush = new SolidBrush(Color.White))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter })
                g.DrawString(_name, titleFont, titleBrush, nameRect, sf);

            if (_number is >= 1 and <= 9)
                using (var numFont = new Font("Segoe UI", 15.3f * k, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var numBrush = new SolidBrush(Color.FromArgb(150, 150, 162)))
                    g.DrawString(_number.ToString(), numFont, numBrush, Width - 28 * k, 9 * k);

            DrawMiniMap(g, Rectangle.Round(new RectangleF(14 * k, 56 * k, Width - 28 * k, Height - 70 * k)), _openWindows);
        }

        /// <summary>The biggest size the whole name fits at: full size for short names (unchanged
        /// from before), stepped down and allowed to wrap for long ones. A name Keith actually
        /// typed — "188-Salon Site Migra and Retargeting" — should be readable on its card, not
        /// trimmed away, so shrinking beats truncating. Only a name too long even at the smallest
        /// size falls through to the caller's ellipsis.</summary>
        private static Font FitName(Graphics g, string name, RectangleF box, float k)
        {
            foreach (float px in new[] { 16f, 14.5f, 13f, 11.5f, 10f })
            {
                var f = new Font("Segoe UI", px * k, FontStyle.Bold, GraphicsUnit.Pixel);
                if (g.MeasureString(name, f, (int)box.Width).Height <= box.Height) return f;
                f.Dispose();
            }
            return new Font("Segoe UI", 10f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        /// <summary>Draw a little map of the whole (multi-monitor) desktop: each connected monitor
        /// as an outlined panel, with the layout's currently-OPEN windows placed on it. Scaled to
        /// fit, aspect preserved. Closed windows aren't drawn — the card previews what you'd get.</summary>
        private static void DrawMiniMap(Graphics g, Rectangle area, IReadOnlyList<Rect> openWindows)
        {
            var screens = Screen.AllScreens.Select(s => s.Bounds).ToArray();
            if (screens.Length == 0) return;

            int minX = screens.Min(s => s.Left);
            int minY = screens.Min(s => s.Top);
            int maxX = screens.Max(s => s.Right);
            int maxY = screens.Max(s => s.Bottom);
            float spanW = Math.Max(1, maxX - minX);
            float spanH = Math.Max(1, maxY - minY);
            float scale = Math.Min(area.Width / spanW, area.Height / spanH);
            float offX = area.X + (area.Width - spanW * scale) / 2f;
            float offY = area.Y + (area.Height - spanH * scale) / 2f;
            RectangleF Map(int x, int y, int w, int h) =>
                new(offX + (x - minX) * scale, offY + (y - minY) * scale, Math.Max(2, w * scale), Math.Max(2, h * scale));

            // Monitors: outlined panels so you can see monitor 1 vs monitor 2.
            using (var monFill = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
            using (var monPen = new Pen(Color.FromArgb(90, 190, 200, 225), 1f))
                foreach (var s in screens)
                {
                    var r = Map(s.Left, s.Top, s.Width, s.Height);
                    g.FillRectangle(monFill, r);
                    g.DrawRectangle(monPen, r.X, r.Y, r.Width, r.Height);
                }

            if (openWindows.Count == 0)
            {
                // 12px == the old 9pt at 100%; sized off the map so it tracks the card. See OnPaint.
                using var f = new Font("Segoe UI", 12f * (area.Height / 140f), FontStyle.Italic, GraphicsUnit.Pixel);
                using var b = new SolidBrush(Color.FromArgb(140, 140, 150));
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far };
                g.DrawString("none of its windows are open", f, b, area, sf);
                return;
            }

            using (var fill = new SolidBrush(Color.FromArgb(150, 120, 170, 255)))
            using (var pen = new Pen(Color.FromArgb(230, 165, 205, 255), 1f))
                foreach (var w in openWindows)
                {
                    var r = Map(w.X, w.Y, w.W, w.H);
                    g.FillRectangle(fill, r);
                    g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                }
        }
    }
}
