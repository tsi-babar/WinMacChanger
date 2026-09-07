using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinMacChanger
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    /// <summary>One network adapter and where its MAC lives in the registry.</summary>
    internal sealed class Adapter
    {
        public string Description;     // DriverDesc
        public string Guid;            // NetCfgInstanceId, e.g. {....}
        public string ConnectionName;  // NetConnectionID, e.g. "Ethernet"
        public string RegistryKeyPath; // ...Class\{4d36e972...}\NNNN
        public string CurrentMac;      // formatted MAC reported by WMI

        public override string ToString() =>
            (string.IsNullOrEmpty(ConnectionName) ? Description : ConnectionName) + "  [" + Description + "]";
    }

    internal sealed class MainForm : Form
    {
        // Network adapters class GUID — the same on every Windows machine.
        private const string ClassKey =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        private ComboBox _adapters;
        private Label _current;
        private TextBox _newMac;
        private Button _random, _apply, _restore, _refresh;
        private Label _status;

        public MainForm()
        {
            Text = "WinMacChanger";
            ClientSize = new Size(452, 232);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var lblA = new Label { Text = "Adapter:", Left = 12, Top = 16, Width = 70 };
            _adapters = new ComboBox { Left = 88, Top = 12, Width = 348, DropDownStyle = ComboBoxStyle.DropDownList };
            _adapters.SelectedIndexChanged += (s, e) => ShowCurrent();

            _current = new Label { Left = 88, Top = 46, Width = 348, Text = "Current MAC: -" };

            var lblN = new Label { Text = "New MAC:", Left = 12, Top = 82, Width = 70 };
            _newMac = new TextBox { Left = 88, Top = 79, Width = 232 };
            _random = new Button { Text = "Randomize", Left = 328, Top = 78, Width = 108 };
            _random.Click += (s, e) => _newMac.Text = RandomMac();

            _apply = new Button { Text = "Apply", Left = 88, Top = 120, Width = 100 };
            _apply.Click += (s, e) => Apply();
            _restore = new Button { Text = "Restore original", Left = 196, Top = 120, Width = 132 };
            _restore.Click += (s, e) => Restore();
            _refresh = new Button { Text = "Refresh", Left = 336, Top = 120, Width = 100 };
            _refresh.Click += (s, e) => LoadAdapters();

            _status = new Label { Left = 12, Top = 162, Width = 428, Height = 58 };

            Controls.AddRange(new Control[]
                { lblA, _adapters, _current, lblN, _newMac, _random, _apply, _restore, _refresh, _status });

            LoadAdapters();
        }

        private void SetStatus(string msg, bool error = false)
        {
            _status.ForeColor = error ? Color.Firebrick : Color.DarkGreen;
            _status.Text = msg;
        }

        private Adapter Selected => _adapters.SelectedItem as Adapter;

        private void LoadAdapters()
        {
            try
            {
                _adapters.Items.Clear();
                var list = EnumeratePhysicalAdapters();
                foreach (var a in list) _adapters.Items.Add(a);
                if (_adapters.Items.Count > 0) _adapters.SelectedIndex = 0;
                else _current.Text = "Current MAC: -";
                SetStatus(list.Count + " physical adapter(s) found.");
            }
            catch (Exception ex)
            {
                SetStatus("Failed to list adapters: " + ex.Message, true);
            }
        }

        private void ShowCurrent()
        {
            var a = Selected;
            _current.Text = a == null ? "Current MAC: -" : "Current MAC: " + (a.CurrentMac ?? "unknown");
        }

        /// <summary>
        /// Physical adapters known to WMI, matched to their registry key via NetCfgInstanceId.
        /// </summary>
        private List<Adapter> EnumeratePhysicalAdapters()
        {
            var wmi = new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase);
            using (var searcher = new ManagementObjectSearcher(
                "SELECT GUID, NetConnectionID, MACAddress FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    var guid = mo["GUID"] as string;
                    if (string.IsNullOrEmpty(guid)) continue;
                    wmi[guid] = Tuple.Create(mo["NetConnectionID"] as string, mo["MACAddress"] as string);
                }
            }

            var result = new List<Adapter>();
            using (var cls = Registry.LocalMachine.OpenSubKey(ClassKey))
            {
                if (cls == null) return result;
                foreach (var sub in cls.GetSubKeyNames())
                {
                    if (!int.TryParse(sub, out _)) continue; // only the numeric NNNN subkeys
                    using (var k = cls.OpenSubKey(sub))
                    {
                        var guid = k?.GetValue("NetCfgInstanceId") as string;
                        if (string.IsNullOrEmpty(guid) || !wmi.ContainsKey(guid)) continue;
                        var info = wmi[guid];
                        result.Add(new Adapter
                        {
                            Description = k.GetValue("DriverDesc") as string ?? "(unknown)",
                            Guid = guid,
                            ConnectionName = info.Item1,
                            CurrentMac = FormatMac(info.Item2),
                            RegistryKeyPath = ClassKey + "\\" + sub
                        });
                    }
                }
            }
            return result;
        }

        private void Apply()
        {
            var a = Selected;
            if (a == null) { SetStatus("Select an adapter first.", true); return; }
            var mac = NormalizeMac(_newMac.Text);
            if (mac == null)
            {
                SetStatus("Enter a valid MAC: 12 hex digits, e.g. 02:1A:2B:3C:4D:5E.", true);
                return;
            }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(a.RegistryKeyPath, writable: true))
                {
                    if (k == null) { SetStatus("Cannot open adapter registry key — run as Administrator.", true); return; }
                    k.SetValue("NetworkAddress", mac, RegistryValueKind.String);
                }
                if (RestartAdapter(a, out var err))
                    SetStatus("MAC set to " + FormatMac(mac) + ". Adapter restarted.");
                else
                    SetStatus("Registry set to " + FormatMac(mac) + ", but adapter restart failed: " + err +
                              "\nDisable then re-enable the adapter manually to apply.", true);
                LoadAdapters();
            }
            catch (Exception ex)
            {
                SetStatus("Apply failed: " + ex.Message + " (run as Administrator).", true);
            }
        }

        private void Restore()
        {
            var a = Selected;
            if (a == null) { SetStatus("Select an adapter first.", true); return; }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(a.RegistryKeyPath, writable: true))
                {
                    if (k == null) { SetStatus("Cannot open adapter registry key — run as Administrator.", true); return; }
                    if (k.GetValue("NetworkAddress") != null) k.DeleteValue("NetworkAddress", throwOnMissingValue: false);
                }
                if (RestartAdapter(a, out var err))
                    SetStatus("Original hardware MAC restored. Adapter restarted.");
                else
                    SetStatus("NetworkAddress cleared, but adapter restart failed: " + err +
                              "\nDisable then re-enable the adapter manually.", true);
                LoadAdapters();
            }
            catch (Exception ex)
            {
                SetStatus("Restore failed: " + ex.Message + " (run as Administrator).", true);
            }
        }

        /// <summary>Disable then re-enable the adapter so the new MAC takes effect.</summary>
        private bool RestartAdapter(Adapter a, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(a.ConnectionName)) { error = "no connection name"; return false; }
            try
            {
                if (!Netsh("interface set interface name=\"" + a.ConnectionName + "\" admin=disabled", out error)) return false;
                Thread.Sleep(1500);
                if (!Netsh("interface set interface name=\"" + a.ConnectionName + "\" admin=enabled", out error)) return false;
                Thread.Sleep(1500);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static bool Netsh(string args, out string error)
        {
            error = null;
            var psi = new ProcessStartInfo("netsh", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) { error = (stderr + stdout).Trim(); return false; }
            }
            return true;
        }

        /// <summary>Random locally-administered, unicast MAC (bit 0x02 set, 0x01 cleared).</summary>
        private static string RandomMac()
        {
            var b = new byte[6];
            new Random().NextBytes(b);
            b[0] = (byte)((b[0] & 0xFC) | 0x02);
            return string.Join(":", b.Select(x => x.ToString("X2")));
        }

        /// <summary>Returns 12 uppercase hex chars (no separators), or null if invalid.</summary>
        private static string NormalizeMac(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var sb = new StringBuilder();
            foreach (var c in input)
            {
                if (Uri.IsHexDigit(c)) sb.Append(char.ToUpperInvariant(c));
                else if (c == ':' || c == '-' || c == ' ' || c == '.') continue;
                else return null;
            }
            var s = sb.ToString();
            return s.Length == 12 ? s : null;
        }

        private static string FormatMac(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var s = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            if (s.Length != 12) return raw;
            var parts = new List<string>();
            for (var i = 0; i < 12; i += 2) parts.Add(s.Substring(i, 2));
            return string.Join(":", parts);
        }
    }
}
