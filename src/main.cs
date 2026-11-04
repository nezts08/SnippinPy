using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnippinCS
{
    public class Config
    {
        [JsonPropertyName("trigger_key")]
        public ushort TriggerKey { get; set; } = 9;

        [JsonPropertyName("trigger_key_name")]
        public string TriggerKeyName { get; set; } = "TAB";

        [JsonPropertyName("trigger_modifiers")]
        public int TriggerModifiers { get; set; } = 0;

        [JsonPropertyName("snippet_dates")]
        public Dictionary<string, SnippetDates> SnippetDates { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("commands")]
        public Dictionary<string, string> Commands { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public class SnippetDates
    {
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("modified_at")]
        public DateTime ModifiedAt { get; set; }
    }

    public static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private const string ConfigFile = "command.json";
        private static Config config;
        private static string buffer = "";

        public static Dictionary<string, ushort> KeyMap = new Dictionary<string, ushort>
        {
            { "TAB", 9 },
            { "F1", 112 },
            { "F2", 113 },
            { "F3", 114 },
            { "F4", 115 },
            { "F5", 116 },
            { "F6", 117 },
            { "F7", 118 },
            { "F8", 119 },
            { "F9", 120 },
            { "F10", 121 },
        };

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        private ListBox listCommands;
        private TextBox txtKey;
        private TextBox txtValue;
        private Label rightTitle;
        private Button btnSave;
        private Button btnDelete;
        private TextBox txtSearch;
        private ComboBox comboSort;
        private bool refreshingList;
        private string editingKey;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int LLKHF_INJECTED = 0x10;
        private const int MOD_CTRL = 1;
        private const int MOD_ALT = 2;
        private const int MOD_SHIFT = 4;
        private const int MOD_WIN = 8;

        public MainForm()
        {
            LoadConfig();
            InitializeUI();

            _hookID = SetHook(_proc);
            this.FormClosing += (s, e) => UnhookWindowsHookEx(_hookID);
        }

        private void InitializeUI()
        {
            Color bgDark = Color.FromArgb(30, 30, 30);
            Color panelDark = Color.FromArgb(37, 37, 38);
            Color inputDark = Color.FromArgb(45, 45, 48);
            Color textLight = Color.FromArgb(241, 241, 241);
            Color accentBlue = Color.FromArgb(0, 122, 204);
            Color dangerRed = Color.FromArgb(197, 34, 31);

            this.Text = "SnippinC#";
            this.Size = new Size(1200, 700);
            this.MinimumSize = new Size(900, 550);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = bgDark;
            this.ForeColor = textLight;
            this.Font = new Font("Segoe UI", 10F);

            Panel leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 295,
                BackColor = bgDark,
            };

            Panel leftHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 45,
                Padding = new Padding(10, 8, 10, 8),
                BackColor = bgDark,
            };

            Label lblTitle = new Label
            {
                Text = "COMANDOS",
                Font = new Font(this.Font, FontStyle.Bold),
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
            };

            Button btnSettings = new Button
            {
                Text = "⚙",
                Dock = DockStyle.Right,
                Width = 35,
                FlatStyle = FlatStyle.Flat,
                ForeColor = textLight,
                BackColor = inputDark,
                Cursor = Cursors.Hand,
            };

            btnSettings.FlatAppearance.BorderSize = 0;

            Button btnAdd = new Button
            {
                Text = "+",
                Dock = DockStyle.Right,
                Width = 35,
                FlatStyle = FlatStyle.Flat,
                ForeColor = textLight,
                BackColor = inputDark,
                Cursor = Cursors.Hand,
            };

            btnAdd.FlatAppearance.BorderSize = 0;

            leftHeader.Controls.Add(lblTitle);
            leftHeader.Controls.Add(btnAdd);
            leftHeader.Controls.Add(btnSettings);

            listCommands = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = bgDark,
                ForeColor = textLight,
                BorderStyle = BorderStyle.None,
                ItemHeight = 25,
                Font = new Font("Consolas", 11F),
                IntegralHeight = false,
            };

            Panel filterPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 94,
                Padding = new Padding(10, 5, 10, 5),
                BackColor = bgDark,
            };
            txtSearch = new TextBox
            {
                Dock = DockStyle.Top,
                PlaceholderText = "Buscar snippets...",
                BackColor = inputDark,
                ForeColor = textLight,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10F),
            };
            comboSort = new ComboBox
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = inputDark,
                ForeColor = textLight,
                FlatStyle = FlatStyle.Flat,
            };
            comboSort.Items.AddRange(
                new object[]
                {
                    "Nombre (A-Z)",
                    "Nombre (Z-A)",
                    "Recien creado",
                    "Mas antiguo",
                    "Recien modificado",
                }
            );
            comboSort.SelectedIndex = 0;
            filterPanel.Controls.Add(txtSearch);
            filterPanel.Controls.Add(comboSort);
            txtSearch.TextChanged += (s, e) => UpdateCommandList();
            comboSort.SelectedIndexChanged += (s, e) => UpdateCommandList();

            leftPanel.Controls.Add(listCommands);
            leftPanel.Controls.Add(filterPanel);
            leftPanel.Controls.Add(leftHeader);
            UpdateCommandList();

            Panel rightPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = bgDark,
                Padding = new Padding(25),
            };

            rightTitle = new Label
            {
                Text = "Nuevo Snippet",
                Dock = DockStyle.Top,
                Height = 45,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            Label lblCommand = new Label
            {
                Text = "Comando:",
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = Color.Gray,
            };

            txtKey = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = inputDark,
                ForeColor = textLight,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 11F),
                PlaceholderText = "Ej: html5",
            };

            Panel spacer1 = new Panel
            {
                Dock = DockStyle.Top,
                Height = 20,
                BackColor = bgDark,
            };

            Label lblExpanded = new Label
            {
                Text = "Texto expandido:",
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = Color.Gray,
            };

            Panel textContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 5, 0, 20),
                BackColor = bgDark,
            };

            txtValue = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = inputDark,
                ForeColor = textLight,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 11F),
            };

            textContainer.Controls.Add(txtValue);

            Panel buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = bgDark,
            };

            btnSave = new Button
            {
                Text = "Guardar",
                Dock = DockStyle.Left,
                Width = 120,
                FlatStyle = FlatStyle.Flat,
                BackColor = accentBlue,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font(this.Font, FontStyle.Bold),
            };

            btnSave.FlatAppearance.BorderSize = 0;

            btnDelete = new Button
            {
                Text = "Eliminar",
                Dock = DockStyle.Left,
                Width = 120,
                FlatStyle = FlatStyle.Flat,
                BackColor = dangerRed,
                ForeColor = Color.White,
                Visible = false,
                Cursor = Cursors.Hand,
                Font = new Font(this.Font, FontStyle.Bold),
                Margin = new Padding(10, 0, 0, 0),
            };

            btnDelete.FlatAppearance.BorderSize = 0;

            buttonPanel.Controls.Add(btnDelete);
            buttonPanel.Controls.Add(btnSave);

            rightPanel.Controls.Add(textContainer);
            rightPanel.Controls.Add(lblExpanded);
            rightPanel.Controls.Add(spacer1);
            rightPanel.Controls.Add(txtKey);
            rightPanel.Controls.Add(lblCommand);
            rightPanel.Controls.Add(rightTitle);
            rightPanel.Controls.Add(buttonPanel);

            Label footerNote = new Label
            {
                Text =
                    "*NOTA: recuerda que al momento de un espaciado o enter se borra la captura del texto anterior.",
                Dock = DockStyle.Bottom,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(this.Font, FontStyle.Italic),
                Height = 35,
                ForeColor = Color.DarkGray,
                BackColor = panelDark,
            };

            this.Controls.Add(rightPanel);
            this.Controls.Add(leftPanel);
            this.Controls.Add(footerNote);

            listCommands.SelectedIndexChanged += (s, e) =>
            {
                if (refreshingList || listCommands.SelectedItem == null)
                    return;

                string key = listCommands.SelectedItem.ToString()!;
                editingKey = key;

                txtKey.Text = key;
                txtKey.Enabled = false;
                txtKey.BackColor = panelDark;

                txtValue.Text = config.Commands[key];

                btnDelete.Visible = true;
                rightTitle.Text = "Editando Snippet";
            };

            btnAdd.Click += (s, e) =>
            {
                listCommands.ClearSelected();
                editingKey = null;

                txtKey.Enabled = true;
                txtKey.BackColor = inputDark;

                txtKey.Text = "";
                txtValue.Text = "";

                btnDelete.Visible = false;
                rightTitle.Text = "Nuevo Snippet";
            };

            btnSave.Click += (s, e) =>
            {
                string key = txtKey.Text.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(txtValue.Text))
                {
                    MessageBox.Show("Completa el comando y el texto expandido.", "SnippinC#");
                    return;
                }
                if (key.Any(c => !char.IsLetterOrDigit(c)))
                {
                    MessageBox.Show(
                        "El comando solo puede contener letras y numeros.",
                        "SnippinC#"
                    );
                    return;
                }

                config.Commands[key] = txtValue.Text;
                DateTime now = DateTime.UtcNow;
                if (!config.SnippetDates.TryGetValue(key, out SnippetDates dates))
                    dates = new SnippetDates { CreatedAt = now };
                if (dates.CreatedAt == default)
                    dates.CreatedAt = now;
                dates.ModifiedAt = now;
                config.SnippetDates[key] = dates;
                SaveConfig();
                UpdateCommandList();
                btnAdd.PerformClick();
            };

            btnDelete.Click += (s, e) =>
            {
                string key = editingKey ?? txtKey.Text.Trim().ToLowerInvariant();
                if (config.Commands.Remove(key))
                {
                    config.SnippetDates.Remove(key);
                    SaveConfig();
                    btnAdd.PerformClick();
                    UpdateCommandList();
                }
            };

            btnSettings.Click += (s, e) => ShowSettingsModal();
        }

        private void ShowSettingsModal()
        {
            Color bgDark = Color.FromArgb(30, 30, 30);
            Color inputDark = Color.FromArgb(45, 45, 48);
            Color textLight = Color.FromArgb(241, 241, 241);
            Color accentBlue = Color.FromArgb(0, 122, 204);

            using Form modal = new Form
            {
                Text = "Configuracion de activacion",
                Size = new Size(410, 265),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = bgDark,
                ForeColor = textLight,
                KeyPreview = true,
            };
            Label info = new Label
            {
                Text = "Pulsa 'Grabar' y presiona la combinacion deseada:",
                Location = new Point(20, 20),
                Size = new Size(360, 26),
            };
            TextBox display = new TextBox
            {
                Location = new Point(20, 54),
                Width = 350,
                ReadOnly = true,
                BackColor = inputDark,
                ForeColor = textLight,
                Text = FormatShortcut(config.TriggerKey, config.TriggerModifiers),
            };
            Button record = new Button
            {
                Text = "Grabar combinacion",
                Location = new Point(20, 92),
                Size = new Size(350, 35),
                BackColor = inputDark,
                ForeColor = textLight,
                FlatStyle = FlatStyle.Flat,
            };
            Button save = new Button
            {
                Text = "Guardar cambios",
                Location = new Point(20, 150),
                Size = new Size(350, 38),
                BackColor = accentBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            Keys recordedKey = (Keys)config.TriggerKey;
            int recordedMods = config.TriggerModifiers;
            bool isRecording = false;

            record.Click += (s, e) =>
            {
                isRecording = true;
                display.Text = "Presiona tu combinacion...";
                record.Text = "Grabando...";
                modal.ActiveControl = record;
            };
            KeyEventHandler captureShortcut = (s, e) =>
            {
                if (!isRecording)
                    return;
                e.Handled = true;
                e.SuppressKeyPress = true;
                Keys key = e.KeyCode;
                if (
                    key == Keys.ControlKey
                    || key == Keys.ShiftKey
                    || key == Keys.Menu
                    || key == Keys.LWin
                    || key == Keys.RWin
                )
                    return;
                int mods =
                    (e.Control ? MOD_CTRL : 0)
                    | (e.Alt ? MOD_ALT : 0)
                    | (e.Shift ? MOD_SHIFT : 0)
                    | ((GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) ? MOD_WIN : 0);
                if (key == Keys.Escape)
                {
                    isRecording = false;
                    record.Text = "Grabar combinacion";
                    display.Text = FormatShortcut(config.TriggerKey, config.TriggerModifiers);
                    return;
                }
                if (mods == 0 && key != Keys.Tab && (key < Keys.F1 || key > Keys.F24))
                {
                    display.Text = "Usa Ctrl/Alt/Shift/Win, o TAB/F1-F24";
                    return;
                }
                recordedKey = key;
                recordedMods = mods;
                display.Text = FormatShortcut((ushort)key, mods);
                isRecording = false;
                record.Text = "Grabar combinacion";
            };
            modal.KeyDown += captureShortcut;
            record.KeyDown += captureShortcut;
            record.PreviewKeyDown += (s, e) =>
            {
                if (isRecording)
                    e.IsInputKey = true;
            };
            save.Click += (s, e) =>
            {
                if (isRecording)
                    return;
                config.TriggerKey = (ushort)recordedKey;
                config.TriggerModifiers = recordedMods;
                config.TriggerKeyName = FormatShortcut(config.TriggerKey, config.TriggerModifiers);
                SaveConfig();
                buffer = "";
                modal.Close();
            };
            modal.Controls.AddRange(new Control[] { info, display, record, save });
            modal.ShowDialog(this);
        }

        private static string FormatShortcut(ushort key, int mods)
        {
            var pieces = new List<string>();
            if ((mods & MOD_CTRL) != 0)
                pieces.Add("Ctrl");
            if ((mods & MOD_ALT) != 0)
                pieces.Add("Alt");
            if ((mods & MOD_SHIFT) != 0)
                pieces.Add("Shift");
            if ((mods & MOD_WIN) != 0)
                pieces.Add("Win");
            pieces.Add(((Keys)key).ToString());
            return string.Join(" + ", pieces);
        }

        private void UpdateCommandList()
        {
            if (listCommands == null || config == null)
                return;
            string selected = listCommands.SelectedItem as string;
            string query = txtSearch?.Text?.Trim() ?? "";
            IEnumerable<string> keys = config.Commands.Keys.Where(k =>
                k.Contains(query, StringComparison.OrdinalIgnoreCase)
                || config.Commands[k].Contains(query, StringComparison.OrdinalIgnoreCase)
            );
            Func<string, DateTime> created = k =>
                config.SnippetDates.TryGetValue(k, out var d) ? d.CreatedAt : DateTime.MinValue;
            Func<string, DateTime> modified = k =>
                config.SnippetDates.TryGetValue(k, out var d) ? d.ModifiedAt : DateTime.MinValue;
            keys = (comboSort?.SelectedIndex ?? 0) switch
            {
                1 => keys.OrderByDescending(k => k, StringComparer.OrdinalIgnoreCase),
                2 => keys.OrderByDescending(created)
                    .ThenBy(k => k, StringComparer.OrdinalIgnoreCase),
                3 => keys.OrderBy(created).ThenBy(k => k, StringComparer.OrdinalIgnoreCase),
                4 => keys.OrderByDescending(modified)
                    .ThenBy(k => k, StringComparer.OrdinalIgnoreCase),
                _ => keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
            };
            refreshingList = true;
            listCommands.BeginUpdate();
            try
            {
                listCommands.Items.Clear();
                listCommands.Items.AddRange(keys.Cast<object>().ToArray());
                if (selected != null && listCommands.Items.Contains(selected))
                    listCommands.SelectedItem = selected;
            }
            finally
            {
                listCommands.EndUpdate();
                refreshingList = false;
            }
        }

        private static void LoadConfig()
        {
            if (File.Exists(ConfigFile))
            {
                try
                {
                    config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigFile));

                    if (config == null)
                    {
                        CreateDefaultConfig();
                        return;
                    }

                    config.SnippetDates ??= new Dictionary<string, SnippetDates>(
                        StringComparer.OrdinalIgnoreCase
                    );
                    config.SnippetDates = new Dictionary<string, SnippetDates>(
                        config.SnippetDates,
                        StringComparer.OrdinalIgnoreCase
                    );
                    if (config.Commands == null)
                    {
                        config.Commands = new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase
                        );
                    }
                    config.Commands = new Dictionary<string, string>(
                        config.Commands,
                        StringComparer.OrdinalIgnoreCase
                    );
                    foreach (var key in config.Commands.Keys)
                    {
                        if (!config.SnippetDates.ContainsKey(key))
                            config.SnippetDates[key] = new SnippetDates
                            {
                                CreatedAt = DateTime.MinValue,
                                ModifiedAt = DateTime.MinValue,
                            };
                    }
                }
                catch
                {
                    CreateDefaultConfig();
                }
            }
            else
            {
                CreateDefaultConfig();
            }
        }

        private static void CreateDefaultConfig()
        {
            config = new Config
            {
                Commands = new Dictionary<string, string> { { "ejemplo", "Texto de prueba" } },
            };
            SaveConfig();
        }

        private static void SaveConfig()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config, opts));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar configuración: {ex.Message}", "SnippinC#");
            }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        private static int CurrentModifiers()
        {
            int mods = 0;
            if (GetAsyncKeyState(0x11) < 0)
                mods |= MOD_CTRL;
            if (GetAsyncKeyState(0x12) < 0)
                mods |= MOD_ALT;
            if (GetAsyncKeyState(0x10) < 0)
                mods |= MOD_SHIFT;
            if (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0)
                mods |= MOD_WIN;
            return mods;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int flags = Marshal.ReadInt32(lParam, 8);
                if ((flags & LLKHF_INJECTED) != 0)
                    return CallNextHookEx(_hookID, nCode, wParam, lParam);

                if (IsOwnWindowActive())
                {
                    buffer = "";
                    return CallNextHookEx(_hookID, nCode, wParam, lParam);
                }

                int mods = CurrentModifiers();
                if (vkCode == config.TriggerKey && mods == config.TriggerModifiers)
                {
                    string word = buffer.ToLowerInvariant();
                    buffer = "";
                    if (config.Commands.TryGetValue(word, out string expanded))
                    {
                        Task.Run(() => ExecuteCommand(word, expanded));
                        return (IntPtr)1;
                    }
                }
                else if (
                    vkCode == 0x10
                    || vkCode == 0x11
                    || vkCode == 0x12
                    || vkCode == 0x5B
                    || vkCode == 0x5C
                    || vkCode == 0xA0
                    || vkCode == 0xA1
                    || vkCode == 0xA2
                    || vkCode == 0xA3
                    || vkCode == 0xA4
                    || vkCode == 0xA5
                ) { }
                else if (vkCode == 8)
                {
                    if (buffer.Length > 0)
                        buffer = buffer[..^1];
                }
                else if (vkCode == 32 || vkCode == 13 || vkCode == 9)
                {
                    buffer = "";
                }
                else if (
                    (vkCode >= 65 && vkCode <= 90)
                    || (vkCode >= 48 && vkCode <= 57)
                    || (vkCode >= 96 && vkCode <= 105)
                )
                {
                    buffer +=
                        vkCode >= 96 && vkCode <= 105
                            ? (vkCode - 96).ToString()
                            : ((char)vkCode).ToString().ToLowerInvariant();
                    if (buffer.Length > 100)
                        buffer = buffer.Substring(buffer.Length - 100);
                }
                else
                    buffer = "";
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private static bool IsOwnWindowActive()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == (uint)Environment.ProcessId;
        }

        private static void ExecuteCommand(string palabraEnBuffer, string textoLargo)
        {
            Thread.Sleep(100);
            int totalABorrar = palabraEnBuffer.Length;
            for (int i = 0; i < totalABorrar; i++)
            {
                keybd_event(VK_BACK, 0, 0, UIntPtr.Zero);
                Thread.Sleep(5);
                keybd_event(VK_BACK, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(5);
            }

            Thread staThread = new(() =>
            {
                try
                {
                    Clipboard.SetText(textoLargo);
                }
                catch { }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();

            Thread.Sleep(50);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId
        );

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam
        );

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const byte VK_BACK = 0x08;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    proc,
                    GetModuleHandle(curModule.ModuleName),
                    0
                );
            }
        }
    }
}
