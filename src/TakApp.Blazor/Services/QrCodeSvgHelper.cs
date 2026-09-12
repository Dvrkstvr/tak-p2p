using System;
using Net.Codecrete.QrCodeGenerator;

namespace TakApp.Blazor.Services;

public static class QrCodeSvgHelper
{
    /// <summary>
    /// Generates a clean SVG QR code with a transparent background and custom foreground fill color.
    /// </summary>
    public static string GenerateSvg(string text, string foregroundHex = "#38bdf8", int border = 2)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        // Generates SVG string. By default it uses #000000 on #FFFFFF.
        string rawSvg = qr.ToSvgString(border);

        // Customize colors for sleek dark-mode aesthetics:
        // Replace black foreground with accent color, and remove or customize white background
        string styledSvg = rawSvg
            .Replace("fill=\"#000000\"", $"fill=\"{foregroundHex}\"")
            .Replace("fill=\"#FFFFFF\"", "fill=\"none\"")
            .Replace("fill=\"#ffffff\"", "fill=\"none\"");

        return styledSvg;
    }
}
