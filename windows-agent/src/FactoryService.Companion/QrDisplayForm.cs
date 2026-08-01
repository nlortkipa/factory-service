using FactoryService.Common;
using QRCoder;

namespace FactoryService.Companion;

internal sealed class QrDisplayForm : Form
{
    private readonly PictureBox _qrPicture;
    private readonly System.Windows.Forms.Timer? _closeTimer;

    public QrDisplayForm(AgentConfig config)
    {
        Text = "Factory Service QR";
        BackColor = Color.White;
        ForeColor = Color.FromArgb(20, 20, 20);
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = config.QrTopMost;
        KeyPreview = true;

        var title = new Label
        {
            Text = "Scan to continue",
            Dock = DockStyle.Top,
            Height = 90,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 28, FontStyle.Bold),
            BackColor = Color.White
        };

        _qrPicture = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            SizeMode = PictureBoxSizeMode.Zoom,
            Padding = new Padding(48),
            Image = CreateQrImage(config.TestQrPayload)
        };

        var footer = new Label
        {
            Text = "Press Esc to close",
            Dock = DockStyle.Bottom,
            Height = 64,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 15),
            ForeColor = Color.DimGray,
            BackColor = Color.White
        };

        Controls.Add(_qrPicture);
        Controls.Add(title);
        Controls.Add(footer);

        if (config.QrDisplaySeconds > 0)
        {
            _closeTimer = new System.Windows.Forms.Timer
            {
                Interval = checked(config.QrDisplaySeconds * 1000)
            };
            _closeTimer.Tick += (_, _) => Close();
            _closeTimer.Start();
        }
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape)
        {
            Close();
            eventArgs.Handled = true;
        }

        base.OnKeyDown(eventArgs);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closeTimer?.Dispose();
            _qrPicture.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Image CreateQrImage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidDataException("TestQrPayload must not be empty.");
        }

        byte[] png = PngByteQRCodeHelper.GetQRCode(
            payload,
            QRCodeGenerator.ECCLevel.Q,
            20);

        using var stream = new MemoryStream(png);
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }
}
