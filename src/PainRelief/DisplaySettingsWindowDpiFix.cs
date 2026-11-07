using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Fixes the Display Settings window being cut off at the bottom under Windows display
    /// scaling (the last brightness slider and the Reset button are clipped), and the left-hand
    /// checkboxes running underneath the Brightness panel.
    ///
    /// Why it breaks: vatSys has no DPI manifest, so it starts DPI-unaware, but Cef.Initialize
    /// (the built-in browser) switches the process to per-monitor DPI awareness partway through
    /// startup. The sliders and labels then come out larger than the designer sized them. The
    /// brightness table (tableLayoutPanel4) is not auto-sized, so it keeps its designed height
    /// and its last rows fall outside it. The Brightness panel also sits at a fixed X, so the
    /// wider left-hand column runs under it.
    ///
    /// The fix sizes the brightness table to what its rows need, wraps the panel around it,
    /// moves the panel to the right of the (possibly wider) left-hand column, stacks the
    /// left-hand tables so they cannot overlap, and then fits the window to the result.
    /// Nothing is scaled by a guessed factor: every size comes from the controls' own
    /// preferred sizes, so it is correct at any scale, including 100%.
    ///
    /// The window is created once and shown and hidden, so the layout is redone each time it is
    /// shown. It is idempotent.
    /// </summary>
    internal sealed class DisplaySettingsWindowDpiFix
    {
        private const int TickMilliseconds = 250;
        private const int Margin = 3; // gap around tables, panels and the window edge, as designed

        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        // The left-hand column, which is stacked top to bottom in order of position.
        private static readonly string[] LeftColumn = { "tableLayoutPanel1", "tableLayoutPanel2", "tableLayoutPanel3", "tableLayoutPanel5" };

        private static readonly FieldInfo DisplayWindowField = typeof(MMI).GetField("DisplaySettingsWindow", NonPublicStatic);

        private readonly System.Threading.Timer timer;
        private readonly ConditionalWeakTable<Form, object> hooked = new ConditionalWeakTable<Form, object>();
        private int ticking;
        private int errorReported;

        public DisplaySettingsWindowDpiFix()
        {
            timer = new System.Threading.Timer(Tick, null, TickMilliseconds, TickMilliseconds);
        }

        private void Tick(object state)
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1)
                return;

            try
            {
                if (DisplayWindowField == null)
                    throw new MissingFieldException("MMI.DisplaySettingsWindow not found");

                Form window = DisplayWindowField.GetValue(null) as Form;
                if (window == null || window.IsDisposed || !window.IsHandleCreated)
                    return; // not created yet
                if (hooked.TryGetValue(window, out _))
                    return; // already hooked

                hooked.Add(window, null);
                window.BeginInvoke((MethodInvoker)(() =>
                {
                    try
                    {
                        window.VisibleChanged += OnVisibleChanged;
                        if (window.Visible)
                            Apply(window);
                    }
                    catch (Exception ex)
                    {
                        Report(ex);
                    }
                }));
            }
            catch (Exception ex)
            {
                Report(ex);
            }
            finally
            {
                Volatile.Write(ref ticking, 0);
            }
        }

        private void OnVisibleChanged(object sender, EventArgs e)
        {
            Form window = (Form)sender;
            if (!window.Visible)
                return;

            // Wait until vatSys has finished showing the window, so the layout it does on show
            // doesn't undo this one.
            window.BeginInvoke((MethodInvoker)(() =>
            {
                try
                {
                    if (window.Visible)
                        Apply(window);
                }
                catch (Exception ex)
                {
                    Report(ex);
                }
            }));
        }

        /// <summary>Re-lays out one DisplaySettingsWindow. Must run on its UI thread.</summary>
        internal static void Apply(Form window)
        {
            var left = new List<Control>();
            foreach (string name in LeftColumn)
                left.Add(GetControl(window, name));
            left.Sort((a, b) => a.Top.CompareTo(b.Top));

            Control panel = GetControl(window, "insetPanel1");
            Control table = GetControl(window, "tableLayoutPanel4");

            window.SuspendLayout();
            panel.SuspendLayout();

            // Left-hand column: each table at its own preferred size, never overlapping the
            // one above it.
            int previousBottom = int.MinValue;
            int leftRight = 0;
            int leftBottom = 0;
            foreach (Control control in left)
            {
                control.Size = control.GetPreferredSize(Size.Empty);
                if (control.Top < previousBottom)
                    control.Top = previousBottom;
                previousBottom = control.Bottom;
                leftRight = Math.Max(leftRight, control.Right);
                leftBottom = Math.Max(leftBottom, control.Bottom);
            }

            // Brightness table at the height its rows need; the panel wraps around it and sits
            // to the right of the left-hand column.
            table.Location = new Point(Margin, Margin);
            table.Size = table.GetPreferredSize(Size.Empty);
            panel.Size = new Size(table.Right + Margin, table.Bottom + Margin);
            panel.Left = leftRight + Margin;

            panel.ResumeLayout(true);
            window.ResumeLayout(true);

            window.ClientSize = new Size(panel.Right + Margin, Math.Max(panel.Bottom, leftBottom) + Margin);
            window.Invalidate(true);
        }

        private static Control GetControl(Form window, string name)
        {
            Control control = window.GetType().GetField(name, NonPublicInstance)?.GetValue(window) as Control;
            if (control == null)
                throw new MissingFieldException(window.GetType().Name + "." + name + " not found");
            return control;
        }

        private void Report(Exception ex)
        {
            // Report once, then stop polling: if a vatSys update renamed these private members
            // there is nothing more to do, and the window keeps vatSys's own layout.
            if (Interlocked.Exchange(ref errorReported, 1) == 1)
                return;
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            Errors.Add(new Exception("Could not fix the Display Settings window for display scaling: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
        }
    }
}
