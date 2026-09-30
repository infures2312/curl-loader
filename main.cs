using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Downloader;

public enum NetworkMode
{
    System,
    DoH
}

public sealed class NetworkProvider
{
    private static readonly Dictionary<string, string> DohPresets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cloudflare"] = "https://cloudflare-dns.com/dns-query",
            ["1.1.1.1"] = "https://cloudflare-dns.com/dns-query",
            ["1.0.0.1"] = "https://cloudflare-dns.com/dns-query",

            ["google"] = "https://dns.google/dns-query",
            ["8.8.8.8"] = "https://dns.google/dns-query",
            ["8.8.4.4"] = "https://dns.google/dns-query",

            ["quad9"] = "https://dns.quad9.net/dns-query",
            ["9.9.9.9"] = "https://dns.quad9.net/dns-query"
        };

    public NetworkMode Mode { get; private set; } = NetworkMode.System;

    public string DohUrl { get; private set; } = "";

    public string DisplayName
    {
        get
        {
            return Mode switch
            {
                NetworkMode.System => "System DNS",
                NetworkMode.DoH => $"DoH: {DohUrl}",
                _ => "Unknown"
            };
        }
    }

    public void SetSystem()
    {
        Mode = NetworkMode.System;
        DohUrl = "";
    }

    public void SetDoH(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("DNS / DoH value is empty.");

        value = value.Trim();

        if (DohPresets.TryGetValue(value, out string? preset))
        {
            Mode = NetworkMode.DoH;
            DohUrl = preset;
            return;
        }

        if (IPAddress.TryParse(value, out _))
        {
            throw new ArgumentException(
                "Этот DNS указан как IP, но для DoH нужен HTTPS endpoint. " +
                "Используй 1.1.1.1 / 8.8.8.8 / 9.9.9.9 или полный HTTPS DoH URL."
            );
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException("Некорректный DNS / DoH адрес.");
        }

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("DoH endpoint должен использовать HTTPS.");
        }

        Mode = NetworkMode.DoH;
        DohUrl = uri.ToString();
    }
}

public static class CommandParser
{
    public static List<string> Parse(string commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();

        bool inDoubleQuotes = false;
        bool inSingleQuotes = false;

        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];

            if (c == '\\' &&
                inDoubleQuotes &&
                i + 1 < commandLine.Length &&
                commandLine[i + 1] == '"')
            {
                current.Append('"');
                i++;
                continue;
            }

            if (c == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (c == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) &&
                !inDoubleQuotes &&
                !inSingleQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}

public sealed class MainForm : Form
{
    // ==========================================
    // COLORS
    // ==========================================

    private static readonly Color WindowColor =
        Color.FromArgb(18, 18, 20);

    private static readonly Color InputColor =
        Color.FromArgb(32, 32, 36);

    private static readonly Color TerminalColor =
        Color.FromArgb(12, 13, 15);

    private static readonly Color BorderColor =
        Color.FromArgb(55, 55, 61);

    private static readonly Color TextColor =
        Color.FromArgb(235, 235, 238);

    private static readonly Color MutedTextColor =
        Color.FromArgb(155, 155, 165);

    private static readonly Color SuccessColor =
        Color.FromArgb(105, 205, 135);

    private static readonly Color ErrorColor =
        Color.FromArgb(235, 105, 105);

    // ==========================================
    // COMPONENTS
    // ==========================================

    private readonly NetworkProvider networkProvider = new();

    private TextBox txtDns = null!;
    private TextBox txtCommand = null!;

    private RichTextBox txtTerminal = null!;

    private Button btnSetDns = null!;
    private Button btnRun = null!;
    private Button btnClear = null!;
    private Button btnHelp = null!;
    private Button btnNetwork = null!;
    private Button btnFolder = null!;
    private Button btnExport = null!;
    private Label lblStatus = null!;
    private Label lblNetwork = null!;
    private Label lblFolder = null!;
    private ToolTip folderToolTip = null!;
    private Label lblCommand = null!;

    // ==========================================
    // DOWNLOAD FOLDER
    // ==========================================

    private string downloadDirectory = "";

    public MainForm()
    {
        InitializeInterface();
        InitializeState();
    }

    // ==========================================
    // UI
    // ==========================================

    private void InitializeInterface()
    {
        Text = "Downloader";

        StartPosition = FormStartPosition.CenterScreen;

        Width = 1100;
        Height = 730;

        MinimumSize = new Size(850, 600);

        BackColor = WindowColor;
        ForeColor = TextColor;

        Font = new Font(
            "Segoe UI",
            10f,
            FontStyle.Regular
        );

        FormBorderStyle = FormBorderStyle.Sizable;

        // ==========================================
        // DNS
        // ==========================================

        var lblDns = new Label
        {
            Text = "DNS / DoH",
            AutoSize = true,
            Left = 24,
            Top = 21,
            ForeColor = TextColor
        };

        txtDns = CreateTextBox();

        txtDns.Left = 120;
        txtDns.Top = 17;
        txtDns.Width = 300;
        txtDns.Text = "1.1.1.1";

        txtDns.KeyDown += TxtDns_KeyDown;

        btnSetDns = CreateButton(
            "SET DNS",
            430,
            15,
            105,
            32
        );

        btnSetDns.Click += BtnSetDns_Click;

        // ==========================================
        // NETWORK
        // ==========================================

        btnNetwork = CreateButton(
            "SYSTEM",
            545,
            15,
            105,
            32
        );

        btnNetwork.Click += BtnNetwork_Click;

        lblNetwork = new Label
        {
            Left = 660,
            Top = 21,
            Width = 120,
            Height = 20,

            AutoSize = false,
            AutoEllipsis = true,

            ForeColor = MutedTextColor,

            Text = "System DNS",

            TextAlign = ContentAlignment.MiddleLeft,

            Anchor =
        AnchorStyles.Top |
        AnchorStyles.Left
        };

        // ==========================================
        // FOLDER BUTTON
        // ==========================================

        btnFolder = CreateButton(
    "FOLDER",
    790,
    15,
    100,
    32
);

        btnFolder.Click += BtnFolder_Click;

        lblFolder = new Label
        {
            Left = btnFolder.Right + 10,
            Top = 21,

            Height = 20,

            ForeColor = MutedTextColor,

            Text = "Folder: \"...\"",

            AutoSize = false,
            AutoEllipsis = true,

            TextAlign = ContentAlignment.MiddleLeft,

            Anchor =
        AnchorStyles.Top |
        AnchorStyles.Left |
        AnchorStyles.Right
        };

        // ==========================================
        // COMMAND LABEL
        // ==========================================

        lblFolder = new Label
        {
            Left = btnFolder.Right + 10,
            Top = 21,

            Height = 20,

            ForeColor = MutedTextColor,

            Text = "Folder: \"...\"",

            AutoSize = false,
            AutoEllipsis = true,

            TextAlign = ContentAlignment.MiddleLeft,

            Anchor =
        AnchorStyles.Top |
        AnchorStyles.Left |
        AnchorStyles.Right
        };

        folderToolTip = new ToolTip();

        folderToolTip.SetToolTip(
            lblFolder,
            ""
        );

        btnExport = CreateButton(
    "EXPORT",
    ClientSize.Width - 110,
    15,
    100,
    32
);

        btnExport.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Right;

        btnExport.Click += BtnExport_Click;

        // ==========================================
        // COMMAND
        // ==========================================

        txtCommand = CreateTextBox();

        txtCommand.Left = 120;
        txtCommand.Top = 62;
        txtCommand.Width = 820;

        txtCommand.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right;

        txtCommand.KeyDown += TxtCommand_KeyDown;

        // ==========================================
        // RUN
        // ==========================================

        btnRun = CreateButton(
            "RUN",
            950,
            60,
            105,
            32
        );

        btnRun.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Right;

        btnRun.Click += BtnRun_Click;

        // ==========================================
        // TERMINAL
        // ==========================================

        txtTerminal = new RichTextBox
        {
            Left = 24,
            Top = 110,
            Width = 1031,
            Height = 535,

            Anchor =
                AnchorStyles.Top |
                AnchorStyles.Bottom |
                AnchorStyles.Left |
                AnchorStyles.Right,

            BackColor = TerminalColor,
            ForeColor = Color.FromArgb(220, 220, 225),

            BorderStyle = BorderStyle.FixedSingle,

            Font = new Font(
                "Cascadia Mono",
                10f,
                FontStyle.Regular
            ),

            DetectUrls = false,
            ReadOnly = true,
            WordWrap = false,

            ScrollBars =
                RichTextBoxScrollBars.Both,

            HideSelection = false
        };

        // ==========================================
        // CLEAR
        // ==========================================

        btnClear = CreateButton(
            "CLEAR",
            24,
            660,
            100,
            32
        );

        btnClear.Anchor =
            AnchorStyles.Bottom |
            AnchorStyles.Left;

        btnClear.Click += BtnClear_Click;

        // ==========================================
        // HELP
        // ==========================================

        btnHelp = CreateButton(
            "HELP",
            134,
            660,
            100,
            32
        );

        btnHelp.Anchor =
            AnchorStyles.Bottom |
            AnchorStyles.Left;

        btnHelp.Click += BtnHelp_Click;

        // ==========================================
        // STATUS
        // ==========================================

        lblStatus = new Label
        {
            AutoSize = true,
            Left = 255,
            Top = 668,

            Anchor =
                AnchorStyles.Bottom |
                AnchorStyles.Left,

            ForeColor = MutedTextColor,
            Text = "READY"
        };

        // ==========================================
        // ADD CONTROLS
        // ==========================================

        Controls.Add(lblDns);
        Controls.Add(txtDns);
        Controls.Add(btnSetDns);

        Controls.Add(btnNetwork);
        Controls.Add(lblNetwork);

        Controls.Add(btnFolder);
        Controls.Add(lblFolder);

        Controls.Add(btnExport);

        Controls.Add(lblCommand);
        Controls.Add(txtCommand);
        Controls.Add(btnRun);

        Controls.Add(txtTerminal);

        Controls.Add(btnClear);
        Controls.Add(btnHelp);
        Controls.Add(lblStatus);

        UpdateNetworkLayout();

        Resize += (_, _) =>
        {
            UpdateNetworkLayout();
            UpdateFolderLayout();
        };
    }

    private void UpdateNetworkLayout()
    {
        int left = btnNetwork.Right + 10;
        int right = btnFolder.Left - 10;

        lblNetwork.Left = left;

        lblNetwork.Width =
            Math.Max(
                40,
                right - left
            );
    }
    private TextBox CreateTextBox()
    {
        return new TextBox
        {
            BackColor = InputColor,
            ForeColor = TextColor,

            BorderStyle = BorderStyle.FixedSingle,

            Font = new Font(
                "Cascadia Mono",
                10f
            )
        };
    }

    private Button CreateButton(
        string text,
        int left,
        int top,
        int width,
        int height)
    {
        var button = new Button
        {
            Text = text,

            Left = left,
            Top = top,

            Width = width,
            Height = height,

            BackColor = InputColor,
            ForeColor = TextColor,

            FlatStyle = FlatStyle.Flat,

            Font = new Font(
                "Segoe UI Semibold",
                9f
            ),

            UseVisualStyleBackColor = false
        };

        button.FlatAppearance.BorderColor = BorderColor;
        button.FlatAppearance.BorderSize = 1;

        button.MouseEnter += (_, _) =>
        {
            button.BackColor =
                Color.FromArgb(45, 45, 50);
        };

        button.MouseLeave += (_, _) =>
        {
            button.BackColor = InputColor;
        };

        return button;
    }

    private void InitializeState()
    {
        // По умолчанию — стандартная папка Downloads.
        downloadDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile
                ),
                "Downloads"
            );

        // Если Downloads вдруг не существует,
        // используем профиль пользователя.
        if (!Directory.Exists(downloadDirectory))
        {
            downloadDirectory =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile
                );
        }

        UpdateFolderUi();

        WriteLine(
            "Downloader started.",
            ConsoleLineType.Normal
        );

        WriteLine(
            "Type curl command and press RUN.",
            ConsoleLineType.Normal
        );

        WriteLine(
            $"Download folder: {downloadDirectory}",
            ConsoleLineType.Network
        );

        WriteLine(
            "",
            ConsoleLineType.Normal
        );

        UpdateNetworkUi();
    }

    // ==========================================
    // FOLDER
    // ==========================================

    private void BtnFolder_Click(
        object? sender,
        EventArgs e)
    {
        using var dialog =
            new FolderBrowserDialog
            {
                Description =
                    "Выберите папку для скачивания файлов",

                UseDescriptionForTitle = true,

                ShowNewFolderButton = true,

                SelectedPath = Directory.Exists(
                    downloadDirectory
                )
                    ? downloadDirectory
                    : Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile
                    )
            };

        DialogResult result =
            dialog.ShowDialog(this);

        if (result != DialogResult.OK)
            return;

        if (string.IsNullOrWhiteSpace(
                dialog.SelectedPath))
        {
            return;
        }

        downloadDirectory =
            dialog.SelectedPath;

        UpdateFolderUi();

        WriteLine(
            $"[FOLDER] Download directory changed:",
            ConsoleLineType.Network
        );

        WriteLine(
            $"[FOLDER] {downloadDirectory}",
            ConsoleLineType.Success
        );

        WriteLine(
            "",
            ConsoleLineType.Normal
        );

        SetStatus("READY");
    }

    private void UpdateFolderLayout()
    {
        int left = btnFolder.Right + 10;

        int right = btnExport.Left - 10;

        lblFolder.Left = left;

        lblFolder.Width =
            Math.Max(
                50,
                right - left
            );
    }
    private void UpdateFolderUi()
    {
        lblFolder.Text =
            $"Folder: \"{downloadDirectory}\"";

        folderToolTip.SetToolTip(
            lblFolder,
            downloadDirectory
        );

        UpdateFolderLayout();

        Text = "Downloader";
    }

    private static string ShortenPath(
    string path,
    int maxLength)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "...";

        if (path.Length <= maxLength)
            return path;

        return "..." + path.Substring(
            path.Length - maxLength
        );
    }

    // ==========================================
    // DNS
    // ==========================================

    private void BtnSetDns_Click(
        object? sender,
        EventArgs e)
    {
        ConfigureDns();
    }

    private void TxtDns_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;

        e.SuppressKeyPress = true;

        ConfigureDns();
    }

    private void ConfigureDns()
    {
        string value =
            txtDns.Text.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            ShowError(
                "Enter DNS or DoH endpoint."
            );

            return;
        }

        try
        {
            if (value.Equals(
                    "system",
                    StringComparison.OrdinalIgnoreCase))
            {
                networkProvider.SetSystem();
            }
            else
            {
                networkProvider.SetDoH(value);
            }

            UpdateNetworkUi();

            WriteLine(
                $"[NETWORK] {networkProvider.DisplayName}",
                ConsoleLineType.Success
            );
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    // ==========================================
    // NETWORK
    // ==========================================

    private void BtnNetwork_Click(
        object? sender,
        EventArgs e)
    {
        if (networkProvider.Mode ==
            NetworkMode.System)
        {
            try
            {
                networkProvider.SetDoH(
                    txtDns.Text.Trim()
                );
            }
            catch
            {
                txtDns.Text = "1.1.1.1";

                networkProvider.SetDoH(
                    "1.1.1.1"
                );
            }
        }
        else
        {
            networkProvider.SetSystem();
        }

        UpdateNetworkUi();
    }

    private void UpdateNetworkUi()
    {
        if (networkProvider.Mode ==
            NetworkMode.System)
        {
            btnNetwork.Text = "SYSTEM";
            lblNetwork.Text = "System DNS";
        }
        else
        {
            btnNetwork.Text = "DoH";

            string shortName =
                networkProvider.DohUrl
                    .Replace(
                        "https://",
                        ""
                    )
                    .TrimEnd('/');

            lblNetwork.Text =
                shortName;
        }

        lblStatus.Text =
            $"READY | {networkProvider.DisplayName}";
    }

    // ==========================================
    // COMMAND
    // ==========================================

    private async void BtnRun_Click(
        object? sender,
        EventArgs e)
    {
        await RunCommandAsync();
    }

    private async void TxtCommand_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;

        e.SuppressKeyPress = true;

        await RunCommandAsync();
    }

    private async Task RunCommandAsync()
    {
        string command =
            txtCommand.Text.Trim();

        if (string.IsNullOrWhiteSpace(command))
            return;

        if (command.Equals(
                "clear",
                StringComparison.OrdinalIgnoreCase))
        {
            txtTerminal.Clear();
            return;
        }

        if (command.Equals(
                "help",
                StringComparison.OrdinalIgnoreCase))
        {
            ShowHelp();
            return;
        }

        List<string> args;

        try
        {
            args =
                CommandParser.Parse(command);
        }
        catch (Exception ex)
        {
            ShowError(
                $"Command parser error: {ex.Message}"
            );

            return;
        }

        if (args.Count == 0)
            return;

        if (!args[0].Equals(
                "curl",
                StringComparison.OrdinalIgnoreCase))
        {
            ShowError(
                "Only curl commands are supported."
            );

            return;
        }

        args.RemoveAt(0);

        if (args.Count == 0)
        {
            ShowError(
                "curl command is empty."
            );

            return;
        }

        await ExecuteCurlAsync(args);
    }

    // ==========================================
    // CURL
    // ==========================================

    private async Task ExecuteCurlAsync(
        List<string> userArguments)
    {
        btnRun.Enabled = false;
        txtCommand.Enabled = false;

        SetStatus("RUNNING...");

        WriteLine(
            $"> curl {JoinForDisplay(userArguments)}",
            ConsoleLineType.Command
        );

        WriteLine(
            $"[NETWORK] {networkProvider.DisplayName}",
            ConsoleLineType.Network
        );

        WriteLine(
            $"[FOLDER] {downloadDirectory}",
            ConsoleLineType.Network
        );

        try
        {
            var finalArguments =
                new List<string>();

            finalArguments.AddRange(
                userArguments
            );

            // Добавляем DoH автоматически.
            if (networkProvider.Mode ==
                    NetworkMode.DoH &&
                !ContainsDohOption(
                    userArguments))
            {
                finalArguments.Add("--doh-url");

                finalArguments.Add(
                    networkProvider.DohUrl
                );
            }

            await RunCurlProcessAsync(
                finalArguments
            );
        }
        catch (Exception ex)
        {
            ShowError(
                ex.Message
            );
        }
        finally
        {
            btnRun.Enabled = true;
            txtCommand.Enabled = true;

            SetStatus("READY");

            WriteLine(
                "",
                ConsoleLineType.Normal
            );

            WriteLine(
                "[READY]",
                ConsoleLineType.Success
            );
        }
    }

    private async Task RunCurlProcessAsync(
        List<string> arguments)
    {
        if (!Directory.Exists(
                downloadDirectory))
        {
            Directory.CreateDirectory(
                downloadDirectory
            );
        }

        var startInfo =
            new ProcessStartInfo
            {
                FileName = "curl.exe",

                // ВОТ ГЛАВНОЕ:
                // curl будет работать из выбранной папки.
                WorkingDirectory =
                    downloadDirectory,

                UseShellExecute = false,

                RedirectStandardOutput = true,
                RedirectStandardError = true,

                CreateNoWindow = true,

                StandardOutputEncoding =
                    Encoding.UTF8,

                StandardErrorEncoding =
                    Encoding.UTF8
            };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(
                argument
            );
        }

        using var process =
            new Process();

        process.StartInfo =
            startInfo;

        process.OutputDataReceived +=
            Process_OutputDataReceived;

        process.ErrorDataReceived +=
            Process_ErrorDataReceived;

        try
        {
            process.Start();
        }
        catch
        {
            throw new InvalidOperationException(
                "curl.exe не найден. " +
                "Проверь, что curl доступен в PATH."
            );
        }

        WriteLine(
            "[curl] Process started.",
            ConsoleLineType.Network
        );

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        process.WaitForExit();

        WriteLine(
            $"[curl] Exit code: {process.ExitCode}",
            process.ExitCode == 0
                ? ConsoleLineType.Success
                : ConsoleLineType.Error
        );
    }

    private void Process_OutputDataReceived(
        object sender,
        DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            WriteLine(
                e.Data,
                ConsoleLineType.Normal
            );
        }
    }

    private void Process_ErrorDataReceived(
        object sender,
        DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            WriteLine(
                e.Data,
                ConsoleLineType.Curl
            );
        }
    }

    private static bool ContainsDohOption(
        List<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (argument.Equals(
                    "--doh-url",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (argument.StartsWith(
                    "--doh-url=",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ==========================================
    // HELP
    // ==========================================

    private void BtnHelp_Click(
        object? sender,
        EventArgs e)
    {
        ShowHelp();
    }

    private void ShowHelp()
    {
        WriteLine(
            "",
            ConsoleLineType.Normal
        );

        WriteLine(
            "=== Downloader Help ===",
            ConsoleLineType.Success
        );

        WriteLine(
            "",
            ConsoleLineType.Normal
        );

        WriteLine(
            "NETWORK",
            ConsoleLineType.Network
        );

        WriteLine(
            "  1.1.1.1       Cloudflare DoH",
            ConsoleLineType.Normal
        );

        WriteLine(
            "  8.8.8.8       Google DoH",
            ConsoleLineType.Normal
        );

        WriteLine(
            "  9.9.9.9       Quad9 DoH",
            ConsoleLineType.Normal
        );

        WriteLine(
            "  system         System DNS",
            ConsoleLineType.Normal
        );

        WriteLine(
            "",
            ConsoleLineType.Normal
        );

        WriteLine(
            "DOWNLOAD FOLDER",
            ConsoleLineType.Network
        );

        WriteLine(
            "  FOLDER button selects where relative",
            ConsoleLineType.Normal
        );

        WriteLine(
            "  curl output files will be created.",
            ConsoleLineType.Normal
        );

        WriteLine(
            "",
            ConsoleLineType.Normal
        );

        WriteLine(
            "CURL",
            ConsoleLineType.Network
        );

        WriteLine(
            "  curl -L -o file.zip URL",
            ConsoleLineType.Normal
        );

        WriteLine(
            "  curl -L -O URL",
            ConsoleLineType.Normal
        );

        WriteLine(
            "  curl -I URL",
            ConsoleLineType.Normal
        );

        WriteLine(
            "  curl -L -H \"User-Agent: Mozilla/5.0\" URL",
            ConsoleLineType.Normal
        );

        WriteLine(
            "",
            ConsoleLineType.Normal
        );

        WriteLine(
            "COMMANDS",
            ConsoleLineType.Network
        );

        WriteLine(
            "  help     Show help",
            ConsoleLineType.Normal
        );

        WriteLine(
            "  clear    Clear terminal",
            ConsoleLineType.Normal
        );

        WriteLine(
            "",
            ConsoleLineType.Normal
        );
    }

    // ==========================================
    // CLEAR
    // ==========================================

    private void BtnClear_Click(
        object? sender,
        EventArgs e)
    {
        txtTerminal.Clear();
    }

    // ==========================================
    // STATUS
    // ==========================================

    private void SetStatus(
        string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(
                new Action(() =>
                    SetStatus(text))
            );

            return;
        }

        lblStatus.Text =
            text;
    }

    private void ShowError(
        string message)
    {
        WriteLine(
            $"[ERROR] {message}",
            ConsoleLineType.Error
        );

        SetStatus("ERROR");
    }

    // export

    private void BtnExport_Click(
    object? sender,
    EventArgs e)
    {
        if (string.IsNullOrEmpty(
                txtTerminal.Text))
        {
            MessageBox.Show(
                this,
                "Терминал пуст.",
                "Export",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            return;
        }

        using var dialog =
            new FolderBrowserDialog
            {
                Description =
                    "Выберите папку для сохранения лога",

                UseDescriptionForTitle = true,

                ShowNewFolderButton = true
            };

        DialogResult result =
            dialog.ShowDialog(this);

        if (result != DialogResult.OK)
            return;

        string selectedDirectory =
            dialog.SelectedPath;

        try
        {
            if (!Directory.Exists(
                    selectedDirectory))
            {
                Directory.CreateDirectory(
                    selectedDirectory
                );
            }

            string timestamp =
                DateTime.Now.ToString(
                    "yyyy-MM-dd_HHmmss"
                );

            string fileName =
                $"Downloader_{timestamp}.txt";

            string filePath =
                Path.Combine(
                    selectedDirectory,
                    fileName
                );

            // UTF-8 с BOM — хорошо открывается
            // в стандартном Windows Notepad.
            File.WriteAllText(
                filePath,
                txtTerminal.Text,
                new UTF8Encoding(true)
            );

            WriteLine(
                $"[EXPORT] Log saved: {filePath}",
                ConsoleLineType.Success
            );

            MessageBox.Show(
                this,
                $"Лог сохранён:\n\n{filePath}",
                "Export",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
        catch (Exception ex)
        {
            ShowError(
                $"Не удалось сохранить лог: {ex.Message}"
            );
        }
    }

    // ==========================================
    // TERMINAL
    // ==========================================

    private enum ConsoleLineType
    {
        Normal,
        Command,
        Network,
        Success,
        Error,
        Curl
    }

    private void WriteLine(
        string text,
        ConsoleLineType type)
    {
        if (InvokeRequired)
        {
            BeginInvoke(
                new Action(() =>
                    WriteLine(
                        text,
                        type))
            );

            return;
        }

        Color color = type switch
        {
            ConsoleLineType.Command =>
                Color.FromArgb(230, 230, 235),

            ConsoleLineType.Network =>
                Color.FromArgb(175, 155, 235),

            ConsoleLineType.Success =>
                SuccessColor,

            ConsoleLineType.Error =>
                ErrorColor,

            ConsoleLineType.Curl =>
                Color.FromArgb(190, 190, 195),

            _ =>
                Color.FromArgb(215, 215, 220)
        };

        txtTerminal.SelectionStart =
            txtTerminal.TextLength;

        txtTerminal.SelectionLength = 0;

        txtTerminal.SelectionColor =
            color;

        txtTerminal.AppendText(
            text +
            Environment.NewLine
        );

        txtTerminal.SelectionStart =
            txtTerminal.TextLength;

        txtTerminal.ScrollToCaret();
    }

    // ==========================================
    // DISPLAY
    // ==========================================

    private static string JoinForDisplay(
        List<string> arguments)
    {
        var result =
            new StringBuilder();

        foreach (string argument in arguments)
        {
            if (result.Length > 0)
                result.Append(' ');

            if (argument.Contains(' ') ||
                argument.Contains('"'))
            {
                result.Append('"');

                result.Append(
                    argument.Replace(
                        "\"",
                        "\\\""
                    )
                );

                result.Append('"');
            }
            else
            {
                result.Append(argument);
            }
        }

        return result.ToString();
    }
}