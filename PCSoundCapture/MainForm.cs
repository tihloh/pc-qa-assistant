using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace PCSoundCapture;

/// <summary>Q&A first. Captured audio and transcript stay in memory unless saved explicitly.</summary>
internal sealed class MainForm : Form
{
    private readonly MMDeviceEnumerator _devices = new();
    private readonly List<MMDevice> _ownedDevices = [];
    private readonly AudioCaptureService _capture = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly OllamaClient _ai = new();
    private readonly AnswerOverlayForm _overlay = new();
    private ScreenAssistantForm? _screenAssistant;
    private void ShowScreenAssistant()
    {
        if (_screenAssistant == null || _screenAssistant.IsDisposed)
            _screenAssistant = new ScreenAssistantForm(this, _overlay, _aiServer.Text,
                _currentAiProvider == "Local" ? _aiModel.Text : _settings.AiModel,
                _currentAiProvider == "Cloud" ? _aiModel.Text : _settings.CloudModel, _settings);
        _screenAssistant.Show();
        _screenAssistant.Activate();
    }
    private readonly ComboBox _source = Combo();
    private readonly ComboBox _output = Combo();
    private readonly ComboBox _application = Combo();
    private readonly ComboBox _microphone = Combo();
    private readonly CheckBox _includeMic = new() { Text = "Include microphone", AutoSize = true };
    private readonly CheckBox _speech = new() { Text = "Speech-to-text", AutoSize = true };
    private readonly CheckBox _autoAnswer = new() { Text = "Automatically answer detected questions", AutoSize = true };
    private readonly CheckBox _saveAudio = new() { Text = "Save WAV (optional)", AutoSize = true };
    private readonly CheckBox _saveTranscript = new() { Text = "Save transcript (optional)", AutoSize = true };
    private readonly CheckBox _overlayEnabled = new() { Text = "Floating answers", AutoSize = true };
    private readonly TextBox _folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _model = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ComboBox _aiProvider = Combo();
    private readonly TextBox _aiModel = new() { Dock = DockStyle.Fill };
    private readonly TextBox _cloudKey = new()
    {
        Dock = DockStyle.Fill, UseSystemPasswordChar = true,
        PlaceholderText = "Paste API key, then click Save key"
    };
    private readonly Button _saveKey = Button("Save key");
    private readonly Button _removeKey = Button("Remove key");
    private readonly CheckBox _includeContext = new()
    {
        Text = "Include recent recognized conversation in AI requests (optional)", AutoSize = true
    };
    private string _currentAiProvider = "Cloud";
    private readonly TextBox _aiServer = new() { Dock = DockStyle.Fill };
    private readonly TextBox _questionInput = new() { Dock = DockStyle.Fill, PlaceholderText = "Type a question or edit one you heard..." };
    private readonly Label _status = Info("Ready — audio recording is off by default");
    private readonly Label _time = Info("00:00:00");
    private readonly Label _partial = Info("Press Start listening to transcribe speech.");
    private readonly Label _lastQuestion = new()
    {
        Text = "No question detected yet.", Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
    };
    private readonly TextBox _transcript = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 9.5f)
    };
    private readonly TextBox _answer = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10.5f),
        Text = "Ask a typed question to test AI, or start listening for spoken questions."
    };
    private readonly Button _start = Button("Start listening");
    private readonly Button _stop = Button("Stop listening");
    private readonly Button _ask = Button("Ask AI");
    private readonly Button _refresh = Button("Refresh devices / apps");
    private readonly Button _modelBrowse = Button("Model folder...");
    private readonly Button _browse = Button("Save folder...");
    private readonly Button _open = Button("Open saved files");
    private readonly Button _showOverlay = Button("Show answer window");
    private readonly Button _copy = Button("Copy answer");
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _speechDebounce = new() { Interval = 1600 };
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _trayListen = new("Start listening");
    private readonly ToolStripMenuItem _trayStop = new("Stop listening");
    private readonly List<string> _recentTranscript = [];
    private readonly List<string> _unprocessedUtterances = [];
    private readonly Queue<string> _questionQueue = new();
    private readonly CancellationTokenSource _lifetime = new();
    private string? _lastQueuedQuestion;
    private DateTimeOffset _lastQuestionTime;
    private bool _answering;
    private bool _starting;
    private bool _exiting;
    private bool _disposed;
    private bool _firstHide = true;
    private DateTimeOffset _listenStarted;

    public MainForm()
    {
        Text = "PC Q&A Assistant — listen, detect, answer";
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(960, 920);
        MinimumSize = new Size(840, 820);
        _source.Items.AddRange(["Entire PC (selected output)",
            "Selected application + child processes", "Microphone only"]);
        _source.SelectedIndex = Enum.TryParse<CaptureSource>(_settings.CaptureSource, out var mode)
            ? (int)mode : 0;
        _includeMic.Checked = _settings.IncludeMicrophone;
        _speech.Checked = _settings.LiveTranscription;
        _autoAnswer.Checked = _settings.AutoAnswerQuestions;
        _saveAudio.Checked = _settings.SaveRecording;
        _saveTranscript.Checked = _settings.SaveTranscript;
        _overlayEnabled.Checked = _settings.ShowAnswerOverlay;
        _folder.Text = _settings.OutputDirectory;
        _model.Text = _settings.VoskModelDirectory;
        _aiProvider.Items.AddRange(["Ollama Cloud (API key)", "Local Ollama (optional)"]);
        _currentAiProvider = string.Equals(_settings.AiProvider, "Local",
            StringComparison.OrdinalIgnoreCase) ? "Local" : "Cloud";
        _aiProvider.SelectedIndex = _currentAiProvider == "Cloud" ? 0 : 1;
        _aiModel.Text = _currentAiProvider == "Cloud" ? _settings.CloudModel : _settings.AiModel;
        _includeContext.Checked = _settings.IncludeAiContext;
        _aiServer.Text = _settings.AiEndpoint;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 206));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 238));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 66));
        root.Controls.Add(new Label
        {
            Text = "PC Q&A Assistant    •    Listening and answering, not recording",
            Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var sourceGroup = Group("Audio source");
        var sources = Fields(5, 148);
        sources.Controls.Add(FieldLabel("Listen to"), 0, 0);
        sources.Controls.Add(_source, 1, 0);
        sources.Controls.Add(FieldLabel("Output device"), 0, 1);
        sources.Controls.Add(_output, 1, 1);
        sources.Controls.Add(FieldLabel("Application"), 0, 2);
        sources.Controls.Add(_application, 1, 2);
        sources.Controls.Add(FieldLabel("Microphone"), 0, 3);
        sources.Controls.Add(_microphone, 1, 3);
        var sourceBottom = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        sourceBottom.Controls.AddRange([_includeMic, _refresh]);
        sources.Controls.Add(sourceBottom, 1, 4);
        sourceGroup.Controls.Add(sources);
        root.Controls.Add(sourceGroup, 0, 1);

        var aiGroup = Group("Speech recognition and AI (Ollama Cloud by default)");
        var aiFields = Fields(6, 160);
        aiFields.ColumnCount = 3;
        aiFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        aiFields.Controls.Add(FieldLabel("Vosk speech model"), 0, 0);
        aiFields.Controls.Add(_model, 1, 0);
        aiFields.Controls.Add(_modelBrowse, 2, 0);
        aiFields.Controls.Add(FieldLabel("AI provider"), 0, 1);
        aiFields.Controls.Add(_aiProvider, 1, 1);
        var cloudDocs = new LinkLabel { Text = "Cloud setup", AutoSize = true };
        cloudDocs.LinkClicked += (_, _) => OpenUrl("https://docs.ollama.com/cloud");
        aiFields.Controls.Add(cloudDocs, 2, 1);
        aiFields.Controls.Add(FieldLabel("AI model"), 0, 2);
        aiFields.Controls.Add(_aiModel, 1, 2);
        var modelHelp = new LinkLabel { Text = "Browse cloud models", AutoSize = true };
        modelHelp.LinkClicked += (_, _) => OpenUrl("https://ollama.com/search?c=cloud");
        aiFields.Controls.Add(modelHelp, 2, 2);
        aiFields.Controls.Add(FieldLabel("Cloud API key"), 0, 3);
        aiFields.Controls.Add(_cloudKey, 1, 3);
        var keyButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
            Margin = Padding.Empty };
        keyButtons.Controls.AddRange([_saveKey, _removeKey]);
        aiFields.Controls.Add(keyButtons, 2, 3);
        aiFields.Controls.Add(FieldLabel("Local Ollama server"), 0, 4);
        aiFields.Controls.Add(_aiServer, 1, 4);
        var keysHelp = new LinkLabel { Text = "Create Ollama API key", AutoSize = true };
        keysHelp.LinkClicked += (_, _) => OpenUrl("https://ollama.com/settings/keys");
        aiFields.Controls.Add(keysHelp, 2, 4);
        aiFields.Controls.Add(FieldLabel("Privacy"), 0, 5);
        aiFields.Controls.Add(_includeContext, 1, 5);
        var speechHelp = new LinkLabel { Text = "Vosk models", AutoSize = true };
        speechHelp.LinkClicked += (_, _) => OpenUrl("https://alphacephei.com/vosk/models");
        aiFields.Controls.Add(speechHelp, 2, 5);
        aiGroup.Controls.Add(aiFields);
        root.Controls.Add(aiGroup, 0, 2);

        var controlGroup = Group("Listening and optional saving");
        var controlRows = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        controlRows.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        controlRows.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var toggles = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        toggles.Controls.AddRange([_speech, _autoAnswer, _overlayEnabled, _saveAudio, _saveTranscript]);
        controlRows.Controls.Add(toggles, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _status.AutoSize = false;
        _status.Width = 190;
        actions.Controls.AddRange([_start, _stop, _time, _status, _browse, _open]);
        controlRows.Controls.Add(actions, 0, 1);
        controlGroup.Controls.Add(controlRows);
        root.Controls.Add(controlGroup, 0, 3);

        var transcriptGroup = Group("Live speech-to-text (in memory unless saved)");
        var speechLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        speechLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        speechLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        speechLayout.Controls.Add(_transcript, 0, 0);
        speechLayout.Controls.Add(_partial, 0, 1);
        transcriptGroup.Controls.Add(speechLayout);
        root.Controls.Add(transcriptGroup, 0, 4);

        var answerGroup = Group("AI questions and answers");
        var answers = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        answers.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        answers.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        answers.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        answers.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var askRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        askRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        askRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        askRow.Controls.Add(_questionInput, 0, 0);
        askRow.Controls.Add(_ask, 1, 0);
        answers.Controls.Add(askRow, 0, 0);
        answers.Controls.Add(_lastQuestion, 0, 1);
        answers.Controls.Add(_answer, 0, 2);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var screenAssistant = Button("Screen assistant");
        screenAssistant.Click += (_, _) => ShowScreenAssistant();
        bottom.Controls.AddRange([_showOverlay, _copy, screenAssistant]);
        answers.Controls.Add(bottom, 0, 3);
        answerGroup.Controls.Add(answers);
        root.Controls.Add(answerGroup, 0, 5);
        Controls.Add(root);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show main window", null, (_, _) => RestoreWindow());
        menu.Items.Add(_trayListen);
        menu.Items.Add(_trayStop);
        menu.Items.Add("Show answer window", null, (_, _) => ShowOverlay());
        menu.Items.Add("Screen assistant", null, (_, _) => ShowScreenAssistant());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PC Q&A Assistant", Visible = true, ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => RestoreWindow();
        _trayListen.Click += async (_, _) => await StartListeningAsync();
        _trayStop.Click += async (_, _) => await StopListeningAsync();

        _source.SelectedIndexChanged += (_, _) => UpdateUi();
        _includeMic.CheckedChanged += (_, _) => UpdateUi();
        _speech.CheckedChanged += (_, _) => UpdateUi();
        _saveAudio.CheckedChanged += (_, _) => UpdateUi();
        _saveTranscript.CheckedChanged += (_, _) => UpdateUi();
        _autoAnswer.CheckedChanged += (_, _) => StoreSettings();
        _includeContext.CheckedChanged += (_, _) => StoreSettings();
        _aiProvider.SelectedIndexChanged += (_, _) => SwitchAiProvider();
        _saveKey.Click += (_, _) => SaveCloudKey();
        _removeKey.Click += (_, _) => RemoveCloudKey();
        _overlayEnabled.CheckedChanged += (_, _) =>
        {
            if (!_overlayEnabled.Checked) _overlay.Hide();
            StoreSettings();
        };
        _output.SelectedIndexChanged += (_, _) => UpdateUi();
        _application.SelectedIndexChanged += (_, _) => UpdateUi();
        _microphone.SelectedIndexChanged += (_, _) => UpdateUi();
        _modelBrowse.Click += (_, _) => BrowseFolder(_model, "Choose extracted Vosk language model");
        _browse.Click += (_, _) => BrowseFolder(_folder, "Choose save location (optional)");
        _open.Click += (_, _) => OpenFolder();
        _refresh.Click += (_, _) => RefreshChoices();
        _showOverlay.Click += (_, _) => ShowOverlay();
        _copy.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_answer.Text)) Clipboard.SetText(_answer.Text);
        };
        _ask.Click += (_, _) => AskTypedQuestion();
        _questionInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AskTypedQuestion();
            }
        };
        _start.Click += async (_, _) => await StartListeningAsync();
        _stop.Click += async (_, _) => await StopListeningAsync();
        _capture.Completed += OnCaptureCompleted;
        _capture.TranscriptUpdated += OnTranscript;
        _clock.Tick += (_, _) =>
        {
            if (_capture.IsActive)
                _time.Text = (DateTimeOffset.Now - _listenStarted).ToString(@"hh\:mm\:ss");
        };
        _speechDebounce.Tick += (_, _) => ProcessPendingSpeech();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) HideToTray(); };
        FormClosing += OnFormClosing;
        RefreshChoices();
        UpdateAiProviderUi();
        UpdateUi();
    }

    private static ComboBox Combo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill,
        IntegralHeight = false, MaxDropDownItems = 14
    };
    private static Button Button(string name) => new() { Text = name, AutoSize = true };
    private static Label Info(string text) => new()
    {
        Text = text, AutoSize = true, Margin = new Padding(5, 8, 6, 0)
    };
    private static Label FieldLabel(string text) => new()
    {
        Text = text, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill
    };
    private static GroupBox Group(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, Padding = new Padding(10, 13, 10, 7)
    };
    private static TableLayoutPanel Fields(int rows, int labelWidth)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows,
            Padding = new Padding(3)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows; i++)
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        return table;
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open link"); }
    }

    private void RefreshChoices()
    {
        if (_capture.IsActive || _starting) return;
        string? outputId = (_output.SelectedItem as DeviceItem)?.Device.ID ?? _settings.OutputDeviceId;
        string? micId = (_microphone.SelectedItem as DeviceItem)?.Device.ID ?? _settings.MicrophoneDeviceId;
        int? oldPid = (_application.SelectedItem as ProcessItem)?.Id;
        try
        {
            _output.Items.Clear();
            _microphone.Items.Clear();
            foreach (var old in _ownedDevices) old.Dispose();
            _ownedDevices.Clear();
            string? defaultOutput = DefaultId(DataFlow.Render);
            string? defaultMic = DefaultId(DataFlow.Capture);
            foreach (var device in _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                _ownedDevices.Add(device);
                _output.Items.Add(new DeviceItem(device));
            }
            foreach (var device in _devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                _ownedDevices.Add(device);
                _microphone.Items.Add(new DeviceItem(device));
            }
            SelectDevice(_output, outputId, defaultOutput);
            SelectDevice(_microphone, micId, defaultMic);
            _application.Items.Clear();
            var processes = new List<ProcessItem>();
            using var current = Process.GetCurrentProcess();
            int sessionId = current.SessionId;
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == Environment.ProcessId) continue;
                        string name = process.ProcessName;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        string title = process.MainWindowTitle;
                        if (string.IsNullOrWhiteSpace(title) && process.SessionId != sessionId)
                            continue;
                        processes.Add(new ProcessItem(process.Id, name, title));
                    }
                    catch { /* protected/system process: ignore */ }
                }
            }
            foreach (var item in processes.OrderBy(x => string.IsNullOrEmpty(x.WindowTitle))
                .ThenBy(x => x.Name).ThenBy(x => x.Id))
            {
                _application.Items.Add(item);
                if (item.Id == oldPid) _application.SelectedIndex = _application.Items.Count - 1;
            }
            if (_application.SelectedIndex < 0 && _application.Items.Count > 0)
                _application.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Refresh audio devices", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        UpdateUi();
    }

    private string? DefaultId(DataFlow flow)
    {
        try
        {
            using var device = _devices.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.ID;
        }
        catch { return null; }
    }

    private static void SelectDevice(ComboBox combo, string? preferred, string? fallback)
    {
        foreach (string? candidate in new[] { preferred, fallback })
        {
            if (candidate is null) continue;
            for (int i = 0; i < combo.Items.Count; i++)
                if (combo.Items[i] is DeviceItem item && item.Device.ID == candidate)
                {
                    combo.SelectedIndex = i;
                    return;
                }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void BrowseFolder(TextBox box, string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description, SelectedPath = box.Text,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            box.Text = dialog.SelectedPath;
            StoreSettings();
        }
    }

    private void StoreSettings()
    {
        if (_disposed) return;
        _settings.CaptureSource = SelectedSource.ToString();
        _settings.OutputDeviceId = (_output.SelectedItem as DeviceItem)?.Device.ID;
        _settings.MicrophoneDeviceId = (_microphone.SelectedItem as DeviceItem)?.Device.ID;
        _settings.IncludeMicrophone = _includeMic.Checked;
        _settings.LiveTranscription = _speech.Checked;
        _settings.SaveRecording = _saveAudio.Checked;
        _settings.SaveTranscript = _saveTranscript.Checked;
        _settings.AutoAnswerQuestions = _autoAnswer.Checked;
        _settings.ShowAnswerOverlay = _overlayEnabled.Checked;
        _settings.OutputDirectory = _folder.Text;
        _settings.VoskModelDirectory = _model.Text;
        _settings.AiProvider = _currentAiProvider;
        if (_currentAiProvider == "Cloud") _settings.CloudModel = _aiModel.Text;
        else _settings.AiModel = _aiModel.Text;
        _settings.IncludeAiContext = _includeContext.Checked;
        _settings.AiEndpoint = _aiServer.Text;
        try { _settings.Save(); }
        catch (Exception ex) { _status.Text = $"Cannot save settings: {ex.Message}"; }
    }


    private void SwitchAiProvider()
    {
        string choice = _aiProvider.SelectedIndex == 0 ? "Cloud" : "Local";
        if (choice != _currentAiProvider)
        {
            if (_currentAiProvider == "Cloud") _settings.CloudModel = _aiModel.Text;
            else _settings.AiModel = _aiModel.Text;
            _currentAiProvider = choice;
            _aiModel.Text = choice == "Cloud" ? _settings.CloudModel : _settings.AiModel;
        }
        UpdateAiProviderUi();
        StoreSettings();
    }

    private void UpdateAiProviderUi()
    {
        bool cloud = _currentAiProvider == "Cloud";
        _aiServer.Enabled = !cloud;
        _cloudKey.Enabled = cloud;
        _saveKey.Enabled = cloud;
        _removeKey.Enabled = cloud && CloudKeyStore.HasStoredKey;
        _cloudKey.PlaceholderText = CloudKeyStore.IsEnvironmentConfigured
            ? "Using OLLAMA_API_KEY environment variable"
            : CloudKeyStore.HasStoredKey
                ? "Key saved securely (Windows DPAPI)"
                : "Paste cloud API key, then click Save key";
    }

    private void SaveCloudKey()
    {
        try
        {
            CloudKeyStore.SaveKey(_cloudKey.Text);
            _cloudKey.Clear();
            UpdateAiProviderUi();
            _status.Text = "Ollama Cloud API key saved for this Windows user";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cloud API key", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void RemoveCloudKey()
    {
        try
        {
            CloudKeyStore.RemoveKey();
            _cloudKey.Clear();
            UpdateAiProviderUi();
            _status.Text = CloudKeyStore.IsEnvironmentConfigured
                ? "Stored key removed; environment variable still active"
                : "Ollama Cloud API key removed";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cloud API key", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private CaptureSource SelectedSource => (CaptureSource)Math.Max(_source.SelectedIndex, 0);
    private bool NeedsMicrophone => SelectedSource == CaptureSource.MicrophoneOnly || _includeMic.Checked;

    private async Task StartListeningAsync()
    {
        if (_capture.IsActive || _starting) return;
        if (_speech.Checked && !Directory.Exists(_model.Text))
        {
            MessageBox.Show(this,
                "Speech recognition needs an extracted Vosk model folder. Download one from " +
                "the Vosk models link, then choose its extracted directory. " +
                "You can still type questions directly without a speech model.",
                "Speech model required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _starting = true;
        _status.Text = "Starting...";
        UpdateUi();
        try
        {
            StoreSettings();
            var options = new CaptureOptions(
                SelectedSource,
                (_output.SelectedItem as DeviceItem)?.Device,
                (_microphone.SelectedItem as DeviceItem)?.Device,
                (_application.SelectedItem as ProcessItem)?.Id,
                NeedsMicrophone,
                _speech.Checked,
                _model.Text,
                _folder.Text,
                _saveAudio.Checked,
                _speech.Checked && _saveTranscript.Checked);
            await _capture.StartAsync(options);
            _listenStarted = DateTimeOffset.Now;
            _time.Text = "00:00:00";
            _transcript.Clear();
            _recentTranscript.Clear();
            _unprocessedUtterances.Clear();
            _speechDebounce.Stop();
            _partial.Text = _speech.Checked ? "Listening for speech..." : "Speech-to-text disabled (recording only).";
            _status.Text = _saveAudio.Checked ? "Listening + recording" : "Listening (no audio saved)";
            _clock.Start();
        }
        catch (Exception ex)
        {
            _status.Text = "Unable to start listening";
            MessageBox.Show(this, ex.Message, "Audio capture error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _starting = false;
            UpdateUi();
            if (_exiting)
            {
                if (_capture.IsActive) _ = StopListeningAsync();
                else Close();
            }
        }
    }

    private async Task StopListeningAsync()
    {
        if (!_capture.IsActive || _capture.IsStopping) return;
        _status.Text = "Stopping listening...";
        UpdateUi();
        try { await _capture.StopAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Stop listening", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        UpdateUi();
    }

    private void OnCaptureCompleted(object? sender, CaptureCompletedEventArgs result)
    {
        if (InvokeRequired)
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke((Action)(() => OnCaptureCompleted(sender, result)));
            return;
        }
        if (_disposed) return;
        _clock.Stop();
        ProcessPendingSpeech();
        _partial.Text = "Not listening.";
        if (result.Error is not null)
        {
            _status.Text = "Audio capture stopped unexpectedly";
            MessageBox.Show(this, result.Error.Message, "Capture interrupted",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
            _status.Text = result.FilePath is null
                ? "Stopped — nothing recorded" : $"Saved {Path.GetFileName(result.FilePath)}";
        if (result.DroppedTranscriptChunks > 0)
            _status.Text += $" · {result.DroppedTranscriptChunks} speech chunks skipped";
        UpdateUi();
        if (_exiting) Close();
    }

    private void OnTranscript(object? sender, TranscriptEventArgs data)
    {
        if (InvokeRequired)
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke((Action)(() => OnTranscript(sender, data)));
            return;
        }
        if (_disposed) return;
        if (!data.IsFinal)
        {
            _partial.Text = string.IsNullOrWhiteSpace(data.Text)
                ? "Listening for speech..." : "Hearing: " + data.Text;
            return;
        }
        if (string.IsNullOrWhiteSpace(data.Text)) return;
        _transcript.AppendText($"[{DateTime.Now:HH:mm:ss}] {data.Text}{Environment.NewLine}");
        _recentTranscript.Add(data.Text);
        if (_recentTranscript.Count > 30) _recentTranscript.RemoveAt(0);
        _unprocessedUtterances.Add(data.Text);
        if (_unprocessedUtterances.Count > 8)
            _unprocessedUtterances.RemoveAt(0);
        _speechDebounce.Stop();
        _speechDebounce.Start();
        _partial.Text = "Listening for the next phrase...";
    }

    private void ProcessPendingSpeech()
    {
        _speechDebounce.Stop();
        if (_unprocessedUtterances.Count == 0) return;
        string phrase = string.Join(" ", _unprocessedUtterances);
        _unprocessedUtterances.Clear();
        if (!_autoAnswer.Checked) return;
        string? question = QuestionDetector.ExtractQuestion(phrase);
        if (question is not null) QueueQuestion(question);
    }

    private void AskTypedQuestion()
    {
        string entered = _questionInput.Text.Trim();
        if (entered.Length == 0)
        {
            // Useful for answering the last speech fragment manually.
            entered = _recentTranscript.Count > 0 ? _recentTranscript[^1] : string.Empty;
        }
        if (entered.Length == 0)
        {
            MessageBox.Show(this, "Type a question first, or start listening.",
                "No question", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        QueueQuestion(entered, manual: true);
    }

    private void QueueQuestion(string question, bool manual = false)
    {
        question = question.Trim();
        if (question.Length == 0) return;
        if (!manual && string.Equals(_lastQueuedQuestion, question,
                StringComparison.OrdinalIgnoreCase) &&
            (DateTimeOffset.Now - _lastQuestionTime) < TimeSpan.FromSeconds(45))
            return;
        _lastQueuedQuestion = question;
        _lastQuestionTime = DateTimeOffset.Now;
        _lastQuestion.Text = "Question: " + question;
        if (_questionQueue.Count >= 5) _questionQueue.Dequeue();
        _questionQueue.Enqueue(question);
        _ = RunAnswerQueueAsync();
    }

    private async Task RunAnswerQueueAsync()
    {
        if (_answering) return;
        _answering = true;
        try
        {
            while (_questionQueue.Count > 0 && !_lifetime.IsCancellationRequested)
            {
                string question = _questionQueue.Dequeue();
                string context = _includeContext.Checked
                    ? string.Join(" ", _recentTranscript.TakeLast(12)) : string.Empty;
                bool useCloud = _currentAiProvider == "Cloud";
                string aiModel = _aiModel.Text.Trim();
                string aiEndpoint = _aiServer.Text.Trim();
                _lastQuestion.Text = "Question: " + question;
                _answer.Text = "Generating an answer using " + aiModel + "...";
                UpdateOverlay(question, _answer.Text);
                try
                {
                    string result = await _ai.AnswerAsync(useCloud, aiEndpoint, aiModel,
                        question, context, _lifetime.Token);
                    if (_disposed) break;
                    _answer.Text = result;
                    UpdateOverlay(question, result);
                    _status.Text = "AI answered a question";
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_disposed) break;
                    string advice = useCloud
                        ? "For cloud AI, check your API key, cloud model and internet connection."
                        : "For local AI, make sure Ollama is running and the model is installed.";
                    string error = "AI error: " + ex.Message + Environment.NewLine + advice;
                    _answer.Text = error;
                    UpdateOverlay(question, error);
                    _status.Text = "AI unavailable";
                }
            }
        }
        finally
        {
            _answering = false;
        }
    }

    private void UpdateOverlay(string question, string content)
    {
        _overlay.SetAnswer(question, content);
        if (_overlayEnabled.Checked && !_overlay.Visible) _overlay.Show();
    }

    private void ShowOverlay()
    {
        if (_overlay.IsDisposed) return;
        if (!_overlay.Visible) _overlay.Show();
        else _overlay.BringToFront();
    }

    private void UpdateUi()
    {
        bool busy = _capture.IsActive || _starting;
        bool app = SelectedSource == CaptureSource.SelectedApplication;
        bool pc = SelectedSource == CaptureSource.EntirePc;
        bool mic = NeedsMicrophone;
        _source.Enabled = !busy;
        _output.Enabled = !busy && pc;
        _application.Enabled = !busy && app;
        _microphone.Enabled = !busy && mic;
        _includeMic.Enabled = !busy && SelectedSource != CaptureSource.MicrophoneOnly;
        _speech.Enabled = !busy;
        _saveAudio.Enabled = !busy;
        _saveTranscript.Enabled = !busy && _speech.Checked;
        _modelBrowse.Enabled = !busy && _speech.Checked;
        _refresh.Enabled = !busy;
        _browse.Enabled = !busy;
        bool hasSource = (!pc || _output.SelectedItem is DeviceItem)
            && (!app || _application.SelectedItem is ProcessItem)
            && (!mic || _microphone.SelectedItem is DeviceItem);
        _start.Enabled = !busy && hasSource;
        _stop.Enabled = _capture.IsActive && !_capture.IsStopping;
        _trayListen.Enabled = _start.Enabled;
        _trayStop.Enabled = _stop.Enabled;
        _tray.Text = busy ? "PC Q&A Assistant - listening" : "PC Q&A Assistant";
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_folder.Text);
            Process.Start(new ProcessStartInfo(_folder.Text) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open saved files", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void HideToTray()
    {
        Hide();
        if (_firstHide)
        {
            _firstHide = false;
            _tray.ShowBalloonTip(2200, "PC Q&A Assistant",
                "Listening continues in the tray. Use the tray icon to reopen or stop.",
                ToolTipIcon.Info);
        }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exiting = true;
        if (_capture.IsActive)
        {
            _ = StopListeningAsync();
            return;
        }
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_starting)
        {
            e.Cancel = true;
            _exiting = true;
            return;
        }
        if (!_exiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        if (_capture.IsActive)
        {
            e.Cancel = true;
            _ = StopListeningAsync();
            return;
        }
        if (_disposed) return;
        StoreSettings();
        _disposed = true;
        _lifetime.Cancel();
        _screenAssistant?.Close();
        _speechDebounce.Stop();
        _speechDebounce.Dispose();
        _clock.Stop();
        _clock.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _overlay.Shutdown();
        _ai.Dispose();
        _lifetime.Dispose();
        foreach (var device in _ownedDevices) device.Dispose();
        _devices.Dispose();
    }

    private sealed record DeviceItem(MMDevice Device)
    {
        public override string ToString() => Device.FriendlyName;
    }
    private sealed record ProcessItem(int Id, string Name, string WindowTitle)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(WindowTitle)
            ? $"{Name} (PID {Id})" : $"{Name} — {WindowTitle} (PID {Id})";
    }
}
