using System.Globalization;
using System.Text;
using TakEngine.Abstractions;
using TakEngine.Core.Board;

namespace TakEngine.Core.Rendering;

/// <summary>
/// Deterministic, invariant SVG renderer for 2.5D Tak pieces, staggered towers, and board elements.
/// Strictly enforces CultureInfo.InvariantCulture to guarantee valid SVG coordinates in all locales.
/// </summary>
public static class SvgTileRenderer
{
    // Flat stone dimensions
    public const double PieceWidth = 28.0;
    public const double PieceHeight = 32.0;

    // Standing wall dimensions (tall barrier)
    public const double WallWidth = 13.0;
    public const double WallHeight = 48.0;

    // Capstone dimensions (tall cylindrical monolith)
    public const double CapstoneWidth = 22.0;
    public const double CapstoneHeight = 34.0;

    // Stagger step per layer in 2.5D perspective (fanning up and right)
    public const double StaggerStepX = 4.0;
    public const double StaggerStepY = -5.5;

    public static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Pt(double x, double y) => $"{F(x)},{F(y)}";

    /// <summary>
    /// Renders an entire piece stack with 2.5D staggered layer visualization.
    /// </summary>
    public static string RenderStackSvg(PieceStack stack, double centerX, double centerY)
    {
        if (stack == null || stack.IsEmpty) return string.Empty;

        var sb = new StringBuilder();
        var pieces = stack.Pieces;
        int count = pieces.Count;

        double baseOffsetX = -((count - 1) * StaggerStepX) / 2.0;
        double baseOffsetY = -((count - 1) * StaggerStepY) / 2.0;

        for (int i = 0; i < count; i++)
        {
            var piece = pieces[i];
            bool isTop = (i == count - 1);
            double layerX = centerX + baseOffsetX + i * StaggerStepX;
            double layerY = centerY + baseOffsetY + i * StaggerStepY;

            sb.Append(RenderPieceSvg(piece, layerX, layerY, isTop));
        }

        // Height badge for stacks of 4 or more
        if (count >= 4)
        {
            double topX = centerX + baseOffsetX + (count - 1) * StaggerStepX;
            double topY = centerY + baseOffsetY + (count - 1) * StaggerStepY;
            double badgeX = topX + (PieceWidth / 2.0) + 3.0;
            double badgeY = topY - (PieceHeight / 2.0) + 8.0;
            sb.Append($"<text x='{F(badgeX)}' y='{F(badgeY)}' fill='#d4a017' font-family=\"'JetBrains Mono', monospace\" font-size='9' font-weight='700'>{count}</text>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders an individual piece at the given coordinate.
    /// </summary>
    public static string RenderPieceSvg(Piece piece, double layerX, double layerY, bool isTop = true)
    {
        var sb = new StringBuilder();
        bool isWhite = piece.Color == PlayerColor.White;
        string topFill = isWhite ? "#ffffff" : "#333333";
        string topStroke = isWhite ? "#ffffff" : "#ffffff";
        string edgeFill = isWhite ? "#b0b0b0" : "#1a1a1a";

        // Non-top pieces are always rendered as flat slabs in the stack
        if (piece.Type == PieceType.Flat || (!isTop && piece.Type != PieceType.Flat))
        {
            double halfW = PieceWidth / 2.0;
            double halfH = PieceHeight / 2.0;

            // 2.5D slab depth extrusion
            sb.Append($"<rect x='{F(layerX - halfW)}' y='{F(layerY - halfH + 3.5)}' width='{F(PieceWidth)}' height='{F(PieceHeight)}' fill='{edgeFill}' />");

            // Flat Stone Top Face
            sb.Append($"<rect x='{F(layerX - halfW)}' y='{F(layerY - halfH)}' width='{F(PieceWidth)}' height='{F(PieceHeight)}' fill='{topFill}' stroke='{topStroke}' stroke-width='1' />");
        }
        else if (piece.Type == PieceType.Standing)
        {
            double halfWW = WallWidth / 2.0;
            double halfWH = WallHeight / 2.0;

            // Ground contact shadow
            sb.Append($"<ellipse cx='{F(layerX)}' cy='{F(layerY + halfWH * 0.4)}' rx='10' ry='3.5' fill='rgba(0,0,0,0.5)' />");

            // 2.5D side edge depth
            sb.Append($"<rect x='{F(layerX - halfWW - 2.0)}' y='{F(layerY - halfWH + 2.0)}' width='2' height='{F(WallHeight - 2.0)}' fill='{edgeFill}' />");

            // Front face
            sb.Append($"<rect x='{F(layerX - halfWW)}' y='{F(layerY - halfWH)}' width='{F(WallWidth)}' height='{F(WallHeight)}' fill='{topFill}' stroke='{topStroke}' stroke-width='1' />");

            // Top bevel showing top facet in 2.5D perspective
            string topFacetFill = isWhite ? "#cbd5e1" : "#555555";
            string p1 = Pt(layerX - halfWW - 2.0, layerY - halfWH + 2.0);
            string p2 = Pt(layerX - halfWW, layerY - halfWH);
            string p3 = Pt(layerX + halfWW, layerY - halfWH);
            string p4 = Pt(layerX + halfWW - 2.0, layerY - halfWH + 2.0);
            sb.Append($"<polygon points='{p1} {p2} {p3} {p4}' fill='{topFacetFill}' />");

            // Vertical center spine for 3D ridge definition
            string spineStroke = isWhite ? "#cbd5e1" : "#666666";
            sb.Append($"<line x1='{F(layerX)}' y1='{F(layerY - halfWH + 2.0)}' x2='{F(layerX)}' y2='{F(layerY + halfWH - 2.0)}' stroke='{spineStroke}' stroke-width='1' />");
        }
        else if (piece.Type == PieceType.Capstone)
        {
            double pillarHalfW = CapstoneWidth / 2.0;
            double topCrownY = layerY - CapstoneHeight * 0.65;
            double bottomBaseY = layerY + CapstoneHeight * 0.35;

            string capBodyFill = isWhite ? "#f1f5f9" : "#262626";
            string capBodyStroke = isWhite ? "#cbd5e1" : "#ffffff";
            string capSideShadow = isWhite ? "#cbd5e1" : "#141414";

            // Ground contact shadow
            sb.Append($"<ellipse cx='{F(layerX)}' cy='{F(bottomBaseY + 3.0)}' rx='14' ry='5' fill='rgba(0,0,0,0.6)' />");

            // Cylinder base curved bottom
            sb.Append($"<ellipse cx='{F(layerX)}' cy='{F(bottomBaseY)}' rx='{F(pillarHalfW)}' ry='5.5' fill='{capSideShadow}' />");

            // Cylinder Pillar Trunk
            sb.Append($"<rect x='{F(layerX - pillarHalfW)}' y='{F(topCrownY)}' width='{F(CapstoneWidth)}' height='{F(bottomBaseY - topCrownY)}' fill='{capBodyFill}' stroke='{capBodyStroke}' stroke-width='1' />");

            // 2.5D Shading facet on left side of cylinder
            sb.Append($"<rect x='{F(layerX - pillarHalfW)}' y='{F(topCrownY)}' width='4.5' height='{F(bottomBaseY - topCrownY)}' fill='{capSideShadow}' opacity='0.6' />");

            // Ornate Top Crown Rim in amber #d4a017
            string crownCapFill = isWhite ? "#ffffff" : "#1a1a1a";
            sb.Append($"<ellipse cx='{F(layerX)}' cy='{F(topCrownY)}' rx='{F(pillarHalfW)}' ry='6.5' fill='{crownCapFill}' stroke='#d4a017' stroke-width='2.5' />");

            // Inner Crown Ring
            sb.Append($"<ellipse cx='{F(layerX)}' cy='{F(topCrownY)}' rx='{F(pillarHalfW * 0.55)}' ry='3.5' fill='none' stroke='#d4a017' stroke-width='1.2' />");

            // Crown Finial / Diamond Accent on Top
            string d1 = Pt(layerX, topCrownY - 8.0);
            string d2 = Pt(layerX + 3.5, topCrownY - 3.0);
            string d3 = Pt(layerX, topCrownY);
            string d4 = Pt(layerX - 3.5, topCrownY - 3.0);
            sb.Append($"<polygon points='{d1} {d2} {d3} {d4}' fill='#d4a017' stroke='#d4a017' stroke-width='0.8' />");
        }

        return sb.ToString();
    }
}
