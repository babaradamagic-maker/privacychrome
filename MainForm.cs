using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using System.Windows.Forms;
using System.Threading;

namespace PrivacyChrome
{
    public class MainForm : Form
    {
        private NotifyIcon tray;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem armMenuItem;
        private ToolStripMenuItem exitMenuItem;
        private ToolStripMenuItem showWindowMenuItem;

        // Hooks
        private IntPtr _keyboardHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc _kbProc;
        private NativeMethods.LowLevelMouseProc _msProc;

        // State
        private volatile bool armed = true; // app is armed only when Chrome is NOT active; timer will enforce
        private volatile bool leftDown = false;
        private volatile bool rightDown = false;
        private DateTime leftDownTime = DateTime.MinValue;
        private DateTime rightDownTime = DateTime.MinValue;
        private readonly TimeSpan dualMouseWindow = TimeSpan.FromMilliseconds(300);

        // Timer to check Chrome foreground status
        private System.Timers.Timer chromeCheckTimer;

        public MainForm()
        {
            DebugLogger.Log("MainForm ctor starting");

            // Invisible form - we do not show main window by default
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Visible = false;

            InitializeTray();
            InstallHooks();

            // Start timer to check Chrome active status every 500ms
            chromeCheckTimer = new System.Timers.Timer(500);
            chromeCheckTimer.Elapsed += ChromeCheckTimer_Elapsed;
            chromeCheckTimer.Start();

            Application.ApplicationExit += OnApplicationExit;

            DebugLogger.Log("MainForm ctor complete");
        }

        private void InitializeTray()
        {
            trayMenu = new ContextMenuStrip();

            armMenuItem = new ToolStripMenuItem("Disarm");
            armMenuItem.Click += (s, e) => ToggleArmed(false);
            trayMenu.Items.Add(armMenuItem);

            showWindowMenuItem = new ToolStripMenuItem("Show Window");
            showWindowMenuItem.Click += (s, e) => ShowMainWindow();
            trayMenu.Items.Add(showWindowMenuItem);

            exitMenuItem = new ToolStripMenuItem("Exit");
            exitMenuItem.Click += (s, e) => ExitApp();
            trayMenu.Items.Add(exitMenuItem);

            tray = new NotifyIcon()
            {
                ContextMenuStrip = trayMenu,
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "PrivacyChrome (armed)"
            };
            tray.DoubleClick += (s, e) => ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            if (this.IsHandleCreated)
            {
                this.Visible = true;
                this.ShowInTaskbar = true;
                this.WindowState = FormWindowState.Normal;
                this.BringToFront();
                this.Activate();
            }
        }

        private void ToggleArmed(bool? setTo = null)
        {
            if (setTo.HasValue) armed = setTo.Value;
            else armed = !armed;
            UpdateTrayText();
        }

        private void UpdateTrayText()
        {
            try
            {
                tray.Text = $"PrivacyChrome ({(armed ? "armed" : "disarmed")})";
                armMenuItem.Text = armed ? "Disarm" : "Arm";
            }
            catch (Exception ex)
            {
                DebugLogger.Log("UpdateTrayText exception: {0}", ex);
            }
        }

        #region Hooks

        private void InstallHooks()
        {
            _kbProc = KeyboardProc;
            _msProc = MouseProc;

            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(curModule.ModuleName);

                _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc, hMod, 0);
                _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _msProc, hMod, 0);

                DebugLogger.Log("InstallHooks: module={0}, kbHook={1}, mouseHook={2}", curModule.ModuleName, _keyboardHook, _mouseHook);
            }

            UpdateTrayText();
        }

        private void UninstallHooks()
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                DebugLogger.Log("Uninstalled keyboard hook {0}", _keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                DebugLogger.Log("Uninstalled mouse hook {0}", _mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    if (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN)
                    {
                        NativeMethods.KBDLLHOOKSTRUCT kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

                        // Handle exit hotkey: Ctrl + Shift + Q
                        bool ctrl = (NativeMethods.GetAsyncKeyState((int)NativeMethods.VK_CONTROL) & 0x8000) != 0;
                        bool shift = (NativeMethods.GetAsyncKeyState((int)NativeMethods.VK_SHIFT) & 0x8000) != 0;
                        if (kb.vkCode == (int)NativeMethods.VK_Q && ctrl && shift)
                        {
                            ExitApp();
                        }
                        else if (armed)
                        {
                            // Any key press => trigger
                            TriggerBringChromeToFront();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("KeyboardProc exception: {0}", ex);
            }

            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == NativeMethods.WM_LBUTTONDOWN)
                    {
                        leftDown = true;
                        leftDownTime = DateTime.UtcNow;
                        if (rightDown && (leftDownTime - rightDownTime) <= dualMouseWindow)
                        {
                            if (armed) TriggerBringChromeToFront();
                        }
                    }
                    else if (msg == NativeMethods.WM_RBUTTONDOWN)
                    {
                        rightDown = true;
                        rightDownTime = DateTime.UtcNow;
                        if (leftDown && (rightDownTime - leftDownTime) <= dualMouseWindow)
                        {
                            if (armed) TriggerBringChromeToFront();
                        }
                    }
                    else if (msg == NativeMethods.WM_LBUTTONUP)
                    {
                        leftDown = false;
                    }
                    else if (msg == NativeMethods.WM_RBUTTONUP)
                    {
                        rightDown = false;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("MouseProc exception: {0}", ex);
            }

            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        #endregion

        #region Trigger logic

        private void TriggerBringChromeToFront()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                IntPtr fg = NativeMethods.GetForegroundWindow();
                DebugLogger.Log("Trigger: current foreground hwnd=0x{0:X}", fg.ToInt64());

                if (IsWindowChrome(fg))
                {
                    armed = false;
                    UpdateTrayText();
                    DebugLogger.Log("Trigger: foreground already Chrome, disarming");
                    return;
                }

                IntPtr chromeWnd = FindChromeWindow();
                DebugLogger.Log("Trigger: FindChromeWindow returned hwnd=0x{0:X}", chromeWnd.ToInt64());
                if (chromeWnd == IntPtr.Zero) return;

                try
                {
                    // If minimized, restore first
                    if (NativeMethods.IsIconic(chromeWnd))
                    {
                        bool restored = NativeMethods.ShowWindow(chromeWnd, NativeMethods.SW_RESTORE);
                        DebugLogger.Log("ShowWindow(SW_RESTORE) returned {0} for chromeWnd=0x{1:X}", restored, chromeWnd.ToInt64());
                    }

                    // Attach input threads so SetForegroundWindow works more reliably
                    IntPtr foreground = NativeMethods.GetForegroundWindow();

                    // Use explicit out uint variables to avoid toolchain quirks
                    uint fgThread = NativeMethods.GetWindowThreadProcessId(foreground, out uint fgPid);
                    uint chromeThread = NativeMethods.GetWindowThreadProcessId(chromeWnd, out uint chromePid);
                    uint currentThread = NativeMethods.GetCurrentThreadId();

                    DebugLogger.Log("Threads: foreground=0x{0:X} fgPid={1} fgThread={2}, chromeThread={3}, currentThread={4}",
                        foreground.ToInt64(), fgPid, fgThread, chromeThread, currentThread);

                    bool attachCurrentToFg = false;
                    bool attachCurrentToChrome = false;

                    if (fgThread != 0)
                    {
                        attachCurrentToFg = NativeMethods.AttachThreadInput(currentThread, fgThread, true);
                        DebugLogger.Log("AttachThreadInput(current->fg) returned {0}", attachCurrentToFg);
                    }
                    if (chromeThread != 0)
                    {
                        attachCurrentToChrome = NativeMethods.AttachThreadInput(currentThread, chromeThread, true);
                        DebugLogger.Log("AttachThreadInput(current->chrome) returned {0}", attachCurrentToChrome);
                    }

                    // Maximize and bring to top
                    bool maximized = NativeMethods.ShowWindow(chromeWnd, NativeMethods.SW_MAXIMIZE);
                    DebugLogger.Log("ShowWindow(SW_MAXIMIZE) returned {0} for chromeWnd=0x{1:X}", maximized, chromeWnd.ToInt64());

                    bool brought = NativeMethods.BringWindowToTop(chromeWnd);
                    DebugLogger.Log("BringWindowToTop returned {0} for chromeWnd=0x{1:X}", brought, chromeWnd.ToInt64());

                    bool fgSet = NativeMethods.SetForegroundWindow(chromeWnd);
                    DebugLogger.Log("SetForegroundWindow returned {0} for chromeWnd=0x{1:X}", fgSet, chromeWnd.ToInt64());

                    try
                    {
                        bool sp = NativeMethods.SetWindowPos(chromeWnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                        DebugLogger.Log("SetWindowPos returned {0} for chromeWnd=0x{1:X}", sp, chromeWnd.ToInt64());
                    }
                    catch (Exception spEx)
                    {
                        DebugLogger.Log("SetWindowPos exception: {0}", spEx);
                    }

                    // detach the attachments we made
                    if (attachCurrentToFg && fgThread != 0)
                    {
                        bool detached = NativeMethods.AttachThreadInput(currentThread, fgThread, false);
                        DebugLogger.Log("Detach AttachThreadInput(current->fg) returned {0}", detached);
                    }
                    if (attachCurrentToChrome && chromeThread != 0)
                    {
                        bool detached2 = NativeMethods.AttachThreadInput(currentThread, chromeThread, false);
                        DebugLogger.Log("Detach AttachThreadInput(current->chrome) returned {0}", detached2);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("TriggerBringChromeToFront exception: {0}", ex);
                }
                finally
                {
                    try
                    {
                        // Defensive detach: try to remove any remaining attachments
                        IntPtr foreground2 = NativeMethods.GetForegroundWindow();
                        uint fgThread2 = NativeMethods.GetWindowThreadProcessId(foreground2, out _);
                        uint chromeThread2 = NativeMethods.GetWindowThreadProcessId(chromeWnd, out _);
                        uint currentThread2 = NativeMethods.GetCurrentThreadId();

                        DebugLogger.Log("Finally: threads current=0x{0:X} fgThread={1} chromeThread={2}", currentThread2, fgThread2, chromeThread2);

                        if (fgThread2 != 0)
                        {
                            bool tryDetach = NativeMethods.AttachThreadInput(currentThread2, fgThread2, false);
                            DebugLogger.Log("Finally: AttachThreadInput(current->fg,false) returned {0}", tryDetach);
                        }
                        if (chromeThread2 != 0)
                        {
                            bool tryDetach2 = NativeMethods.AttachThreadInput(currentThread2, chromeThread2, false);
                            DebugLogger.Log("Finally: AttachThreadInput(current->chrome,false) returned {0}", tryDetach2);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log("Finally detach exception: {0}", ex);
                    }
                }

                MinimizeOtherWindows(chromeWnd);

                armed = false;
                UpdateTrayText();

                DebugLogger.Log("Trigger: completed for chromeWnd=0x{0:X}", chromeWnd.ToInt64());
            });
        }

        private bool IsWindowChrome(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            try
            {
                var proc = Process.GetProcessById((int)pid);
                string name = proc.ProcessName.ToLowerInvariant();
                return name.Contains("chrome") || name.Contains("brave") || name.Contains("chromium");
            }
            catch
            {
                return false;
            }
        }

        private IntPtr FindChromeWindow()
        {
            IntPtr found = IntPtr.Zero;
            List<IntPtr> candidates = new();
            NativeMethods.EnumWindows((hw, l) =>
            {
                if (!NativeMethods.IsWindowVisible(hw)) return true;
                if (hw == IntPtr.Zero) return true;

                if (hw == this.Handle) return true;
                const int buf = 256;
                var sb = new StringBuilder(buf);
                NativeMethods.GetClassName(hw, sb, buf);
                string cls = sb.ToString();
                if (cls == "Shell_TrayWnd" || cls == "Progman" || cls == "Button") return true;

                uint pid;
                NativeMethods.GetWindowThreadProcessId(hw, out pid);
                try
                {
                    var proc = Process.GetProcessById((int)pid);
                    string name = proc.ProcessName.ToLowerInvariant();
                    if (name.Contains("chrome") || name.Contains("brave") || name.Contains("chromium"))
                    {
                        candidates.Add(hw);
                    }
                }
                catch { }

                return true;
            }, IntPtr.Zero);

            IntPtr fg = NativeMethods.GetForegroundWindow();
            foreach (var c in candidates)
            {
                if (c == fg) return c;
            }
            if (candidates.Count > 0) return candidates[0];
            return IntPtr.Zero;
        }

        private void MinimizeOtherWindows(IntPtr exceptHwnd)
        {
            NativeMethods.EnumWindows((hw, l) =>
            {
                try
                {
                    if (!NativeMethods.IsWindowVisible(hw)) return true;
                    if (hw == exceptHwnd) return true;
                    if (hw == this.Handle) return true;

                    const int buf = 256;
                    var sb = new StringBuilder(buf);
                    NativeMethods.GetClassName(hw, sb, buf);
                    string cls = sb.ToString();
                    if (cls == "Shell_TrayWnd" || cls == "Progman") return true;

                    if (NativeMethods.IsIconic(hw)) return true;

                    NativeMethods.ShowWindow(hw, NativeMethods.SW_MINIMIZE);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("MinimizeOtherWindows item exception: {0}", ex);
                }
                return true;
            }, IntPtr.Zero);
        }

        #endregion

        #region Chrome status timer

        private void ChromeCheckTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            bool chromeActive = IsWindowChrome(fg);
            if (chromeActive)
            {
                if (armed)
                {
                    armed = false;
                    UpdateTrayText();
                }
            }
            else
            {
                if (!armed)
                {
                    armed = true;
                    UpdateTrayText();
                }
            }
        }

        #endregion

        #region Shutdown / cleanup

        private void ExitApp()
        {
            DebugLogger.Log("ExitApp called");
            chromeCheckTimer?.Stop();
            UninstallHooks();
            try
            {
                tray.Visible = false;
                tray.Dispose();
            }
            catch { }
            Application.Exit();
            Environment.Exit(0);
        }

        private void OnApplicationExit(object sender, EventArgs e)
        {
            DebugLogger.Log("OnApplicationExit");
            UninstallHooks();
            tray?.Dispose();
            chromeCheckTimer?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tray?.Dispose();
                trayMenu?.Dispose();
                chromeCheckTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}