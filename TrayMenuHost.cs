using System.Runtime.InteropServices;

namespace IsConnected;

internal sealed class TrayMenuHost : Form
{
    private const int ToolWindowExtendedStyle = 0x00000080;
    private const int WmNull = 0x0000;

    public TrayMenuHost()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1, 1);
        Location = new Point(-32_000, -32_000);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= ToolWindowExtendedStyle;
            return createParams;
        }
    }

    public void ShowContextMenu(ContextMenuStrip menu, Point screenLocation)
    {
        if (!Visible)
        {
            Show();
        }

        if (menu.Visible)
        {
            menu.Close(ToolStripDropDownCloseReason.CloseCalled);
        }

        // Notification-area popup menus must belong to the foreground window;
        // otherwise Windows can leave them open after the user clicks elsewhere.
        NativeMethods.SetForegroundWindow(Handle);
        menu.Show(this, PointToClient(screenLocation));
        NativeMethods.PostMessage(Handle, WmNull, IntPtr.Zero, IntPtr.Zero);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
