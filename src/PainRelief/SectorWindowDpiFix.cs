using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Fixes clipped sector names in the Sector Configuration Window ("Controlled Sectors" /
    /// "Available Sectors") under Windows display scaling.
    ///
    /// Why it breaks: vatSys has no DPI manifest, so it starts DPI-unaware, but Cef.Initialize
    /// (the built-in browser) switches the process to per-monitor DPI awareness partway through
    /// startup. From then on TextRenderer draws the 16px Terminus font about 12% wider than the
    /// native TreeView measured it. vatSys owner-draws each node with
    /// TextRenderer.DrawText(..., e.Node.Bounds, ...), which centres the text in the native
    /// (too narrow) bounds and clips to them, so both ends of every name are cut off
    /// ("T - Jakarta Grou" for "JT - Jakarta Group").
    ///
    /// The fix swaps vatSys's DrawNode handler on both lists for one that draws the text
    /// left-aligned from the node's left edge, across the full visible width of the list, with
    /// an ellipsis if it still doesn't fit (the node tooltip still shows the full name).
    /// Colours and selection highlighting are unchanged.
    ///
    /// MMI.SectorsWindow is private and created on first use, so a short timer looks for it and
    /// patches each new instance once.
    /// </summary>
    internal sealed class SectorWindowDpiFix
    {
        private const int TickMilliseconds = 250;

        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private const TextFormatFlags NodeTextFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        private static readonly string[] TreeFields = { "currSectorsView", "availSectorsView" };
        private const string VatSysDrawNodeMethod = "SectorsView_DrawNode";

        private static readonly FieldInfo SectorsWindowField = typeof(MMI).GetField("SectorsWindow", NonPublicStatic);

        private readonly System.Threading.Timer timer;
        private readonly WeakReference patchedWindow = new WeakReference(null);
        private int ticking;
        private int errorReported;

        public SectorWindowDpiFix()
        {
            timer = new System.Threading.Timer(Tick, null, TickMilliseconds, TickMilliseconds);
        }

        private void Tick(object state)
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1)
                return;

            try
            {
                if (SectorsWindowField == null)
                    throw new MissingFieldException("MMI.SectorsWindow not found");

                Form window = SectorsWindowField.GetValue(null) as Form;
                if (window == null || window.IsDisposed || !window.IsHandleCreated)
                    return; // not created (or not shown) yet
                if (ReferenceEquals(patchedWindow.Target, window))
                    return; // already done

                patchedWindow.Target = window;
                window.BeginInvoke((MethodInvoker)(() =>
                {
                    try
                    {
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

        /// <summary>Patches one SectorsWindow instance. Must run on its UI thread.</summary>
        internal static void Apply(Form window)
        {
            Type type = window.GetType();
            MethodInfo vatSysHandler = type.GetMethod(VatSysDrawNodeMethod, NonPublicInstance);
            if (vatSysHandler == null)
                throw new MissingMethodException(type.Name + "." + VatSysDrawNodeMethod + " not found");
            var original = (DrawTreeNodeEventHandler)Delegate.CreateDelegate(typeof(DrawTreeNodeEventHandler), window, vatSysHandler);

            foreach (string name in TreeFields)
            {
                TreeView tree = type.GetField(name, NonPublicInstance)?.GetValue(window) as TreeView;
                if (tree == null)
                    throw new MissingFieldException(type.Name + "." + name + " not found");

                tree.DrawNode -= original;
                tree.DrawNode -= DrawNode; // never add ours twice
                tree.DrawNode += DrawNode;
                tree.Invalidate();
            }
        }

        private static void DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            TreeView tree = (TreeView)sender;
            if (e.Node == null || e.Bounds.IsEmpty)
                return;

            // From the node's indent to the visible right edge of the list, not just the
            // native text box, so the ellipsis lands where the list ends.
            int width = tree.ClientSize.Width - e.Bounds.X;
            Rectangle row = new Rectangle(e.Bounds.X, e.Bounds.Y, width > 0 ? width : e.Bounds.Width, e.Bounds.Height);

            Color colour = (e.State & TreeNodeStates.Selected) != 0
                ? Colours.GetColour(Colours.Identities.HighlightedText)
                : tree.ForeColor;

            Region oldClip = e.Graphics.Clip;
            using (Region clip = new Region(row))
            {
                e.Graphics.Clip = clip;
                e.Graphics.Clear(tree.BackColor);
                TextRenderer.DrawText(e.Graphics, e.Node.Text, e.Node.NodeFont ?? tree.Font, row, colour, NodeTextFlags);
            }
            e.Graphics.Clip = oldClip;
            oldClip.Dispose();
        }

        private void Report(Exception ex)
        {
            // Report once, then stop polling: if a vatSys update renamed these private members
            // there is nothing more to do, and the window keeps vatSys's own drawing.
            if (Interlocked.Exchange(ref errorReported, 1) == 1)
                return;
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            Errors.Add(new Exception("Could not fix the Sector Configuration Window for display scaling: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
        }
    }
}
