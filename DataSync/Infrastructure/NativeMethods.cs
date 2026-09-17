using System;
using System.Runtime.InteropServices;

namespace DataSync.Infrastructure
{
    /// <summary>
    /// The few Windows calls the application needs; there is no managed way to bring another process's window forward.
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>Shows the window and restores it when it is minimised, without changing its size or position.</summary>
        public const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
