using System.Drawing.Imaging;
using System.IO.Ports;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using FactoryService.Common;
using QRCoder;

namespace FactoryService.Companion;

internal sealed class TurmoPanelDisplay : IPanelDisplay
{
    private const int PanelWidth = 320;
    private const int PanelHeight = 480;
    private const int QrSize = 276;
    private const int QrTop = 90;
    private const float DesignDpi = 96f;
    private const int BaudRate = 115200;
    private static readonly string[] TurmoDeviceIdentifiers = ["USB35INCHIPSV2", "TURMO"];

    public Task ShowQrAsync(AgentQrResponse response, Func<bool> canWrite, CancellationToken cancellationToken)
        => SendAsync(() => RenderQr(response), canWrite, cancellationToken);

    public Task ShowUnavailableAsync(string detail, Func<bool> canWrite, CancellationToken cancellationToken)
        => SendAsync(() => RenderUnavailable(detail), canWrite, cancellationToken);

    private static Task SendAsync(Func<Bitmap> render, Func<bool> canWrite, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!canWrite()) return;
            using Mutex mutex = CreatePanelMutex();
            bool acquired = false;
            try
            {
                // Keep acquisition and release on this worker thread. The Global namespace
                // and explicit ACL serialize complete frames across Windows users/sessions.
                try
                {
                    int result = WaitHandle.WaitAny([mutex, cancellationToken.WaitHandle], TimeSpan.FromSeconds(15));
                    if (result == WaitHandle.WaitTimeout) throw new TimeoutException("The Turmo panel is busy.");
                    if (result == 1) throw new OperationCanceledException(cancellationToken);
                    acquired = true;
                }
                catch (AbandonedMutexException) { acquired = true; }
                cancellationToken.ThrowIfCancellationRequested();
                if (!canWrite()) return;
                using Bitmap image = render();
                Send(image, () => !cancellationToken.IsCancellationRequested && canWrite());
                // Finish a serial frame rather than leaving a partial command. If ownership
                // changed during the transfer, erase it before releasing the global mutex;
                // the new owner cannot have published a newer frame while we hold it.
                bool stillAllowed;
                try { stillAllowed = !cancellationToken.IsCancellationRequested && canWrite(); }
                catch { stillAllowed = false; }
                if (!stillAllowed)
                {
                    using Bitmap unavailable = RenderUnavailable("შეფასება დროებით მიუწვდომელია");
                    Send(unavailable, () => true);
                    throw new OperationCanceledException();
                }
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }
        catch (Exception exception) when (exception is not TurmoPanelException and not OperationCanceledException)
        {
            throw new TurmoPanelException($"Could not update the Turmo panel: {exception.Message}", exception);
        }
    });

    internal static Mutex CreatePanelMutex()
    {
        const string name = @"Global\FactoryService.TurmoPanel";
        const MutexRights rights = MutexRights.Synchronize | MutexRights.Modify;
        try { return MutexAcl.OpenExisting(name, rights); }
        catch (WaitHandleCannotBeOpenedException)
        {
            var security = new MutexSecurity();
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                rights, AccessControlType.Allow));
            try { return MutexAcl.Create(false, name, out _, security); }
            catch (UnauthorizedAccessException) { return MutexAcl.OpenExisting(name, rights); }
        }
    }

    internal static Bitmap RenderQr(AgentQrResponse response)
    {
        var image = CreateCanvas();
        using Graphics graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);

        DrawHeader(graphics);

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(response.SurveyUrl, QRCodeGenerator.ECCLevel.Q);
        DrawQr(graphics, qrData);

        using var promptFont = CreateFixedFont("Sylfaen", 15, FontStyle.Bold);
        using var contextFont = CreateFixedFont("Sylfaen", 10);
        using var operatorFont = CreateFixedFont("Sylfaen", 10, FontStyle.Bold);
        using var plantFont = CreateFixedFont("Sylfaen", 9);
        DrawCentered(graphics, "დაასკანერეთ QR კოდი", promptFont, Color.FromArgb(25, 53, 81), 367, 25);
        DrawCentered(graphics, "და შეაფასეთ მომსახურება", contextFont, Color.FromArgb(105, 118, 128), 395, 20);

        using var operatorBackground = new SolidBrush(Color.FromArgb(232, 246, 238));
        graphics.FillRectangle(operatorBackground, 18, 419, 284, 59);
        DrawCentered(graphics, $"ოპერატორი: {response.OperatorName}", operatorFont, Color.FromArgb(0, 125, 56), 425, 20);
        DrawCentered(graphics, $"ქარხანა: {response.PlantName}", plantFont, Color.FromArgb(0, 125, 56), 449, 26);
        return image;
    }

    private static void DrawQr(Graphics graphics, QRCodeData qrData)
    {
        int moduleCount = qrData.ModuleMatrix.Count;
        int left = (PanelWidth - QrSize) / 2;
        using var dark = new SolidBrush(Color.Black);

        // Map module boundaries onto one fixed-size square. Integer division gives
        // each module a hard 3-7 px edge (depending on QR density), while the last
        // boundary always lands at QrSize. This avoids the large size drop caused
        // by flooring one pixels-per-module value for denser/longer URLs.
        for (int row = 0; row < moduleCount; row++)
        {
            int top = QrTop + row * QrSize / moduleCount;
            int bottom = QrTop + (row + 1) * QrSize / moduleCount;
            for (int column = 0; column < moduleCount; column++)
            {
                if (!qrData.ModuleMatrix[row][column]) continue;
                int x = left + column * QrSize / moduleCount;
                int right = left + (column + 1) * QrSize / moduleCount;
                graphics.FillRectangle(dark, x, top, right - x, bottom - top);
            }
        }
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

        using var titleFont = CreateFixedFont("Sylfaen", 14, FontStyle.Bold);
        using var detailFont = CreateFixedFont("Sylfaen", 9);
        using var helpdeskFont = CreateFixedFont("Segoe UI", 10, FontStyle.Bold);
        DrawCentered(graphics, "შეფასება დროებით მიუწვდომელია", titleFont, Color.FromArgb(25, 53, 81), 320, 48);
        DrawCentered(graphics, detail, detailFont, Color.FromArgb(105, 118, 128), 374, 48);

        using var helpdeskBackground = new SolidBrush(Color.FromArgb(232, 246, 238));
        graphics.FillRectangle(helpdeskBackground, 18, 431, 284, 31);
        DrawCentered(graphics, "HELPDESK", helpdeskFont, Color.FromArgb(0, 125, 56), 437, 20);
        return image;
    }

    private static Bitmap CreateCanvas()
    {
        var image = new Bitmap(PanelWidth, PanelHeight, PixelFormat.Format24bppRgb);
        image.SetResolution(DesignDpi, DesignDpi);
        return image;
    }

    internal static Font CreateFixedFont(string familyName, float pointSize, FontStyle style = FontStyle.Regular)
        // Fonts are expressed in pixels so Windows display scaling cannot change the
        // layout of the fixed 320x480 hardware frame. At 96 DPI this preserves the
        // same visual size as the original point-based values.
        => new(familyName, pointSize * DesignDpi / 72f, style, GraphicsUnit.Pixel);

    private static void DrawHeader(Graphics graphics)
    {
        using var background = new SolidBrush(Color.FromArgb(0, 156, 69));
        graphics.FillRectangle(background, 0, 0, PanelWidth, 88);
        using var brandFont = CreateFixedFont("Segoe UI", 16, FontStyle.Bold);
        using var subtitleFont = CreateFixedFont("Sylfaen", 10);
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

    private static void Send(Bitmap image, Func<bool> canWrite)
    {
        string portName = FindTurmoPort();

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
        if (!canWrite()) throw new OperationCanceledException();
        SendCommand(port, command: 197, x: 0, y: 0, endX: PanelWidth - 1, endY: PanelHeight - 1);
        for (int offset = 0; offset < pixels.Length; offset += PanelWidth * 8)
        {
            int count = Math.Min(PanelWidth * 8, pixels.Length - offset);
            port.Write(pixels, offset, count);
        }
    }

    private static string FindTurmoPort()
    {
        string[] ports = SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ports.Length == 0)
            throw new InvalidOperationException("No serial ports were found. Check that USB35INCHIPSV2 is connected.");

        var availablePorts = new HashSet<string>(ports, StringComparer.OrdinalIgnoreCase);
        var matchingPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, Caption, Description, PNPDeviceID FROM Win32_SerialPort");
            using ManagementObjectCollection devices = searcher.Get();
            foreach (ManagementBaseObject device in devices)
            {
                string? deviceId = device["DeviceID"]?.ToString();
                if (deviceId is null || !availablePorts.Contains(deviceId))
                    continue;

                string metadata = string.Join(" ", new[]
                {
                    device["Name"]?.ToString(),
                    device["Caption"]?.ToString(),
                    device["Description"]?.ToString(),
                    device["PNPDeviceID"]?.ToString()
                }.Where(value => !string.IsNullOrWhiteSpace(value)));

                if (TurmoDeviceIdentifiers.Any(identifier =>
                    metadata.Contains(identifier, StringComparison.OrdinalIgnoreCase)))
                {
                    matchingPorts.Add(deviceId);
                }
            }
        }
        catch (Exception exception) when (exception is ManagementException
                                          or UnauthorizedAccessException
                                          or System.Runtime.InteropServices.COMException)
        {
            // A single serial port is still unambiguous if Windows Management
            // Instrumentation is unavailable or does not expose device metadata.
        }

        if (matchingPorts.Count == 1)
            return matchingPorts.Single();

        if (matchingPorts.Count > 1)
            throw new InvalidOperationException(
                $"More than one Turmo panel was found ({string.Join(", ", matchingPorts)}). Disconnect the unused panel and try again.");

        if (ports.Length == 1)
            return ports[0];

        throw new InvalidOperationException(
            $"Could not identify the Turmo panel among these serial ports: {string.Join(", ", ports)}. " +
            "Disconnect unrelated serial devices and try again.");
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
