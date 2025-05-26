using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Yimmenu_Launchpad;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Yimmenu_Launchpad
{
    enum LauncherId
    {
        EGS = 0,
        STEAM,
        RSG,
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "LocalizableElement")]
    public partial class Launchpad : Form
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, int bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        private delegate IntPtr VirtualAllocExDelegate(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        private delegate int WriteProcessMemoryDelegate(IntPtr hProcess, IntPtr lpBaseAddress, byte[] buffer, uint size, int lpNumberOfBytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        private delegate IntPtr CreateRemoteThreadDelegate(IntPtr hProcess, IntPtr lpThreadAttribute, IntPtr dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("dwmapi.dll", SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, int[] pvAttribute, uint cbAttribute);

        //dark title bar
        protected override void OnHandleCreated(EventArgs e)
        {
            if (DwmSetWindowAttribute(Handle, 19, new[] { 1 }, 4) != 0)
            {
                DwmSetWindowAttribute(Handle, 20, new[] { 1 }, 4);
            }
        }

        private static Random random = new Random();

        // Don't forget to update the file version
        private const string launchpad_update_version = "1.9.3";
        private const string launchpad_display_version = "1.9.3";

        private string yimmenu_dir;
        private FileStream lockfile;
        private string yimmenu_dll;

        private const int width_simple = 248;
        private readonly int width_advanced;

        private string[] versions;
        private int download_progress = 0;

        private int gta_pid = 0;
        private bool game_was_open = false;
        private bool can_auto_inject = true;
        private bool any_successful_injection = false;

        public Launchpad()
        {
            yimmenu_dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Yimmenu";

            if (!Directory.Exists(yimmenu_dir))
            {
                Directory.CreateDirectory(yimmenu_dir);
            }
            if (!Directory.Exists(yimmenu_dir + "\\Bin"))
            {
                Directory.CreateDirectory(yimmenu_dir + "\\Bin");
            }

            if (File.Exists(yimmenu_dir + "\\Bin\\Launchpad.lock"))
            {
                try
                {
                    File.Delete(yimmenu_dir + "\\Bin\\Launchpad.lock");
                }
                catch (Exception)
                {
                    showMessageBox("Only one instance of the Launchpad can be open at a time.", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1);
                    return;
                }
            }
            lockfile = File.Create(yimmenu_dir + "\\Bin\\Launchpad.lock");

            InitializeComponent();
            width_advanced = Width;
            Text += " " + launchpad_display_version;
            LauncherType.DataSource = new[]
            {
                new DropDownEntry((int)LauncherId.STEAM, "Steam"),
                new DropDownEntry((int)LauncherId.EGS, "Epic Games"),
                new DropDownEntry((int)LauncherId.RSG, "Rockstar Games"),
            };

            try
            {
                bool read = Properties.Settings.Default.MustUpgrade;
            }
            catch (Exception)
            {
                showMessageBox("Your Launchpad configuration seems to be corrupted. The easiest fix for this is just heading into %localappdata%\\Calamity,_Inc\\ and deleting the Launchpad folders.", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(2);
                return;
            }

            if (Properties.Settings.Default.MustUpgrade)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.MustUpgrade = false;

                if (Properties.Settings.Default.Version == -1)
                {
                    // First-time user, set current config version.
                    Properties.Settings.Default.Version = 0;
                }
                else
                {
                    // Not a first-time user, may upgrade config here in the future.
                    /*if (Properties.Settings.Default.Version == 0)
					{
						// ...
						Properties.Settings.Default.Version = 1;
					}*/
                }

                Properties.Settings.Default.Save();
            }

            // Apply saved state
            AutoInjectCheckBox.Checked = Properties.Settings.Default.AutoInject;
            AutoInjectDelaySeconds.Value = Properties.Settings.Default.AutoInjectDelaySeconds;
            LauncherType.SelectedValue = Properties.Settings.Default.GameLauncher;
            if (!Properties.Settings.Default.Advanced)
            {
                updateAdvancedMode();
            }

            toggleInjectOrLaunchBtn(false);
            UpdateTimer.Start();
        }

        private async void UpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateTimer.Stop();

            // Await the async update check
            await checkForUpdate(false);

            // Update the GTA process ID and handle the update
            bool pidUpdated = UpdateGtaPid();
            ProcessGtaPidUpdate(false);

            if (gta_pid != 0)
            {
                InjectBtn.Focus();
            }

            ProcessScanTimer.Start();
        }


        private bool isYimmenuDll(FileInfo file)
        {
            return file.Name.StartsWith("Yimmenu ") && file.Name.EndsWith(".dll");
        }

        private string getYimmenuVersionFromDll(FileInfo file)
        {
            return file.Name.Substring(6, file.Name.Length - 6 - 4);
        }

        private async Task<int> checkForUpdate(bool recheck)
        {
            string releasesApiUrl = "https://api.github.com/repos/YimMenu/YimMenuV2/releases";
            string fallbackDllUrl = "https://github.com/YimMenu/YimMenuV2/releases/download/nightly/YimMenuV2.dll";
            string userAgent = "Mozilla/5.0"; // GitHub API requires a user-agent

            string dllDownloadUrl = "";
            string dllFileName = "";

            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);

            string buildIdFile = Path.Combine(yimmenu_dir, "build_id.txt");
            string dllPath = "";
            long latestReleaseId = 0;

            try
            {
                // Get releases JSON
                string releasesJson = await httpClient.GetStringAsync(releasesApiUrl);
                var releases = Newtonsoft.Json.Linq.JArray.Parse(releasesJson);
                var latestRelease = releases.FirstOrDefault();

                if (latestRelease != null)
                {
                    latestReleaseId = (long)latestRelease["id"];
                    var asset = latestRelease["assets"]
                        .FirstOrDefault(a => a["name"].ToString().EndsWith(".dll"));

                    if (asset != null)
                    {
                        dllDownloadUrl = asset["browser_download_url"].ToString();
                        dllFileName = asset["name"].ToString();
                    }
                    else
                    {
                        showMessageBox("DLL asset not found in the latest release. Falling back to nightly.");
                    }
                }
                else
                {
                    Console.WriteLine("No releases found. Falling back to nightly.");
                }
            }
            catch (Exception)
            {
                if (!recheck)
                {
                    showMessageBox("Failed to connect to GitHub API. Falling back to latest nightly download.");
                }
            }

            // Fallback logic
            if (string.IsNullOrEmpty(dllDownloadUrl))
            {
                dllDownloadUrl = fallbackDllUrl;
                dllFileName = "YimMenuV2.dll";
                latestReleaseId = 0; // fallback mode
            }

            dllPath = Path.Combine(yimmenu_dir, "Bin", dllFileName);

            // Read saved release ID from file
            long savedReleaseId = 0;
            if (File.Exists(buildIdFile))
            {
                try
                {
                    string idText = File.ReadAllText(buildIdFile); // Synchronous for compatibility
                    long.TryParse(idText, out savedReleaseId);
                }
                catch { }
            }

            // Download if release ID changed or DLL doesn't exist
            if (latestReleaseId != savedReleaseId || !File.Exists(dllPath))
            {
                try
                {
                    byte[] dllBytes = await httpClient.GetByteArrayAsync(dllDownloadUrl);
                    Directory.CreateDirectory(Path.Combine(yimmenu_dir, "Bin"));
                    File.WriteAllBytes(dllPath, dllBytes);
                    Console.WriteLine($"Downloaded {dllFileName} successfully.");

                    if (latestReleaseId != 0)
                    {
                        File.WriteAllText(buildIdFile, latestReleaseId.ToString()); // Synchronous for compatibility
                    }
                }
                catch (Exception ex)
                {
                    showMessageBox("Failed to download the DLL: " + ex.Message);
                    return -1;
                }
            }

            if (recheck)
            {
                saveSettings();
                DllList.Items.Clear();
            }

            if (!Properties.Settings.Default.Advanced)
            {
                updateAdvancedMode();
            }

            DllList.Items.Add("Yimmenu " + dllFileName.Replace(".dll", ""));

            if (!string.IsNullOrEmpty(Properties.Settings.Default.CustomDll))
            {
                foreach (string dll in Properties.Settings.Default.CustomDll.Split('|'))
                {
                    DllList.Items.Add(dll);
                }
            }

            for (int i = 0; i < DllList.Items.Count && i < Properties.Settings.Default.InjectDll.Length; i++)
            {
                if (Properties.Settings.Default.InjectDll.Substring(i, 1) == "1")
                {
                    DllList.Items[i].Checked = true;
                }
            }

            yimmenu_dll = dllPath;
            return 1;
        }


        private void onDownloadProgress(object sender, DownloadProgressChangedEventArgs e)
        {
            download_progress = e.ProgressPercentage;
        }

        private void onDownloadComplete(object sender, AsyncCompletedEventArgs e)
        {
            lock (e.UserState)
            {
                Monitor.Pulse(e.UserState);
            }
        }

        private async Task<bool> downloadYimmenuDll()
        {
            bool success = true;
            InfoText.Text = "Downloading latest YimMenu...";
            download_progress = 0;
            progressBar1.Value = 0;
            progressBar1.Show();

            string releasesApiUrl = "https://api.github.com/repos/YimMenu/YimMenuV2/releases";
            string userAgent = "Mozilla/5.0";
            string dllDownloadUrl = "";
            string dllFileName = "";

            string tempPath = "";

            try
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);

                    string releasesJson = await httpClient.GetStringAsync(releasesApiUrl);
                    var releases = Newtonsoft.Json.Linq.JArray.Parse(releasesJson);
                    var latestRelease = releases.FirstOrDefault();

                    if (latestRelease != null)
                    {
                        var asset = latestRelease["assets"].FirstOrDefault(a => a["name"].ToString().EndsWith(".dll"));
                        if (asset != null)
                        {
                            dllDownloadUrl = asset["browser_download_url"].ToString();
                            dllFileName = asset["name"].ToString();
                            yimmenu_dll = Path.Combine(yimmenu_dir, "Bin", dllFileName);
                            tempPath = yimmenu_dll + ".tmp";
                        }
                        else
                        {
                            showMessageBox("DLL asset not found in the latest release.");
                            return false;
                        }
                    }
                    else
                    {
                        showMessageBox("No releases found for YimMenu.");
                        return false;
                    }

                    using (HttpResponseMessage response = await httpClient.GetAsync(dllDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync(),
                                      fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            byte[] buffer = new byte[8192];
                            long totalRead = 0;
                            int bytesRead;

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;

                                if (totalBytes.HasValue)
                                {
                                    download_progress = (int)((totalRead * 100L) / totalBytes.Value);
                                    progressBar1.Invoke((MethodInvoker)(() => progressBar1.Value = download_progress));
                                }
                            }
                        }
                    }

                    File.Move(tempPath, yimmenu_dll);

                    if (new FileInfo(yimmenu_dll).Length < 1024)
                    {
                        File.Delete(yimmenu_dll);
                        showMessageBox("DLL download seems incomplete. Possibly blocked by antivirus.");
                        success = false;
                    }
                }
            }
            catch (Exception ex)
            {
                showMessageBox("Error downloading DLL from GitHub: " + ex.Message);
                success = false;
            }

            progressBar1.Hide();
            return success;
        }


        private void ProcessScanTimer_Tick(object sender, EventArgs e)
        {
            if (UpdateGtaPid())
            {
                ProcessGtaPidUpdate(can_auto_inject);
            }
        }

        private bool UpdateGtaPid()
        {
            foreach (Process process in Process.GetProcessesByName("GTA5_Enhanced"))
            {
                if (gta_pid != process.Id)
                {
                    gta_pid = process.Id;
                    game_was_open = true;
                    return true;
                }
                return false;
            }

            AutoInjectTimer.Stop();
            bool pidChanged = gta_pid != 0;
            gta_pid = 0;
            return pidChanged;
        }

        private void ProcessGtaPidUpdate(bool procCanAutoInject)
        {
            bool gameRunning = gta_pid != 0;
            toggleInjectOrLaunchBtn(gameRunning);

            if (gameRunning)
            {
                if (AutoInjectCheckBox.Checked && procCanAutoInject)
                {
                    if (Properties.Settings.Default.Advanced && AutoInjectDelaySeconds.Value > 0)
                    {
                        InfoText.Text = $"Automatically injecting in {AutoInjectDelaySeconds.Value} seconds...";
                        AutoInjectTimer.Interval = (int)AutoInjectDelaySeconds.Value * 1000;
                        AutoInjectTimer.Start();
                    }
                    else
                    {
                        _ = inject(); // Assume `Inject` is now async
                    }
                }
                else
                {
                    InfoText.Text = "Ready to inject.";
                }
            }
            else
            {
                InfoText.Text = "Ready to inject; just start the game.";
                if (game_was_open)
                {
                    game_was_open = false;
                    can_auto_inject = false;
                    GameClosedTimer.Start();
                }
            }
        }

        private void InjectBtn_Click(object sender, EventArgs e)
        {
            _ = inject(); // Ensure Inject is async
        }

        private void AutoInjectTimer_Tick(object sender, EventArgs e)
        {
            _ = inject(); // Ensure Inject is async
        }


        private async Task inject()
        {
            var failedBecauseOfAntiVirus = false;
            AutoInjectTimer.Stop();
            ProcessScanTimer.Stop();
            InjectBtn.Enabled = false;

            List<string> dlls = new List<string>();
            if (Properties.Settings.Default.Advanced)
            {
                for (int i = 0; i < DllList.Items.Count; i++)
                {
                    if (DllList.Items[i].Checked)
                    {
                        dlls.Add(i == 0 ? yimmenu_dll : DllList.Items[i].Text);
                    }
                }
            }
            else
            {
                dlls.Add(yimmenu_dll);
            }

            if (dlls.Contains(yimmenu_dll) && !File.Exists(yimmenu_dll))
            {
                bool success = await downloadYimmenuDll(); // Ensure your download function is async
                if (!success)
                {
                    dlls.Remove(yimmenu_dll);
                }
            }

            InfoText.Text = "Injecting...";
            int injected = 0;
            IntPtr pHandle = OpenProcess(1082u, 1, (uint)gta_pid);
            if (pHandle == IntPtr.Zero)
            {
                Console.WriteLine("Failed to get a hold of the game's process.");
            }
            else
            {
                IntPtr hKernel32 = GetModuleHandle("kernel32.dll");
                IntPtr procAddress = GetProcAddress(hKernel32, "LoadLibraryW");
                if (procAddress == IntPtr.Zero)
                {
                    Console.WriteLine("Failed to find LoadLibraryW.");
                }
                else
                {
                    string temp_dir = Path.Combine(yimmenu_dir, "Bin", "Temp");
                    if (!Directory.Exists(temp_dir))
                    {
                        Directory.CreateDirectory(temp_dir);
                    }
                    else
                    {
                        DirectoryInfo temp_di = new DirectoryInfo(temp_dir);
                        foreach (FileInfo file in temp_di.GetFiles())
                        {
                            try { file.Delete(); } catch { }
                        }
                    }

                    var VirtualAllocEx = (VirtualAllocExDelegate)Marshal.GetDelegateForFunctionPointer(
                        GetProcAddress(hKernel32, "VirtualAllocEx"), typeof(VirtualAllocExDelegate));
                    var WriteProcessMemory = (WriteProcessMemoryDelegate)Marshal.GetDelegateForFunctionPointer(
                        GetProcAddress(hKernel32, "WriteProcessMemory"), typeof(WriteProcessMemoryDelegate));
                    var CreateRemoteThread = (CreateRemoteThreadDelegate)Marshal.GetDelegateForFunctionPointer(
                        GetProcAddress(hKernel32, "CreateRemoteThread"), typeof(CreateRemoteThreadDelegate));

                    try
                    {
                        foreach (string dll in dlls)
                        {
                            if (!File.Exists(dll))
                            {
                                Console.WriteLine("Couldn't inject " + dll + " because the file doesn't exist.");
                                continue;
                            }

                            string dll_copy = Path.Combine(temp_dir, "YM_" + generateRandomString(5) + ".dll");
                            File.Copy(dll, dll_copy);

                            byte[] dllBytes = Encoding.Unicode.GetBytes(dll_copy);
                            IntPtr baseAddress = VirtualAllocEx(pHandle, IntPtr.Zero, (IntPtr)dllBytes.Length, 0x3000, 0x40);
                            if (baseAddress == IntPtr.Zero)
                            {
                                Console.WriteLine("Couldn't allocate memory for " + dll);
                                continue;
                            }

                            if (WriteProcessMemory(pHandle, baseAddress, dllBytes, (uint)dllBytes.Length, 0) == 0)
                            {
                                Console.WriteLine("Couldn't write " + dll_copy + " to allocated memory");
                                continue;
                            }

                            if (CreateRemoteThread(pHandle, IntPtr.Zero, IntPtr.Zero, procAddress, baseAddress, 0, IntPtr.Zero) == IntPtr.Zero)
                            {
                                Console.WriteLine("Failed to create remote thread for " + dll);
                                continue;
                            }

                            injected++;
                        }
                    }
                    catch (IOException)
                    {
                        this.Activate();
                        failedBecauseOfAntiVirus = true;
                        showMessageBox("Your antivirus seems to be preventing injection.\nDisable your antivirus or add an exclusion and try again.", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                CloseHandle(pHandle);
            }

            InfoText.Text = $"Injected {injected}/{dlls.Count} DLLs.";

            if (injected == 0)
            {
                if (!any_successful_injection && dlls.Count != 0 && !failedBecauseOfAntiVirus)
                {
                    showMessageBox("No DLL was injected.\n1. Ensure that BattlEye is disabled.\n2. If it still doesn't work, try running the Launchpad as Administrator.");
                }

                EnableReInject();
            }
            else
            {
                any_successful_injection = true;
                ReInjectTimer.Start();
            }
        }

        private static string generateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private DialogResult showMessageBox(string message, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
        {
            return MessageBox.Show(message, "Yimmenu Launchpad " + launchpad_display_version, buttons, icon);
        }

        private void Launchpad_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.AutoInject = AutoInjectCheckBox.Checked;
            Properties.Settings.Default.AutoInjectDelaySeconds = (int)AutoInjectDelaySeconds.Value;
            Properties.Settings.Default.GameLauncher = ((DropDownEntry)LauncherType.SelectedItem).Id;
            saveSettings();

            lockfile.Close();
            File.Delete(yimmenu_dir + "\\Bin\\Launchpad.lock");
        }

        private void saveSettings()
        {
            Properties.Settings.Default.InjectDll = "";
            Properties.Settings.Default.CustomDll = "";
            for (int i = 0; i < DllList.Items.Count; i++)
            {
                Properties.Settings.Default.InjectDll += (DllList.Items[i].Checked ? "1" : "0");
                if (i != 0)
                {
                    Properties.Settings.Default.CustomDll += DllList.Items[i].Text + "|";
                }
            }
            if (Properties.Settings.Default.CustomDll != "")
            {
                Properties.Settings.Default.CustomDll = Properties.Settings.Default.CustomDll.Substring(0, Properties.Settings.Default.CustomDll.Length - 1);
            }
            Properties.Settings.Default.Save();
        }

        private void CustomDllDialog_FileOk(object sender, CancelEventArgs e)
        {
            addDll(CustomDllDialog.FileName);
        }

        private void addDll(string path)
        {
            DllList.Items[DllList.Items.Add(path).Index].Checked = true;
        }

        private void AdvancedBtn_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Advanced = !Properties.Settings.Default.Advanced;
            updateAdvancedMode();
        }

        private void updateAdvancedMode()
        {
            if (Properties.Settings.Default.Advanced)
            {
                Width = width_advanced;
                MinimizeBox = true;
                InjectBtn.Text = "Inject";
                AutoInjectDelaySeconds.Visible = true;
                AddBtn.TabStop = true;
                RemoveBtn.TabStop = true;
                DllList.Visible = true;
            }
            else
            {
                MinimizeBox = false;
                Width = width_simple;
                if (versions != null)
                {
                    InjectBtn.Text = "Inject Yimmenu " + versions[1];
                }
                AutoInjectDelaySeconds.Visible = false;
                AddBtn.TabStop = false;
                RemoveBtn.TabStop = false;
                DllList.Visible = false;
            }
        }

        private void AddBtn_Click(object sender, EventArgs e)
        {
            CustomDllDialog.ShowDialog();
        }

        private void RemoveBtn_Click(object sender, EventArgs e)
        {
            removeSelectedDll();
        }

        private void UpBtn_Click(object sender, EventArgs e)
        {
            if (DllList.SelectedItems.Count == 1)
            {
                int selectedIndex = DllList.SelectedIndices[0];
                if (selectedIndex > 1)
                {
                    var selectedItem = DllList.Items[selectedIndex];
                    DllList.Items.RemoveAt(selectedIndex);
                    DllList.Items.Insert(selectedIndex - 1, selectedItem);
                    DllList.Items[selectedIndex - 1].Selected = true;
                    saveSettings();
                }
            }
        }

        private void DownBtn_Click(object sender, EventArgs e)
        {
            if (DllList.SelectedItems.Count == 1)
            {
                int selectedIndex = DllList.SelectedIndices[0];
                if (selectedIndex != 0 && selectedIndex < DllList.Items.Count - 1)
                {
                    var selectedItem = DllList.Items[selectedIndex];
                    DllList.Items.RemoveAt(selectedIndex);
                    DllList.Items.Insert(selectedIndex + 1, selectedItem);
                    DllList.Items[selectedIndex + 1].Selected = true;
                    saveSettings();
                }
            }
        }

        private void removeSelectedDll()
        {
            if (DllList.SelectedItems.Count == 1)
            {
                var selectedIndex = DllList.SelectedIndices[0];
                if (selectedIndex == 0)
                {
                    return;
                }

                DllList.Items.RemoveAt(selectedIndex);

                if (DllList.Items.Count > selectedIndex && DllList.Items[selectedIndex] != null)
                {
                    DllList.Items[selectedIndex].Selected = true;
                }
                else
                {
                    DllList.Items[selectedIndex - 1].Selected = true;
                }
            }
            else
            {
                for (var i = DllList.Items.Count - 1; i > 0; i--)
                {
                    if (DllList.Items[i].Selected)
                    {
                        DllList.Items.RemoveAt(i);
                    }
                }
            }
        }

        private void AutoInjectCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!AutoInjectCheckBox.Checked && AutoInjectTimer.Enabled)
            {
                AutoInjectTimer.Stop();
                InfoText.Text = "You may inject now.";
            }
        }

        private void GameClosedTimer_Tick(object sender, EventArgs e)
        {
            GameClosedTimer.Stop();
            can_auto_inject = true;
        }

        //private void ChangelogBtn_Click(object sender, EventArgs e)
        //{
        //    (new Changelog()).Show();
        //}

        private void ReInjectTimer_Tick(object sender, EventArgs e)
        {
            ReInjectTimer.Stop();
            EnableReInject();
        }

        private void EnableReInject()
        {
            InjectBtn.Enabled = true;
            ProcessScanTimer.Start();
        }

        private void YimFolderBtn_Click(object sender, EventArgs e)
        {
            Process.Start(yimmenu_dir);
        }

        private async void UpdCheckBtn_Click(object sender, EventArgs e)
        {
            if (await checkForUpdate(true) == 0)
            {
                showMessageBox("Everything up-to-date.");
            }
            ProcessGtaPidUpdate(false);
        }
        private void DllList_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void DllList_DragDrop(object sender, DragEventArgs e)
        {
            foreach (string file in (string[])e.Data.GetData(DataFormats.FileDrop))
            {
                addDll(file);
            }
        }

        private void DllList_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                removeSelectedDll();
            }
        }

        private void LaunchBtn_Click(object sender, EventArgs e)
        {
            switch (((DropDownEntry)LauncherType.SelectedItem).Id)
            {
                case (int)LauncherId.EGS:
                    Process.Start("com.epicgames.launcher://apps/9d2d0eb64d5c44529cece33fe2a46482?action=launch&silent=true");
                    break;
                case (int)LauncherId.STEAM:
                    object steamKeyValue = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
                    if (steamKeyValue != null && !string.IsNullOrWhiteSpace(steamKeyValue.ToString()))
                    {
                        Process.Start("steam://run/271590");
                    }
                    else
                    {
                        showMessageBox("Whoops, looks like Steam isn't installed. Try selecting a different launcher in the dropdown.", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    }
                    break;
                case (int)LauncherId.RSG:
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Rockstar Games\\Grand Theft Auto V"))
                        {
                            var path = (string)key?.GetValue("InstallFolder");
                            if (path != null)
                            {
                                Process.Start(path + "\\PlayGTAV.exe");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                    break;
            }
        }

        private void LauncherType_SelectedIndexChanged(object sender, EventArgs e)
        {
            LaunchBtn.Focus();
        }

        private void toggleInjectOrLaunchBtn(bool gameRunning)
        {
            InjectBtn.Visible = gameRunning;
            LauncherType.Visible = LaunchBtn.Visible = !gameRunning;
        }
    }
}