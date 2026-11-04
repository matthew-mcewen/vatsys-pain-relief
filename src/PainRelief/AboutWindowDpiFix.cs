using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Fixes the About window ("vatSys: Virtual Air Traffic System") collapsing to a narrow,
    /// almost empty box with a truncated title under Windows display scaling.
    ///
    /// Why it breaks: vatSys has no DPI manifest, so it starts DPI-unaware, but Cef.Initialize
    /// (the built-in browser) switches the process to per-monitor DPI awareness partway through
    /// startup. After that the labels measure wider than the designer sizes. The window's
    /// auto-sized inset panel holds a table that is anchored to all four sides, and the change
    /// in size sets off an auto-size/anchor feedback loop that shrinks the panel to about 17px
    /// wide. The table and its text are clipped away, and the auto-sized window shrinks to fit.
    ///
    /// The fix anchors the table top-left only, so it keeps its own preferred size. It then
    /// sizes the panel to fit the table, centres the Okay button under it, lets the window
    /// re-fit and keeps it centred where it was.
    ///
    /// The About window is created fresh each time (new AboutWindow().ShowDialog()) and is not
    /// kept in a field, so a short timer asks the UI thread to look through the open forms for
    /// it and patches each new one once.
    /// </summary>
    internal sealed class AboutWindowDpiFix
    {
        private const int TickMilliseconds = 250;
        private const int PanelPadding = 3; // table margin inside the inset panel, as designed
        private const int ButtonGap = 6;    // gap between panel and Okay button, as designed
        private const int BottomGap = 7;    // gap under the Okay button, as designed

        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo MainFormField = typeof(MMI).GetField("MainForm", NonPublicStatic);

        private readonly System.Threading.Timer timer;
        private readonly ConditionalWeakTable<Form, object> patched = new ConditionalWeakTable<Form, object>();
        private int ticking;
        private int errorReported;

        public AboutWindowDpiFix()
        {
            timer = new System.Threading.Timer(Tick, null, TickMilliseconds, TickMilliseconds);
        }

        private void Tick(object state)
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1)
                return;

            try
            {
                if (MainFormField == null)
                    throw new MissingFieldException("MMI.MainForm not found");

                Form mainForm = MainFormField.GetValue(null) as Form;
                if (mainForm == null || mainForm.IsDisposed || !mainForm.IsHandleCreated)
                    return; // vatSys still starting

                // Application.OpenForms is only safe to read on the UI thread. Clear 'ticking'
                // there, so a busy UI thread never gets more than one scan queued.
                mainForm.BeginInvoke((MethodInvoker)(() =>
                {
                    try
                    {
                        Scan();
                    }
                    catch (Exception ex)
                    {
                        Report(ex);
                    }
                    finally
                    {
                        Volatile.Write(ref ticking, 0);
                    }
                }));
            }
            catch (Exception ex)
            {
                Report(ex);
                Volatile.Write(ref ticking, 0);
            }
        }

        private void Scan()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (!(form is AboutWindow) || form.IsDisposed || !form.IsHandleCreated)
                    continue;
                if (patched.TryGetValue(form, out _))
                    continue;

                patched.Add(form, null);
                Apply(form);
            }
        }

        /// <summary>Re-lays out one AboutWindow. Must run on its UI thread.</summary>
        internal static void Apply(Form window)
        {
            Control table = GetControl(window, "tableLayoutPanel1");
            Control panel = GetControl(window, "insetPanel1");
            Control button = GetControl(window, "genericButton1");

            Point centre = new Point(window.Left + window.Width / 2, window.Top + window.Height / 2);

            window.SuspendLayout();
            panel.SuspendLayout();

            // Anchored to all four sides inside an auto-sized parent, the table and panel fight
            // over who sets the size. Top-left only, the table keeps its own preferred size.
            table.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            table.Location = new Point(PanelPadding, PanelPadding);
            table.Size = table.GetPreferredSize(Size.Empty);
            panel.Size = new Size(table.Right + PanelPadding, table.Bottom + PanelPadding);

            panel.ResumeLayout(true);
            window.ResumeLayout(true);

            // Resize the window first: the button is anchored to the bottom only, so a resize
            // afterwards would slide it sideways again.
            window.ClientSize = new Size(panel.Right + 1, panel.Bottom + ButtonGap + button.Height + BottomGap);
            button.Location = new Point(panel.Left + (panel.Width - button.Width) / 2, panel.Bottom + ButtonGap);

            // It was centred at its broken size; keep the same centre at the real size.
            window.Location = new Point(centre.X - window.Width / 2, centre.Y - window.Height / 2);
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
            Errors.Add(new Exception("Could not fix the About window for display scaling: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
        }
    }
}
