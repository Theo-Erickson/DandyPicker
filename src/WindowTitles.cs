using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DandyPicker
{
    // Retitles each running profile's Claude window, e.g. "Claude (Work)", so the taskbar
    // and Alt+Tab say which account is which. Claude sets its own title ("Claude") when a
    // window opens and may set it again later, so this watches for that and puts the
    // profile's title back. It only works while DandyPicker is open.
    static class WindowTitles
    {
        // Claude main-process id -> the title its windows should have.
        static Dictionary<int, string> wanted = new Dictionary<int, string>();
        static readonly List<IntPtr> hooks = new List<IntPtr>();
        static WinEventProc proc;   // kept alive: Windows holds a pointer to it

        public static string TitleOf(Profile p)
        {
            return string.IsNullOrWhiteSpace(p.windowTitle) ? "Claude (" + p.name + ")" : p.windowTitle.Trim();
        }

        // Starts watching for Claude windows appearing or renaming themselves. Call on the UI thread.
        public static void Start()
        {
            if (proc != null) return;
            proc = OnEvent;
            foreach (uint ev in new[] { EVENT_OBJECT_SHOW, EVENT_OBJECT_NAMECHANGE })
            {
                IntPtr h = SetWinEventHook(ev, ev, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                if (h != IntPtr.Zero) hooks.Add(h);
            }
        }

        public static void Stop()
        {
            foreach (var h in hooks) UnhookWinEvent(h);
            hooks.Clear();
            proc = null;
        }

        // Sets which processes are which profile, then retitles their windows now.
        public static void Update(Dictionary<int, string> titlesByPid)
        {
            wanted = titlesByPid;
            if (wanted.Count == 0) return;
            EnumWindows((h, _) => { Fix(h); return true; }, IntPtr.Zero);
        }

        static void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;
            Fix(hwnd);
        }

        static void Fix(IntPtr hwnd)
        {
            if (wanted.Count == 0) return;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            string title;
            if (!wanted.TryGetValue((int)pid, out title)) return;
            // Only the app's own top-level windows, not tooltips, menus or hidden helpers.
            if (GetAncestor(hwnd, GA_ROOT) != hwnd || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero || !IsWindowVisible(hwnd)) return;
            int len = GetWindowTextLength(hwnd);
            if (len == 0) return;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            if (sb.ToString() == title) return;
            IntPtr result;
            // A timeout, so a busy Claude window can't freeze DandyPicker.
            SendMessageTimeout(hwnd, WM_SETTEXT, IntPtr.Zero, title, SMTO_ABORTIFHUNG | SMTO_NORMAL, 500, out result);
        }

        const uint EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_NAMECHANGE = 0x800C;
        const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
        const int OBJID_WINDOW = 0;
        const uint GA_ROOT = 2, GW_OWNER = 4;
        const uint WM_SETTEXT = 0x000C, SMTO_NORMAL = 0, SMTO_ABORTIFHUNG = 2;

        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    }
}
