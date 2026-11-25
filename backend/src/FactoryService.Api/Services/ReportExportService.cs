using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FactoryService.Api.Domain;

namespace FactoryService.Api.Services;

public sealed record ReportRow(DateTimeOffset SubmittedAt, string CarPlate, string Plant, string Operator, int LoadingScore, int SalesScore, int ScaleScore, string Comment);

public static class ReportExportService
{
    public static byte[] Csv(IReadOnlyList<ReportRow> rows)
    {
        var sb = new StringBuilder("თარიღი,სახელმწიფო ნომერი,ქარხანა,ოპერატორი,დატვირთვის მომსახურება,გაყიდვების ოპერატორი,სასწორის მომსასხურება,კომენტარი\r\n");
        foreach (ReportRow row in rows)
        {
            sb.Append(CsvCell(row.SubmittedAt.ToString("O"))).Append(',').Append(CsvCell(row.CarPlate)).Append(',').Append(CsvCell(row.Plant)).Append(',')
              .Append(CsvCell(row.Operator)).Append(',')
              .Append(row.LoadingScore).Append(',').Append(row.SalesScore).Append(',').Append(row.ScaleScore).Append(',')
              .Append(CsvCell(row.Comment)).Append("\r\n");
        }
        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }

    public static byte[] Xlsx(IReadOnlyList<ReportRow> rows)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.AddWorksheet("Ratings");
        string[] headers = ["თარიღი", "სახელმწიფო ნომერი", "ქარხანა", "ოპერატორი", "დატვირთვის მომსახურება", "გაყიდვების ოპერატორი", "სასწორის მომსასხურება", "კომენტარი"];
        for (int i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        for (int i = 0; i < rows.Count; i++)
        {
            ReportRow row = rows[i]; int r = i + 2;
            sheet.Cell(r, 1).Value = row.SubmittedAt.LocalDateTime; sheet.Cell(r, 2).Value = row.CarPlate;
            sheet.Cell(r, 3).Value = row.Plant; sheet.Cell(r, 4).Value = row.Operator;
            sheet.Cell(r, 5).Value = row.LoadingScore; sheet.Cell(r, 6).Value = row.SalesScore;
            sheet.Cell(r, 7).Value = row.ScaleScore; sheet.Cell(r, 8).Value = row.Comment;
        }
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#193551");
        sheet.Row(1).Style.Font.FontColor = XLColor.White; sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents(10, 42); sheet.RangeUsed()?.SetAutoFilter();
        using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray();
    }

    public static byte[] Pdf(IReadOnlyList<ReportRow> rows)
    {
        // Lightweight standards-compliant PDF for dependency-free server exports. The portal UI remains fully Georgian;
        // this tabular export uses ASCII labels so it renders with the PDF core Helvetica font everywhere.
        var lines = new List<string> { "KARTULI CEMENT - SERVICE EVALUATION REPORT", $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}", "" };
        lines.Add("DATE       | CAR PLATE  | PLANT              | OPERATOR           | LOAD | SALES | SCALE");
        lines.AddRange(rows.Take(45).Select(x => $"{x.SubmittedAt:yyyy-MM-dd} | {Ascii(x.CarPlate),-10} | {Ascii(x.Plant),-18} | {Ascii(x.Operator),-18} | {x.LoadingScore,4} | {x.SalesScore,5} | {x.ScaleScore,5}"));
        if (rows.Count > 46) lines.Add($"... {rows.Count - 46} more rows. Use XLSX or CSV for the complete dataset.");
        string content = "BT /F1 10 Tf 46 795 Td 14 TL " + string.Join(" Tj T* ", lines.Select(x => $"({Escape(x)})")) + " Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        using var stream = new MemoryStream(); using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, true) { NewLine = "\n" };
        writer.WriteLine("%PDF-1.4"); writer.Flush(); var offsets = new List<long> { 0 };
        for (int i = 0; i < objects.Length; i++) { offsets.Add(stream.Position); writer.WriteLine($"{i + 1} 0 obj"); writer.WriteLine(objects[i]); writer.WriteLine("endobj"); writer.Flush(); }
        long xref = stream.Position; writer.WriteLine("xref"); writer.WriteLine($"0 {objects.Length + 1}"); writer.WriteLine("0000000000 65535 f ");
        foreach (long offset in offsets.Skip(1)) writer.WriteLine($"{offset:0000000000} 00000 n ");
        writer.WriteLine($"trailer << /Size {objects.Length + 1} /Root 1 0 R >>"); writer.WriteLine("startxref"); writer.WriteLine(xref.ToString(CultureInfo.InvariantCulture)); writer.WriteLine("%%EOF"); writer.Flush();
        return stream.ToArray();
    }

    private static string CsvCell(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string Ascii(string value) => string.Concat(value.Select(c => c <= 127 ? c : '?'));
}
