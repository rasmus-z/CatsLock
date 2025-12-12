using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;


//TODO: Add process kill when trying to run the same process again 
//TODO: Do not let the user end the process while cleaning mode is on 

namespace CatsLock
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayAppContext());
        }
    }

    internal class TrayAppContext : ApplicationContext
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // AKA Alt key
        private const int VK_F12 = 0x7B;
        private const int VK_CAPSLOCK = 0x14;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")] private static extern short GetKeyState(int nVirtKey);

        private readonly LowLevelKeyboardProc proc;
        private IntPtr hookId = IntPtr.Zero;

        // App State 
        private bool cleaningMode = false;
        private DateTime cleaningStartedUtc;
        private readonly int autoDisableMinutes = 10;

        // User Interface
        private Icon iconOn;
        private Icon iconOff;
        private Icon iconIdle;
        private readonly NotifyIcon trayIcon;
        private readonly System.Windows.Forms.Timer timer;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem toggleItem;
        private readonly ToolStripMenuItem exitItem;
        private readonly ToolStripMenuItem aboutItem;

        public TrayAppContext()
        {
            proc = HookCallback;
            hookId = SetHook(proc);

            iconOff = LoadIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "cleaningDisabled.ico"));
            iconOn = LoadIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "cleaningEnabled.ico"));
            iconIdle = LoadIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "broom.ico"));

            menu = new ContextMenuStrip();
            toggleItem = new ToolStripMenuItem("Enable Cat Paw Mode", null, OnToggleClick);
            aboutItem = new ToolStripMenuItem("About", null, OnAboutClick);
            exitItem = new ToolStripMenuItem("Exit", null, OnExitClick);

            menu.Items.AddRange(new ToolStripItem[] { toggleItem, new ToolStripSeparator(), aboutItem, exitItem });

            trayIcon = new NotifyIcon
            {
                Icon = iconOff,
                ContextMenuStrip = menu,
                Visible = true,
                Text = "CatsLock - Cat Paw Mode: Off"
            };

            trayIcon.DoubleClick += (s, e) => ToggleCleaningMode();

            ShowBalloon("Keyboard Cat Paw Mode ready", "Toggle with Ctrl + Alt + F12");

        }

        //TODO: Fix balloon tip showing the wrong icon on the top right corner
        private void ShowBalloon(string title, string text)
        {
            trayIcon.BalloonTipTitle = title;
            trayIcon.BalloonTipText = text;
            trayIcon.BalloonTipIcon = ToolTipIcon.Info;
            trayIcon.Icon = iconIdle;
            trayIcon.ShowBalloonTip(3000);
        }

        private void OnAboutClick(object? sender, EventArgs e)
        {
            MessageBox.Show(
            "CatsLock (Keyboard Keys Blocking Utility)\n\n" +
            "Keyboard Cat Paw Mode\n\n" +
            "� Toggle: Capslock or Ctrl + Alt + F12\n" +
            "� Blocks all keyboard keys while On (mouse still works)\n" +
            "� Toggle via tray menu or double-click the tray icon\n" +
            "Tip: You can right-click the tray icon to exit.",
            "Keyboard Cat Paw Mode",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
            );
        }

        private void OnToggleClick(object? sender, EventArgs e) => ToggleCleaningMode();

        private void OnExitClick(object? sender, EventArgs e)
        {
            if (hookId != IntPtr.Zero) UnhookWindowsHookEx(hookId);
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon.Dispose();
            timer.Stop();
            trayIcon.Dispose();
            ExitThread();
        }

        private void ToggleCleaningMode()
        {
            cleaningMode = !cleaningMode;
            if (cleaningMode)
            {
                cleaningStartedUtc = DateTime.UtcNow;
                toggleItem.Text = "Disable Cat Paw Mode";
                trayIcon.Icon = iconOn;
                trayIcon.Text = TooltipText();
                ShowBalloon("Cat Paw Mode On", "All keyboard input is blocked. Toggle off to restore.");
            }
            else
            {
                toggleItem.Text = "Enable Cat Paw Mode";
                trayIcon.Text = "CatsLock - Cat Paw Mode: Off";
                trayIcon.Icon = iconOff;
                ShowBalloon("Cat Paw Mode Disabled", "Keyboard input restored.");
            }
        }

        private Icon? LoadIcon(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return new Icon(path);
                }
                else
                {
                    MessageBox.Show($"Icon file not found: {path}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return System.Drawing.SystemIcons.Application;
                }
            }
            catch { }

            return null;
        }

        private string TooltipText()
        {
            return cleaningMode ? "Cat Paw Mode: On (Ctrl + Alt + F12 to toggle)" : "Cat Paw Mode: Off (Ctrl + Alt + F12 to toggle) ";
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                IntPtr moduleHandle = GetModuleHandle(curModule.ModuleName);
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, moduleHandle, 0);
            }
        }

        private static bool IsKeyDown(int vkCode)
        {
            return (GetKeyState(vkCode) & 0x8000) != 0;
        }

        private bool IsToggleCombo(int vkCode)
        {
            //return vkCode == IsKeyDown(VK_
            return vkCode == VK_F12 && IsKeyDown(VK_CONTROL) && IsKeyDown(VK_MENU);
        }
        private static bool IsKeyToggled(int vkCode)
        {
            return (GetKeyState(vkCode) & 0x1) != 0;
        }
        private bool IsCapsLock(int vkCode)
        {
            //return vkCode == IsKeyDown(VK_
            return vkCode == VK_CAPSLOCK;// && IsKeyDown(VK_CONTROL) && IsKeyDown(VK_MENU);
        }

        private bool IsCapsLockToggled()
        {
            return Control.IsKeyLocked(Keys.CapsLock);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (IsCapsLock(vkCode))
                {
                    if (!IsCapsLockToggled())
                    {
                        if (!cleaningMode)
                            ToggleCleaningMode();

                        //return (IntPtr)1; // Block the toggle key combo
                        return CallNextHookEx(hookId, nCode, wParam, lParam);
                    } else {
                        if (cleaningMode)
                            ToggleCleaningMode();

                        return CallNextHookEx(hookId, nCode, wParam, lParam);
                    }
                }
                if (IsToggleCombo(vkCode))
                {
                    ToggleCleaningMode();
                    return (IntPtr)1; // Block the toggle key combo
                }
                if (cleaningMode)
                {
                    // Block all other key presses when in cleaning mode
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (hookId != IntPtr.Zero) UnhookWindowsHookEx(hookId);
                trayIcon.Visible = false;
                trayIcon.Dispose();
                timer.Dispose();
                menu.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}