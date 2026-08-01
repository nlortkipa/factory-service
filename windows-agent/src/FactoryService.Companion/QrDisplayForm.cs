using FactoryService.Common;
using QRCoder;

namespace FactoryService.Companion;

internal sealed class QrDisplayForm : Form
{
    private readonly PictureBox _qrPicture;
    private readonly Label _message;
    private readonly Label _operator;
    private readonly Label _context;
    private readonly System.Windows.Forms.Timer? _closeTimer;

    public QrDisplayForm(AgentConfig config, AgentQrResponse? response, string? unavailableMessage = null)
    {
        Text = "Kartuli Cement — მომსახურების შეფასება";
        BackColor = Color.FromArgb(234, 240, 243);
        ForeColor = Color.FromArgb(25, 53, 81);
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = config.QrTopMost;
        KeyPreview = true;

        var frame = new Panel { Width = 430, Height = 730, BackColor = Color.FromArgb(17, 27, 33), Padding = new Padding(14) };
        var surface = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.White, RowCount = 6, ColumnCount = 1,
            Padding = new Padding(0), Margin = new Padding(0)
        };
        surface.RowStyles.Add(new RowStyle(SizeType.Absolute, 135));
        surface.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        surface.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        surface.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        surface.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        surface.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0, 156, 69), Margin = new Padding(0) };
        header.Controls.Add(new Label { Text = "KARTULI CEMENT", Dock = DockStyle.Top, Height = 72, Padding = new Padding(0, 30, 0, 0), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 21, FontStyle.Bold) });
        header.Controls.Add(new Label { Text = "მომსახურების შეფასება", Dock = DockStyle.Bottom, Height = 55, TextAlign = ContentAlignment.TopCenter, ForeColor = Color.White, Font = new Font("Sylfaen", 16, FontStyle.Regular) });

        _qrPicture = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White, SizeMode = PictureBoxSizeMode.Zoom, Padding = new Padding(52, 28, 52, 18), Margin = new Padding(0) };
        _message = new Label { Text = response is null ? "შეფასება დროებით მიუწვდომელია" : "დაასკანერეთ QR კოდი", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(25, 53, 81), Font = new Font("Sylfaen", response is null ? 16 : 20, FontStyle.Bold), Margin = new Padding(0) };
        _context = new Label { Text = response is null ? unavailableMessage ?? "სცადეთ მოგვიანებით" : "და შეაფასეთ მომსახურება", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(105, 118, 128), Font = new Font("Sylfaen", 13), Margin = new Padding(0) };
        _operator = new Label { Text = response is null ? "HELPDESK" : $"ოპერატორი: {response.OperatorName}", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(0, 125, 56), BackColor = Color.FromArgb(232, 246, 238), Font = new Font("Sylfaen", 14, FontStyle.Bold), Margin = new Padding(45, 8, 45, 8) };
        var footer = new Label { Text = response is null ? "კავშირი აღდგენის პროცესშია" : $"{response.PlantName} · {response.Terminal}", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(105, 118, 128), Font = new Font("Sylfaen", 10), Margin = new Padding(0) };

        if (response is not null) _qrPicture.Image = CreateQrImage(response.SurveyUrl);
        else _qrPicture.Image = CreateUnavailableImage();
        surface.Controls.Add(header, 0, 0); surface.Controls.Add(_qrPicture, 0, 1); surface.Controls.Add(_message, 0, 2);
        surface.Controls.Add(_context, 0, 3); surface.Controls.Add(_operator, 0, 4); surface.Controls.Add(footer, 0, 5);
        frame.Controls.Add(surface);

        var canvas = new Panel { Dock = DockStyle.Fill, BackColor = BackColor };
        canvas.Controls.Add(frame);
        canvas.Resize += (_, _) => frame.Location = new Point(Math.Max(0, (canvas.ClientSize.Width - frame.Width) / 2), Math.Max(0, (canvas.ClientSize.Height - frame.Height) / 2));
        Controls.Add(canvas);

        if (config.QrDisplaySeconds > 0)
        {
            _closeTimer = new System.Windows.Forms.Timer { Interval = checked(config.QrDisplaySeconds * 1000) };
            _closeTimer.Tick += (_, _) => Close(); _closeTimer.Start();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Controls[0] is Panel canvas && canvas.Controls[0] is Panel frame)
            frame.Location = new Point(Math.Max(0, (canvas.ClientSize.Width - frame.Width) / 2), Math.Max(0, (canvas.ClientSize.Height - frame.Height) / 2));
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape) { Close(); eventArgs.Handled = true; }
        base.OnKeyDown(eventArgs);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _closeTimer?.Dispose(); _qrPicture.Image?.Dispose(); }
        base.Dispose(disposing);
    }

    private static Image CreateQrImage(string payload)
    {
        byte[] png = PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.Q, 20);
        using var stream = new MemoryStream(png); using var source = Image.FromStream(stream); return new Bitmap(source);
    }

    private static Image CreateUnavailableImage()
    {
        var bitmap = new Bitmap(260, 260);
        using Graphics graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.White);
        using var pen = new Pen(Color.FromArgb(190, 205, 213), 10); graphics.DrawEllipse(pen, 50, 50, 160, 160);
        graphics.DrawLine(pen, 85, 85, 175, 175);
        return bitmap;
    }
}
