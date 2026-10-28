using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ZipHere")]
[assembly: AssemblyDescription("Extract-here context menu and automatic unpacking of downloaded ZIP archives")]
[assembly: AssemblyProduct("ZipHere")]
[assembly: AssemblyCompany("ZipHere contributors")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

static class Program
{
    const string MenuText = "Распаковать здесь";
    const string WatchArg = "--watch";
    const string MenuKeyPath = @"Software\Classes\SystemFileAssociations\.zip\shell\ZipHere";
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string SettingsKeyPath = @"Software\ZipHere";

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    static string InstallDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZipHere"); }
    }

    static string InstalledExe
    {
        get { return Path.Combine(InstallDir, "ZipHere.exe"); }
    }

    // ================= Распаковка =================

    // Распаковывает архив в папку рядом с ним и переносит сам архив в корзину.
    // Если распаковка не удалась, архив остаётся на месте, а недоделанная папка удаляется.
    static string Unpack(string zipPath, out bool archiveRecycled)
    {
        archiveRecycled = false;
        string full = Path.GetFullPath(zipPath);
        string dir = Path.GetDirectoryName(full);
        string target = Path.Combine(dir, Path.GetFileNameWithoutExtension(full));
        string candidate = target;
        int n = 1;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = target + "_" + (n++);

        try
        {
            ZipFile.ExtractToDirectory(full, candidate);
        }
        catch
        {
            try { if (Directory.Exists(candidate)) Directory.Delete(candidate, true); } catch { }
            throw;
        }

        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            archiveRecycled = true;
        }
        catch { }

        return candidate;
    }

    // Ждёт, пока файл докачается: он должен открыться и перестать расти.
    static bool WaitUntilReady(string path, int timeoutSec)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(timeoutSec);
        long lastSize = -1;
        int stable = 0;
        while (DateTime.UtcNow < end)
        {
            try
            {
                if (!File.Exists(path)) return false;
                long size = new FileInfo(path).Length;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                if (size > 0 && size == lastSize)
                {
                    if (++stable >= 3) return true;
                }
                else
                {
                    stable = 0;
                    lastSize = size;
                }
            }
            catch (IOException) { stable = 0; }
            catch (UnauthorizedAccessException) { stable = 0; }
            Thread.Sleep(1500);
        }
        return false;
    }

    static string GetDownloadsFolder()
    {
        try
        {
            IntPtr p;
            Guid id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out p) == 0)
            {
                string s = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    // ================= Настройки =================

    static bool GetAuto()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKeyPath))
        {
            if (k == null) return true;
            object v = k.GetValue("AutoUnpack");
            return v == null || Convert.ToInt32(v) != 0;
        }
    }

    static void SetAuto(bool on)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
            k.SetValue("AutoUnpack", on ? 1 : 0, RegistryValueKind.DWord);
    }

    // ================= Установка / удаление =================

    static bool IsInstalled()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(MenuKeyPath))
            return k != null;
    }

    static void StopOtherInstances()
    {
        int me = Process.GetCurrentProcess().Id;
        foreach (Process p in Process.GetProcessesByName("ZipHere"))
        {
            try
            {
                if (p.Id != me)
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch { }
            finally { p.Dispose(); }
        }
    }

    static void Install()
    {
        StopOtherInstances();
        Directory.CreateDirectory(InstallDir);

        string self = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
            File.Copy(self, InstalledExe, true);

        // Пункт контекстного меню
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(MenuKeyPath))
        {
            k.SetValue("", MenuText);
            k.SetValue("Icon", "zipfldr.dll");
        }
        using (RegistryKey c = Registry.CurrentUser.CreateSubKey(MenuKeyPath + @"\command"))
        {
            c.SetValue("", "\"" + InstalledExe + "\" \"%1\"");
        }

        // Автозапуск наблюдателя за папкой «Загрузки»
        using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            run.SetValue("ZipHere", "\"" + InstalledExe + "\" " + WatchArg);
        }

        // Запускаем сразу, чтобы не ждать перезагрузки
        Process.Start(new ProcessStartInfo(InstalledExe, WatchArg) { UseShellExecute = false });
    }

    static void Uninstall()
    {
        StopOtherInstances();
        Registry.CurrentUser.DeleteSubKeyTree(MenuKeyPath, false);
        using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
        {
            if (run != null) run.DeleteValue("ZipHere", false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(SettingsKeyPath, false);
        try { if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true); } catch { }
    }

    // ================= Распаковка из меню правой кнопки =================

    static void ExtractWithProgress(string zipPath)
    {
        string error = null;

        Form form = new Form();
        form.Text = "Распаковка";
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.ClientSize = new Size(320, 70);
        form.ControlBox = false;
        form.TopMost = true;

        Label label = new Label();
        label.Text = Path.GetFileName(zipPath);
        label.AutoEllipsis = true;
        label.SetBounds(12, 10, 296, 18);

        ProgressBar bar = new ProgressBar();
        bar.Style = ProgressBarStyle.Marquee;
        bar.SetBounds(12, 36, 296, 20);

        form.Controls.Add(label);
        form.Controls.Add(bar);

        Thread worker = new Thread(delegate ()
        {
            try
            {
                bool recycled;
                Unpack(zipPath, out recycled);
            }
            catch (Exception e) { error = e.Message; }
            try { form.BeginInvoke(new MethodInvoker(form.Close)); } catch { }
        });
        worker.IsBackground = true;

        form.Shown += delegate { worker.Start(); };
        Application.Run(form);

        if (error != null)
            MessageBox.Show(error, "Ошибка распаковки", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    // ================= Фоновая автораспаковка (значок в трее) =================

    static Icon MakeIcon()
    {
        using (Bitmap bmp = new Bitmap(32, 32))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Brush b = new SolidBrush(Color.FromArgb(255, 193, 7)))
                    g.FillRectangle(b, 2, 6, 28, 22);
                using (Font f = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush t = new SolidBrush(Color.FromArgb(60, 60, 60)))
                    g.DrawString("Z", f, t, 9, 7);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    sealed class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Control invoker = new Control();
        readonly HashSet<string> busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly ToolStripMenuItem autoItem;
        readonly string downloads;
        FileSystemWatcher watcher;
        volatile bool autoEnabled;

        public TrayApp()
        {
            IntPtr handle = invoker.Handle; // создаём дескриптор в UI-потоке для BeginInvoke
            downloads = GetDownloadsFolder();
            autoEnabled = GetAuto();

            autoItem = new ToolStripMenuItem("Автораспаковка загрузок");
            autoItem.CheckOnClick = true;
            autoItem.Checked = autoEnabled;
            autoItem.CheckedChanged += delegate
            {
                autoEnabled = autoItem.Checked;
                SetAuto(autoEnabled);
                UpdateTrayText();
            };

            ToolStripMenuItem openItem = new ToolStripMenuItem("Открыть папку загрузок");
            openItem.Click += delegate { OpenDownloads(); };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += delegate
            {
                tray.Visible = false;
                if (watcher != null) watcher.Dispose();
                tray.Dispose();
                ExitThread();
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(autoItem);
            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            tray.Icon = MakeIcon();
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { OpenDownloads(); };
            UpdateTrayText();
            tray.Visible = true;

            if (Directory.Exists(downloads))
            {
                watcher = new FileSystemWatcher(downloads);
                watcher.NotifyFilter = NotifyFilters.FileName;
                watcher.Created += delegate (object s, FileSystemEventArgs e) { OnFile(e.FullPath); };
                watcher.Renamed += delegate (object s, RenamedEventArgs e) { OnFile(e.FullPath); };
                watcher.EnableRaisingEvents = true;
            }
        }

        void UpdateTrayText()
        {
            tray.Text = autoEnabled ? "ZipHere: автораспаковка включена" : "ZipHere: автораспаковка выключена";
        }

        void OpenDownloads()
        {
            try { Process.Start("explorer.exe", "\"" + downloads + "\""); } catch { }
        }

        // Браузеры качают во временный файл и в конце переименовывают в .zip,
        // поэтому слушаем и создание, и переименование.
        void OnFile(string path)
        {
            if (!autoEnabled) return;
            if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;
            lock (busy)
            {
                if (!busy.Add(path)) return;
            }
            ThreadPool.QueueUserWorkItem(delegate { HandleFile(path); });
        }

        void HandleFile(string path)
        {
            try
            {
                if (!WaitUntilReady(path, 900)) return;
                if (!autoEnabled) return;
                bool recycled;
                string target = Unpack(path, out recycled);
                Notify("Распаковано: " + Path.GetFileName(path),
                    "Папка: " + Path.GetFileName(target) + (recycled ? "\nАрхив перемещён в корзину" : "\nАрхив удалить не удалось"),
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Notify("Не удалось распаковать " + Path.GetFileName(path), ex.Message, ToolTipIcon.Error);
            }
            finally
            {
                lock (busy) { busy.Remove(path); }
            }
        }

        void Notify(string title, string text, ToolTipIcon icon)
        {
            try
            {
                invoker.BeginInvoke(new MethodInvoker(delegate { tray.ShowBalloonTip(4000, title, text, icon); }));
            }
            catch { }
        }
    }

    static void RunWatcher()
    {
        bool created;
        using (Mutex m = new Mutex(true, @"Local\ZipHere.Watcher", out created))
        {
            if (!created) return; // уже запущен
            Application.Run(new TrayApp());
            GC.KeepAlive(m);
        }
    }

    // ================= Окно установщика =================

    static void RunInstaller()
    {
        try
        {
            if (!IsInstalled())
            {
                DialogResult r = MessageBox.Show(
                    "Установить ZipHere?\n\n" +
                    "• Пункт «" + MenuText + "» в меню правой кнопки для ZIP-архивов.\n" +
                    "• Автозапуск вместе с Windows: новые ZIP-файлы в папке «Загрузки» распаковываются автоматически.\n" +
                    "• После распаковки архив перемещается в корзину.\n\n" +
                    "Автораспаковку можно отключить через значок в трее.",
                    "Установка ZipHere", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
                Install();
                MessageBox.Show(
                    "Готово! ZipHere запущен (значок возле часов) и будет стартовать вместе с Windows.\n\n" +
                    "В новом меню Windows 11 пункт находится в «Показать дополнительные параметры».",
                    "Установлено");
            }
            else
            {
                DialogResult r = MessageBox.Show(
                    "ZipHere уже установлен.\n\n" +
                    "Да — обновить\nНет — удалить\nОтмена — ничего не делать",
                    "ZipHere", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Yes)
                {
                    Install();
                    MessageBox.Show("Обновлено и запущено.", "Готово");
                }
                else if (r == DialogResult.No)
                {
                    Uninstall();
                    MessageBox.Show("Удалено.", "Готово");
                }
            }
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();

        if (args.Length > 0 && args[0] == WatchArg) { RunWatcher(); return; }
        if (args.Length > 0) { ExtractWithProgress(args[0]); return; }
        RunInstaller();
    }
}
