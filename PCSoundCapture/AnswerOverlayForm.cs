namespace PCSoundCapture;

/// <summary>Small, draggable, always-on-top answer window. No recording UI.</summary>
internal sealed class AnswerOverlayForm : Form
{
    private readonly Label _question = new()
    {
        Dock = DockStyle.Fill,
        Text = "Waiting for a question...",
        Font = new Font("Segoe UI", 10, FontStyle.Bold),
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly TextBox _answer = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Segoe UI", 10),
        BorderStyle = BorderStyle.None,
        BackColor = SystemColors.Window,
        Text = "Start listening, or type a question in the main window."
    };
    private bool _reallyClose;
    protected override bool ShowWithoutActivation => true;

    public AnswerOverlayForm()
    {
        Text = "AI Q&A · Answer";
        Font = new Font("Segoe UI", 9);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        TopMost = true;
        ShowInTaskbar = false;
        MinimumSize = new Size(360, 190);
        Size = new Size(465, 270);
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 800);
        Location = new Point(Math.Max(area.Left, area.Right - Width - 22),
            Math.Max(area.Top, area.Bottom - Height - 22));
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(12),
            ColumnCount = 1, RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.Controls.Add(_question, 0, 0);
        layout.Controls.Add(_answer, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var copy = new Button { Text = "Copy", AutoSize = true };
        copy.Click += (_, _) => { if (_answer.TextLength > 0) Clipboard.SetText(_answer.Text); };
        var hide = new Button { Text = "Hide", AutoSize = true };
        hide.Click += (_, _) => Hide();
        buttons.Controls.Add(hide);
        buttons.Controls.Add(copy);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
    }

    public void SetAnswer(string question, string answer)
    {
        _question.Text = question;
        _answer.Text = answer;
        _answer.SelectionStart = 0;
        _answer.ScrollToCaret();
    }

    public void Shutdown()
    {
        _reallyClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
