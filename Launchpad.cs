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
using Microsoft.Win32;

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

		private string Yimmenu_dir;
		private FileStream lockfile;
		private string Yimmenu_dll;

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
			Yimmenu_dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Yimmenu";

			if (!Directory.Exists(Yimmenu_dir))
			{
				Directory.CreateDirectory(Yimmenu_dir);
			}
			if (!Directory.Exists(Yimmenu_dir + "\\Bin"))
			{
				Directory.CreateDirectory(Yimmenu_dir + "\\Bin");
			}

			if (File.Exists(Yimmenu_dir + "\\Bin\\Launchpad.lock"))
			{
				try
				{
					File.Delete(Yimmenu_dir + "\\Bin\\Launchpad.lock");
				}
				catch (Exception)
				{
					showMessageBox("Only one instance of the Launchpad can be open at a time.", MessageBoxButtons.OK, MessageBoxIcon.Error);
					Environment.Exit(1);
					return;
				}
			}
			lockfile = File.Create(Yimmenu_dir + "\\Bin\\Launchpad.lock");

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
				bool read = Yimmenu_Launchpad.Properties.Settings.Default.MustUpgrade;
			}
			catch (Exception)
			{
				showMessageBox("Your Launchpad configuration seems to be corrupted. The easiest fix for this is just heading into %localappdata%\\Calamity,_Inc\\ and deleting the Launchpad folders.", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Environment.Exit(2);
				return;
			}

			if (Yimmenu_Launchpad.Properties.Settings.Default.MustUpgrade)
			{
				Yimmenu_Launchpad.Properties.Settings.Default.Upgrade();
				Yimmenu_Launchpad.Properties.Settings.Default.MustUpgrade = false;

				if (Yimmenu_Launchpad.Properties.Settings.Default.Version == -1)
				{
					// First-time user, set current config version.
					Yimmenu_Launchpad.Properties.Settings.Default.Version = 0;
				}
				else
				{
					// Not a first-time user, may upgrade config here in the future.
					/*if (Yimmenu_Launchpad.Properties.Settings.Default.Version == 0)
					{
						// ...
						Yimmenu_Launchpad.Properties.Settings.Default.Version = 1;
					}*/
				}

				Yimmenu_Launchpad.Properties.Settings.Default.Save();
			}

			// Apply saved state
			AutoInjectCheckBox.Checked = Yimmenu_Launchpad.Properties.Settings.Default.AutoInject;
			AutoInjectDelaySeconds.Value = Yimmenu_Launchpad.Properties.Settings.Default.AutoInjectDelaySeconds;
			LauncherType.SelectedValue = Yimmenu_Launchpad.Properties.Settings.Default.GameLauncher;
			if (!Yimmenu_Launchpad.Properties.Settings.Default.Advanced)
			{
				updateAdvancedMode();
			}

			toggleInjectOrLaunchBtn(false);
			UpdateTimer.Start();
		}

        private async void UpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateTimer.Stop();

            await checkForUpdateAsync(false); // ✅ FIXED: await the async update checker

            updateGtaPid();
            processGtaPidUpdate(false);

            if (gta_pid != 0)
                InjectBtn.Focus();

            ProcessScanTimer.Start();
        }

        private bool isYimmenuDll(FileInfo file)
		{
			return file.Name.StartsWith("Stand ") && file.Name.EndsWith(".dll");
		}

		private string getYimmenuVersionFromDll(FileInfo file)
		{
			return file.Name.Substring(6, file.Name.Length - 6 - 4);
		}

        private async Task<int> checkForUpdateAsync(bool recheck)
        {
            string githubReleasesUrl = "https://github.com/YimMenu/YimMenuV2/releases/tag/nightly";
            string downloadBaseUrl = "https://github.com";
            string latestDllUrl = "";
            string latestVersion = "nightly";

            // Scrape GitHub for .dll download link
            try
            {
                HttpClient client = new HttpClient();
                string html = await client.GetStringAsync(githubReleasesUrl);

                var dllMatch = System.Text.RegularExpressions.Regex.Match(html, @"href=\""(/YimMenu/YimMenuV2/releases/download/nightly/[^""]+\.dll)\""");
                if (dllMatch.Success)
                {
                    latestDllUrl = downloadBaseUrl + dllMatch.Groups[1].Value;
                }
                else
                {
                    showMessageBox("Failed to find DLL in YimMenu GitHub release.");
                    return -1;
                }
            }
            catch (Exception ex)
            {
                showMessageBox("Failed to check GitHub for updates:\n" + ex.Message);
                return -1;
            }

            DirectoryInfo bin_di = new DirectoryInfo(Yimmenu_dir + "\\Bin\\");
            string dllPath = Path.Combine(Yimmenu_dir, "Bin", "YimMenu_" + latestVersion + ".dll");
            Yimmenu_dll = dllPath;

            if (!File.Exists(dllPath))
            {
                try
                {
                    HttpClient client = new HttpClient();
                    byte[] dllData = await client.GetByteArrayAsync(latestDllUrl);
                    File.WriteAllBytes(dllPath, dllData);

                    // Delete old YimMenu DLLs
                    foreach (FileInfo file in bin_di.GetFiles())
                    {
                        if (isYimmenuDll(file) && file.FullName != dllPath)
                        {
                            try { file.Delete(); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    showMessageBox("Failed to download YimMenu DLL:\n" + ex.Message);
                    return -1;
                }
            }

            if (recheck)
            {
                saveSettings();
                DllList.Items.Clear();
            }

            if (!Yimmenu_Launchpad.Properties.Settings.Default.Advanced)
            {
                updateAdvancedMode();
            }

            DllList.Items.Add("YimMenu " + latestVersion);
            if (Yimmenu_Launchpad.Properties.Settings.Default.CustomDll != "")
            {
                foreach (string dll in Yimmenu_Launchpad.Properties.Settings.Default.CustomDll.Split('|'))
                {
                    DllList.Items.Add(dll);
                }
            }

            for (int i = 0; i < Yimmenu_Launchpad.Properties.Settings.Default.InjectDll.Length; i++)
            {
                if (Yimmenu_Launchpad.Properties.Settings.Default.InjectDll.Substring(i, 1) == "1")
                {
                    DllList.Items[i].Checked = true;
                }
            }

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

		private bool downloadYimmenuDll()
		{
			bool success = true;
			InfoText.Text = "Downloading Yimmenu " + versions[1] + "...";
			download_progress = 0;
			progressBar1.Show();
			var t = Task.Run(() =>
			{
				WebClient webClient = new WebClient();
				webClient.DownloadProgressChanged += onDownloadProgress;
				webClient.DownloadFileCompleted += onDownloadComplete;
				var syncObject = new object();
				lock (syncObject)
				{
					webClient.DownloadFileAsync(new Uri("https://stand.sh/Stand%20" + versions[1] + ".dll"), Yimmenu_dll + ".tmp", syncObject);
					Monitor.Wait(syncObject);
				}
			});
			do
			{
				progressBar1.Value = download_progress;
			}
			while (!t.Wait(20));
			File.Move(Yimmenu_dll + ".tmp", Yimmenu_dll);
			if (new FileInfo(Yimmenu_dll).Length < 1024)
			{
				File.Delete(Yimmenu_dll);
				showMessageBox("It looks like the DLL download has failed. Ensure you have no antivirus program interfering.");
				success = false;
			}
			progressBar1.Hide();
			return success;
		}

		private void ProcessScanTimer_Tick(object sender, EventArgs e)
		{
			if (updateGtaPid())
			{
				processGtaPidUpdate(can_auto_inject);
			}
		}

		private bool updateGtaPid()
		{
			foreach (Process process in Process.GetProcesses())
			{
				if (process.ProcessName == "GTA5")
				{
					if (gta_pid == process.Id)
					{
						return false;
					}
					gta_pid = process.Id;
					game_was_open = true;
					return true;
				}
			}
			AutoInjectTimer.Stop();
			var pid_changed = gta_pid != 0;
			gta_pid = 0;
			return pid_changed;
		}

		private void processGtaPidUpdate(bool proc_can_auto_inject)
		{
			var gameRunning = (gta_pid != 0);
			toggleInjectOrLaunchBtn(gameRunning);
			if (gameRunning)
			{
				if (AutoInjectCheckBox.Checked && proc_can_auto_inject)
				{
					if (Yimmenu_Launchpad.Properties.Settings.Default.Advanced && AutoInjectDelaySeconds.Value > 0)
					{
						InfoText.Text = "Automatically injecting in a few seconds...";
						AutoInjectTimer.Interval = (int)AutoInjectDelaySeconds.Value * 1000;
						AutoInjectTimer.Start();
					}
					else
					{
						inject();
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
			inject();
		}

		private void AutoInjectTimer_Tick(object sender, EventArgs e)
		{
			inject();
		}

		private void inject()
		{
			var failedBecauseOfAntiVirus = false;
			AutoInjectTimer.Stop();
			ProcessScanTimer.Stop();
			InjectBtn.Enabled = false;
			List<string> dlls = new List<string>();
			if (Yimmenu_Launchpad.Properties.Settings.Default.Advanced)
			{
				for (int i = 0; i < DllList.Items.Count; i++)
				{
					if (DllList.Items[i].Checked)
					{
						dlls.Add(i == 0 ? Yimmenu_dll : DllList.Items[i].Text);
					}
				}
			}
			else
			{
				dlls.Add(Yimmenu_dll);
			}
			if (dlls.Contains(Yimmenu_dll) && !File.Exists(Yimmenu_dll))
			{
				if (!downloadYimmenuDll())
				{
					dlls.Remove(Yimmenu_dll);
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
					string temp_dir = Yimmenu_dir + "\\Bin\\Temp";
					if (!Directory.Exists(temp_dir))
					{
						Directory.CreateDirectory(temp_dir);
					}
					else
					{
						DirectoryInfo temp_di = new DirectoryInfo(temp_dir);
						foreach (FileInfo file in temp_di.GetFiles())
						{
							try
							{
								file.Delete();
							}
							catch (Exception)
							{
							}
						}
					}
					var VirtualAllocEx = (VirtualAllocExDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hKernel32, "VirtualAllocEx"), typeof(VirtualAllocExDelegate));
					var WriteProcessMemory = (WriteProcessMemoryDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hKernel32, "WriteProcessMemory"), typeof(WriteProcessMemoryDelegate));
					var CreateRemoteThread = (CreateRemoteThreadDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hKernel32, "CreateRemoteThread"), typeof(CreateRemoteThreadDelegate));
					try
					{
						foreach (string dll in dlls)
						{
							if (!File.Exists(dll))
							{
								Console.WriteLine("Couldn't inject " + dll + " because the file doesn't exist.");
								continue;
							}
							string dll_copy = temp_dir + "\\SL_" + generateRandomString(5) + ".dll";
							File.Copy(dll, dll_copy);
							byte[] dllBytes = Encoding.Unicode.GetBytes(dll_copy);
							IntPtr baseAddress = VirtualAllocEx(pHandle, (IntPtr)null, (IntPtr)dllBytes.Length, 12288u, 64u);
							if (baseAddress == IntPtr.Zero)
							{
								Console.WriteLine("Couldn't allocate the bytes to represent " + dll);
								continue;
							}
							if (WriteProcessMemory(pHandle, baseAddress, dllBytes, (uint)dllBytes.Length, 0) == 0)
							{
								Console.WriteLine("Couldn't write " + dll_copy + " to allocated memory");
								continue;
							}
							if (CreateRemoteThread(pHandle, (IntPtr)null, IntPtr.Zero, procAddress, baseAddress, 0u, (IntPtr)null) == IntPtr.Zero)
							{
								Console.WriteLine("Failed to create remote thread for " + dll);
								continue;
							}
							injected++;
						}
					}catch(IOException)
					{
						this.Activate();
						failedBecauseOfAntiVirus = true;
						showMessageBox("Your antivirus seems to be preventing injection.\nDisable your antivirus or add an exclusion and try again.", MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
				CloseHandle(pHandle);
			}
			InfoText.Text = "Injected " + injected.ToString() + "/" + dlls.Count.ToString() + " DLLs.";

			if (injected == 0)
			{
				if (!any_successful_injection
					&& dlls.Count != 0
					&& !failedBecauseOfAntiVirus
					)
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
			Yimmenu_Launchpad.Properties.Settings.Default.AutoInject = AutoInjectCheckBox.Checked;
			Yimmenu_Launchpad.Properties.Settings.Default.AutoInjectDelaySeconds = (int)AutoInjectDelaySeconds.Value;
			Yimmenu_Launchpad.Properties.Settings.Default.GameLauncher = ((DropDownEntry)LauncherType.SelectedItem).Id;
			saveSettings();

			lockfile.Close();
			File.Delete(Yimmenu_dir + "\\Bin\\Launchpad.lock");
		}

		private void saveSettings()
		{
			Yimmenu_Launchpad.Properties.Settings.Default.InjectDll = "";
			Yimmenu_Launchpad.Properties.Settings.Default.CustomDll = "";
			for (int i = 0; i < DllList.Items.Count; i++)
			{
				Yimmenu_Launchpad.Properties.Settings.Default.InjectDll += (DllList.Items[i].Checked ? "1" : "0");
				if (i != 0)
				{
					Yimmenu_Launchpad.Properties.Settings.Default.CustomDll += DllList.Items[i].Text + "|";
				}
			}
			if (Yimmenu_Launchpad.Properties.Settings.Default.CustomDll != "")
			{
				Yimmenu_Launchpad.Properties.Settings.Default.CustomDll = Yimmenu_Launchpad.Properties.Settings.Default.CustomDll.Substring(0, Yimmenu_Launchpad.Properties.Settings.Default.CustomDll.Length - 1);
			}
			Yimmenu_Launchpad.Properties.Settings.Default.Save();
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
			Yimmenu_Launchpad.Properties.Settings.Default.Advanced = !Yimmenu_Launchpad.Properties.Settings.Default.Advanced;
			updateAdvancedMode();
		}

		private void updateAdvancedMode()
		{
			if (Yimmenu_Launchpad.Properties.Settings.Default.Advanced)
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

		private void ChangelogBtn_Click(object sender, EventArgs e)
		{
			(new Changelog()).Show();
		}

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

		private void YimmenuFolderBtn_Click(object sender, EventArgs e)
		{
			Process.Start(Yimmenu_dir);
		}

        private async void UpdCheckBtn_Click(object sender, EventArgs e)
        {
            if (await checkForUpdateAsync(true) == 0)
            {
                showMessageBox("Everything up-to-date.");
            }

            processGtaPidUpdate(false);
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
					Process.Start("com.epicgames.launcher://apps/8769e24080ea413b8ebca3f1b8c50951?action=launch&silent=true");
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
						using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Rockstar Games\\Grand Theft Auto V Enhanced"))
						{
							var path = (string) key?.GetValue("InstallFolder");
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