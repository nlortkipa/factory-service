using System.Drawing.Imaging;
using System.IO.Ports;
using FactoryService.Common;
using QRCoder;

namespace FactoryService.Companion;

internal static class TurmoPanelDisplay
{
    private const int PanelWidth = 320;
    private const int PanelHeight = 480;
    private const int BaudRate = 115200;
    private const string TurmoPortName = "COM3";

    public static Task ShowQrAsync(AgentQrResponse response) => SendAsync(() => RenderQr(response));

    public static Task ShowUnavailableAsync(string detail) => SendAsync(() => RenderUnavailable(detail));

    private static Task SendAsync(Func<Bitmap> render) => Task.Run(() =>
    {
        try
        {
            using Bitmap image = render();
            Send(image);
        }
        catch (Exception exception) when (exception is not TurmoPanelException)
        {
            throw new TurmoPanelException($"Could not update the Turmo panel on {TurmoPortName}: {exception.Message}", exception);
        }
    });

    private static Bitmap RenderQr(AgentQrResponse response)
    {
        var image = CreateCanvas();
        using Graphics graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);

        DrawHeader(graphics);

        byte[] qrBytes = PngByteQRCodeHelper.GetQRCode(response.SurveyUrl, QRCodeGenerator.ECCLevel.Q, 14);
        using var stream = new MemoryStream(qrBytes);
        using var qrSource = new Bitmap(stream);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(qrSource, new Rectangle(31, 102, 258, 258));

        using var promptFont = new Font("Sylfaen", 15, FontStyle.Bold);
        using var contextFont = new Font("Sylfaen", 10);
        using var operatorFont = new Font("Sylfaen", 10, FontStyle.Bold);
        using var plantFont = new Font("Sylfaen", 8);
        DrawCentered(graphics, "დაასკანერეთ QR კოდი", promptFont, Color.FromArgb(25, 53, 81), 367, 25);
        DrawCentered(graphics, "და შეაფასეთ მომსახურება", contextFont, Color.FromArgb(105, 118, 128), 395, 20);

        using var operatorBackground = new SolidBrush(Color.FromArgb(232, 246, 238));
        graphics.FillRectangle(operatorBackground, 18, 419, 284, 31);
        DrawCentered(graphics, $"ოპერატორი: {response.OperatorName}", operatorFont, Color.FromArgb(0, 125, 56), 425, 20);
        DrawCentered(graphics, response.PlantName, plantFont, Color.FromArgb(105, 118, 128), 456, 18);
        return image;
    }

    private static Bitmap RenderUnavailable(string detail)
    {
        var image = CreateCanvas();
        using Graphics graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        DrawHeader(graphics);

        using var pen = new Pen(Color.FromArgb(190, 205, 213), 9);
        graphics.DrawEllipse(pen, 85, 137, 150, 150);
        graphics.DrawLine(pen, 112, 164, 208, 260);

        using var titleFont = new Font("Sylfaen", 14, FontStyle.Bold);
        using var detailFont = new Font("Sylfaen", 9);
        using var helpdeskFont = new Font("Segoe UI", 10, FontStyle.Bold);
        DrawCentered(graphics, "შეფასება დროებით მიუწვდომელია", titleFont, Color.FromArgb(25, 53, 81), 320, 48);
        DrawCentered(graphics, detail, detailFont, Color.FromArgb(105, 118, 128), 374, 48);

        using var helpdeskBackground = new SolidBrush(Color.FromArgb(232, 246, 238));
        graphics.FillRectangle(helpdeskBackground, 18, 431, 284, 31);
        DrawCentered(graphics, "HELPDESK", helpdeskFont, Color.FromArgb(0, 125, 56), 437, 20);
        return image;
    }

    private static Bitmap CreateCanvas() => new(PanelWidth, PanelHeight, PixelFormat.Format24bppRgb);

    private static void DrawHeader(Graphics graphics)
    {
        using var background = new SolidBrush(Color.FromArgb(0, 156, 69));
        graphics.FillRectangle(background, 0, 0, PanelWidth, 88);
        using var brandFont = new Font("Segoe UI", 16, FontStyle.Bold);
        using var subtitleFont = new Font("Sylfaen", 10);
        DrawCentered(graphics, "KARTULI CEMENT", brandFont, Color.White, 18, 27);
        DrawCentered(graphics, "მომსახურების შეფასება", subtitleFont, Color.White, 54, 22);
    }

    private static void DrawCentered(Graphics graphics, string text, Font font, Color color, float y, float height)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(text, font, brush, new RectangleF(12, y, PanelWidth - 24, height), format);
    }

    private static void Send(Bitmap image)
    {
        string[] ports = SerialPort.GetPortNames();
        string? portName = ports.FirstOrDefault(name => name.Equals(TurmoPortName, StringComparison.OrdinalIgnoreCase));
        if (portName is null)
            throw new InvalidOperationException($"Turmo panel was not found on {TurmoPortName}. Check that USB35INCHIPSV2 is connected.");

        using var port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
        {
            RtsEnable = true,
            WriteTimeout = 5000
        };

        try
        {
            port.Open();
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                $"Cannot open {portName}. Close the official TURMO app or any other program using the panel, then reconnect it and try again.",
                exception);
        }

        byte[] pixels = ToRgb565LittleEndian(image);
        SendCommand(port, command: 197, x: 0, y: 0, endX: PanelWidth - 1, endY: PanelHeight - 1);
        for (int offset = 0; offset < pixels.Length; offset += PanelWidth * 8)
        {
            int count = Math.Min(PanelWidth * 8, pixels.Length - offset);
            port.Write(pixels, offset, count);
        }
    }

    private static void SendCommand(SerialPort port, byte command, int x, int y, int endX, int endY)
    {
        byte[] packet =
        [
            (byte)(x >> 2),
            (byte)(((x & 3) << 6) | (y >> 4)),
            (byte)(((y & 15) << 4) | (endX >> 6)),
            (byte)(((endX & 63) << 2) | (endY >> 8)),
            (byte)endY,
            command
        ];
        port.Write(packet, 0, packet.Length);
    }

    private static byte[] ToRgb565LittleEndian(Bitmap image)
    {
        var result = new byte[PanelWidth * PanelHeight * 2];
        int index = 0;
        for (int y = 0; y < PanelHeight; y++)
        for (int x = 0; x < PanelWidth; x++)
        {
            Color color = image.GetPixel(x, y);
            var value = (ushort)(((color.R >> 3) << 11) | ((color.G >> 2) << 5) | (color.B >> 3));
            result[index++] = (byte)value;
            result[index++] = (byte)(value >> 8);
        }
        return result;
    }
}

internal sealed class TurmoPanelException(string message, Exception innerException) : Exception(message, innerException);
