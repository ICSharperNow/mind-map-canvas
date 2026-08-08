using System.Globalization;
using System.IO;
using System.Text;

namespace MindMapCanvas;

/// <summary>
/// Minimal single-page PDF writer that embeds one JPEG image (DCTDecode),
/// sized so the page matches the image at 96 DPI.
/// </summary>
public static class PdfWriter
{
    public static void WriteImagePdf(string path, byte[] jpeg, int pxW, int pxH)
    {
        double ptW = pxW * 72.0 / 96.0;
        double ptH = pxH * 72.0 / 96.0;

        using var ms = new MemoryStream();
        var offsets = new long[6];

        void W(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        W("%PDF-1.4\n");

        offsets[1] = ms.Position;
        W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = ms.Position;
        W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = ms.Position;
        W($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(ptW)} {F(ptH)}] " +
          "/Resources << /XObject << /Im0 4 0 R >> /ProcSet [/PDF /ImageC] >> " +
          "/Contents 5 0 R >>\nendobj\n");

        offsets[4] = ms.Position;
        W($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pxW} /Height {pxH} " +
          $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        ms.Write(jpeg, 0, jpeg.Length);
        W("\nendstream\nendobj\n");

        var content = $"q\n{F(ptW)} 0 0 {F(ptH)} 0 0 cm\n/Im0 Do\nQ\n";
        offsets[5] = ms.Position;
        W($"5 0 obj\n<< /Length {content.Length} >>\nstream\n{content}endstream\nendobj\n");

        long xref = ms.Position;
        W("xref\n0 6\n0000000000 65535 f \n");
        for (int i = 1; i <= 5; i++)
            W($"{offsets[i]:D10} 00000 n \n");
        W($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");

        File.WriteAllBytes(path, ms.ToArray());
    }

    static string F(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
