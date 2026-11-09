using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ZipHere")]
[assembly: AssemblyDescription("Extract-here context menu and automatic unpacking of downloaded ZIP, RAR, 7z and TAR archives")]
[assembly: AssemblyProduct("ZipHere")]
[assembly: AssemblyCompany("ZipHere contributors")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

static class Program
{
    const string MenuText = "Распаковать здесь";
    const string WatchArg = "--watch";
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string SettingsKeyPath = @"Software\ZipHere";

    // Расширения, для которых добавляется пункт в меню правой кнопки.
    static readonly string[] MenuExts = new string[]
    {
        ".zip", ".rar", ".7z", ".tar", ".tgz", ".tbz2", ".txz", ".gz", ".bz2", ".xz"
    };

    // Расширения, которые автоматически распаковываются в папке «Загрузки».
    // (.gz / .bz2 / .xz по отдельности не трогаем: это бывают и одиночные сжатые файлы.)
    static readonly string[] AutoExts = new string[]
    {
        ".zip", ".rar", ".7z", ".tar", ".tgz", ".tbz2", ".txz", ".tar.gz", ".tar.bz2", ".tar.xz"
    };

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

    static string MenuKeyPath(string ext)
    {
        return @"Software\Classes\SystemFileAssociations\" + ext + @"\shell\ZipHere";
    }

    // ================= Определение формата =================

    // Определяет формат по первым байтам файла, а не по расширению:
    // «.zip», который на самом деле RAR (и наоборот), встречается постоянно.
    // Возвращает: zip, rar, 7z, gz, bz2, xz, tar, html или unknown.
    static string Sniff(string path)
    {
        try
        {
            byte[] b = new byte[512];
            int n = 0;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int r;
                while (n < b.Length && (r = fs.Read(b, n, b.Length - n)) > 0) n += r;
            }

            if (n >= 4 && b[0] == 0x50 && b[1] == 0x4B &&
                ((b[2] == 3 && b[3] == 4) || (b[2] == 5 && b[3] == 6) || (b[2] == 7 && b[3] == 8)))
                return "zip";
            if (n >= 6 && b[0] == 0x52 && b[1] == 0x61 && b[2] == 0x72 && b[3] == 0x21 && b[4] == 0x1A && b[5] == 0x07)
                return "rar";
            if (n >= 6 && b[0] == 0x37 && b[1] == 0x7A && b[2] == 0xBC && b[3] == 0xAF && b[4] == 0x27 && b[5] == 0x1C)
                return "7z";
            if (n >= 2 && b[0] == 0x1F && b[1] == 0x8B) return "gz";
            if (n >= 3 && b[0] == 0x42 && b[1] == 0x5A && b[2] == 0x68) return "bz2";
            if (n >= 6 && b[0] == 0xFD && b[1] == 0x37 && b[2] == 0x7A && b[3] == 0x58 && b[4] == 0x5A && b[5] == 0x00)
                return "xz";
            if (n >= 262 && b[257] == (byte)'u' && b[258] == (byte)'s' && b[259] == (byte)'t' &&
                b[260] == (byte)'a' && b[261] == (byte)'r')
                return "tar";

            // Неудачное скачивание часто даёт HTML-страницу с расширением архива.
            string head = Encoding.ASCII.GetString(b, 0, Math.Min(n, 200)).ToLowerInvariant();
            if (head.Contains("<!doctype html") || head.Contains("<html")) return "html";
        }
        catch { }
        return "unknown";
    }

    static bool IsAutoArchive(string path)
    {
        string name = Path.GetFileName(path);
        if (IsSecondaryVolume(name)) return false;
        foreach (string e in AutoExts)
            if (name.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Второй и последующие тома многотомного RAR (name.part2.rar, name.r00 ...):
    // их не трогаем, распаковывается только первый том.
    static bool IsSecondaryVolume(string name)
    {
        Match m = Regex.Match(name, @"\.part0*(\d+)\.rar$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            int num;
            if (int.TryParse(m.Groups[1].Value, out num) && num > 1) return true;
            return false;
        }
        return Regex.IsMatch(name, @"\.r\d\d$", RegexOptions.IgnoreCase);
    }

    // Первый том многотомного RAR: рядом лежат следующие тома.
    static bool IsMultiVolumeStart(string full)
    {
        try
        {
            string name = Path.GetFileName(full);
            string dir = Path.GetDirectoryName(full);
            Match m = Regex.Match(name, @"^(.*)\.part0*1\.rar$", RegexOptions.IgnoreCase);
            if (m.Success)
                return Directory.GetFiles(dir, m.Groups[1].Value + ".part*.rar").Length > 1;
            if (name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                return File.Exists(Path.ChangeExtension(full, ".r00"));
        }
        catch { }
        return false;
    }

    static string FolderNameFor(string full)
    {
        string name = Path.GetFileNameWithoutExtension(full);
        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);
        name = Regex.Replace(name, @"\.part0*1$", "", RegexOptions.IgnoreCase);
        name = name.TrimEnd(' ', '.');
        if (name.Length == 0) name = "archive";
        return name;
    }

    // ================= Внешние распаковщики =================

    static string FindInPath(string exeName)
    {
        try
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return null;
            foreach (string d in path.Split(';'))
            {
                if (d.Trim().Length == 0) continue;
                string p = Path.Combine(d.Trim().Trim('"'), exeName);
                if (File.Exists(p)) return p;
            }
        }
        catch { }
        return null;
    }

    static string FirstExisting(string[] candidates)
    {
        foreach (string c in candidates)
        {
            try { if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c; } catch { }
        }
        return null;
    }

    static string ProgramFilesDir(string variable)
    {
        return Environment.GetEnvironmentVariable(variable);
    }

    static string Find7z()
    {
        string pf = ProgramFilesDir("ProgramFiles");
        string pf86 = ProgramFilesDir("ProgramFiles(x86)");
        string pf64 = ProgramFilesDir("ProgramW6432");
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string found = FirstExisting(new string[]
        {
            pf == null ? null : Path.Combine(pf, @"7-Zip\7z.exe"),
            pf64 == null ? null : Path.Combine(pf64, @"7-Zip\7z.exe"),
            pf86 == null ? null : Path.Combine(pf86, @"7-Zip\7z.exe"),
            Path.Combine(local, @"Programs\7-Zip\7z.exe")
        });
        return found ?? FindInPath("7z.exe");
    }

    static string FindUnrar()
    {
        string pf = ProgramFilesDir("ProgramFiles");
        string pf86 = ProgramFilesDir("ProgramFiles(x86)");
        string pf64 = ProgramFilesDir("ProgramW6432");
        string found = FirstExisting(new string[]
        {
            pf == null ? null : Path.Combine(pf, @"WinRAR\UnRAR.exe"),
            pf64 == null ? null : Path.Combine(pf64, @"WinRAR\UnRAR.exe"),
            pf86 == null ? null : Path.Combine(pf86, @"WinRAR\UnRAR.exe")
        });
        return found ?? FindInPath("UnRAR.exe");
    }

    // tar.exe (bsdtar на libarchive) входит в Windows 10/11 и умеет читать RAR, 7z, tar.* и др.
    static string FindTar()
    {
        string sys = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (File.Exists(sys)) return sys;
        return FindInPath("tar.exe");
    }

    static string Quote(string s)
    {
        return "\"" + s + "\"";
    }

    static Encoding ConsoleEncoding()
    {
        try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch { return Encoding.Default; }
    }

    // Запускает процесс без окна, ждёт завершения, возвращает код выхода и вывод (хвост).
    static int RunProcess(string exe, string args, string workDir, out string output)
    {
        ProcessStartInfo psi = new ProcessStartInfo(exe, args);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = workDir;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true; // закрываем сразу: чтобы программа не ждала ввод пароля
        Encoding enc = ConsoleEncoding();
        psi.StandardOutputEncoding = enc;
        psi.StandardErrorEncoding = enc;

        StringBuilder sb = new StringBuilder();
        using (Process p = Process.Start(psi))
        {
            DataReceivedEventHandler handler = delegate (object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (sb)
                {
                    sb.AppendLine(e.Data);
                    if (sb.Length > 6000) sb.Remove(0, sb.Length - 4000);
                }
            };
            p.OutputDataReceived += handler;
            p.ErrorDataReceived += handler;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            try { p.StandardInput.Close(); } catch { }
            p.WaitForExit();
            p.WaitForExit(); // дочитать потоки вывода
            lock (sb) { output = sb.ToString(); }
            return p.ExitCode;
        }
    }

    // Достаёт из вывода программы короткое понятное описание ошибки.
    static string ShortError(string output, int code)
    {
        List<string> lines = new List<string>();
        foreach (string raw in output.Split('\n'))
        {
            string l = raw.Trim();
            if (l.Length > 0) lines.Add(l);
        }
        List<string> pick = new List<string>();
        foreach (string l in lines)
        {
            if (l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 && pick.Count < 3) pick.Add(l);
        }
        if (pick.Count == 0)
        {
            for (int i = Math.Max(0, lines.Count - 3); i < lines.Count; i++) pick.Add(lines[i]);
        }
        string text = string.Join("\n", pick.ToArray());
        if (text.Length == 0) text = "Распаковщик завершился с кодом " + code + ".";
        if (text.Length > 400) text = text.Substring(0, 400) + "…";
        return text;
    }

    // Все Run*-методы возвращают null при успехе или текст ошибки.
    static string Run7z(string exe, string archive, string dest)
    {
        string output;
        int code = RunProcess(exe, "x -y -bd -o" + Quote(dest) + " " + Quote(archive), dest, out output);
        return (code == 0 || code == 1) ? null : ShortError(output, code); // 1 = предупреждения
    }

    static string RunUnrar(string exe, string archive, string dest)
    {
        // Без аргумента назначения UnRAR распаковывает в текущую папку (мы её задаём как рабочую).
        string output;
        int code = RunProcess(exe, "x -y -p- -idq " + Quote(archive), dest, out output);
        return (code == 0 || code == 1) ? null : ShortError(output, code);
    }

    static string RunTar(string exe, string archive, string dest)
    {
        string output;
        int code = RunProcess(exe, "-xf " + Quote(archive) + " -C " + Quote(dest), dest, out output);
        return code == 0 ? null : ShortError(output, code);
    }

    static void ResetDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }

    // Очищает папку назначения и пробует один распаковщик.
    static string Attempt(string dest, Func<string> run)
    {
        try
        {
            ResetDir(dest);
            Directory.CreateDirectory(dest);
            return run();
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    // Возвращает null при успехе, иначе текст последней ошибки.
    static string ExtractExternal(string archive, string dest, string kind)
    {
        bool isRar = kind == "rar" ||
            (kind == "unknown" && archive.EndsWith(".rar", StringComparison.OrdinalIgnoreCase));
        bool multi = IsMultiVolumeStart(archive);
        string lastError = null;

        // 1. 7-Zip, если установлен: самый полный по форматам.
        string z = Find7z();
        if (z != null)
        {
            lastError = Attempt(dest, delegate { return Run7z(z, archive, dest); });
            if (lastError == null) return null;
        }

        // 2. UnRAR из WinRAR, если установлен.
        if (isRar)
        {
            string u = FindUnrar();
            if (u != null)
            {
                lastError = Attempt(dest, delegate { return RunUnrar(u, archive, dest); });
                if (lastError == null) return null;
            }
        }

        // 3. tar.exe, встроенный в Windows. Многотомные архивы он не собирает.
        if (!multi)
        {
            string t = FindTar();
            if (t != null)
            {
                lastError = Attempt(dest, delegate { return RunTar(t, archive, dest); });
                if (lastError == null) return null;
            }
        }

        if (lastError == null)
            lastError = multi
                ? "Это многотомный архив: для него нужен 7-Zip или WinRAR."
                : "В системе не найден распаковщик для этого формата.";
        return lastError;
    }

    static void Extract(string archive, string dest)
    {
        string kind = Sniff(archive);

        if (kind == "html")
            throw new IOException("Это не архив, а веб-страница: скачивание, скорее всего, не удалось. Скачайте файл заново.");

        string zipError = null;
        if (kind == "zip")
        {
            try
            {
                ZipFile.ExtractToDirectory(archive, dest);
                return;
            }
            catch (Exception e)
            {
                zipError = e.Message;
                ResetDir(dest);
            }
        }

        string error = ExtractExternal(archive, dest, kind);
        if (error == null) return;

        string msg = zipError != null ? zipError : error;
        string low = msg.ToLowerInvariant();
        if (low.Contains("password") || low.Contains("passphrase") || low.Contains("encrypt") || low.Contains("парол"))
            msg = "Архив защищён паролем: ZipHere такие не распаковывает.\n\n" + msg;
        else
            msg += "\n\nЕсли архив целый, но не открывается, установите 7-Zip или WinRAR: ZipHere подхватит их автоматически.";
        throw new IOException(msg);
    }

    // ================= Распаковка =================

    // Распаковывает архив в папку рядом с ним и переносит сам архив в корзину.
    // Если распаковка не удалась, архив остаётся на месте, а недоделанная папка удаляется.
    static string Unpack(string archivePath, out bool archiveRecycled)
    {
        archiveRecycled = false;
        string full = Path.GetFullPath(archivePath);
        string dir = Path.GetDirectoryName(full);
        string target = Path.Combine(dir, FolderNameFor(full));
        string candidate = target;
        int n = 1;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = target + "_" + (n++);

        try
        {
            Extract(full, candidate);
        }
        catch
        {
            ResetDir(candidate);
            throw;
        }

        // Многотомный архив оставляем: его остальные тома нужны для полной распаковки.
        if (!IsMultiVolumeStart(full))
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                archiveRecycled = true;
            }
            catch { }
        }
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
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(MenuKeyPath(".zip")))
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

        // Пункт контекстного меню для каждого поддерживаемого расширения
        foreach (string ext in MenuExts)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(MenuKeyPath(ext)))
            {
                k.SetValue("", MenuText);
                k.SetValue("Icon", "zipfldr.dll");
            }
            using (RegistryKey c = Registry.CurrentUser.CreateSubKey(MenuKeyPath(ext) + @"\command"))
            {
                c.SetValue("", "\"" + InstalledExe + "\" \"%1\"");
            }
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
        foreach (string ext in MenuExts)
            Registry.CurrentUser.DeleteSubKeyTree(MenuKeyPath(ext), false);
        using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
        {
            if (run != null) run.DeleteValue("ZipHere", false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(SettingsKeyPath, false);
        try { if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true); } catch { }
    }

    // ================= Распаковка из меню правой кнопки =================

    static void ExtractWithProgress(string archivePath)
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
        label.Text = Path.GetFileName(archivePath);
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
                Unpack(archivePath, out recycled);
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

        // Браузеры качают во временный файл и в конце переименовывают его в архив,
        // поэтому слушаем и создание, и переименование.
        void OnFile(string path)
        {
            if (!autoEnabled) return;
            if (!IsAutoArchive(path)) return;
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
                    "Папка: " + Path.GetFileName(target) + (recycled ? "\nАрхив перемещён в корзину" : "\nАрхив оставлен на месте"),
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
            // Лимиты Windows: заголовок до 63 символов, текст до 255.
            if (title.Length > 63) title = title.Substring(0, 60) + "...";
            if (text.Length > 250) text = text.Substring(0, 247) + "...";
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
                    "• Пункт «" + MenuText + "» в меню правой кнопки для архивов ZIP, RAR, 7z, TAR и др.\n" +
                    "• Автозапуск вместе с Windows: новые архивы в папке «Загрузки» распаковываются автоматически.\n" +
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
