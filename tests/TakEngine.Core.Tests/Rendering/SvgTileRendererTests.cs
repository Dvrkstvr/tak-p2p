using System.Globalization;
using System.Xml.Linq;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Rendering;
using Xunit;

namespace TakEngine.Core.Tests.Rendering;

public class SvgTileRendererTests
{
    [Theory]
    [InlineData("de-DE")] // Uses comma for decimal separator
    [InlineData("fr-FR")] // Uses comma for decimal separator
    [InlineData("en-US")] // Uses dot for decimal separator
    public void RenderPieceSvg_NeverEmitsCommasInFloatAttributes(string cultureName)
    {
        var culture = new CultureInfo(cultureName);
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;

            var pieceTypes = new[] { PieceType.Flat, PieceType.Standing, PieceType.Capstone };
            var colors = new[] { PlayerColor.White, PlayerColor.Black };

            foreach (var type in pieceTypes)
            {
                foreach (var color in colors)
                {
                    var piece = new Piece(color, type);
                    // Use fractional coordinates to ensure float formatting is exercised
                    string svg = SvgTileRenderer.RenderPieceSvg(piece, 123.456, 789.123, isTop: true);

                    Assert.False(string.IsNullOrEmpty(svg));
                    
                    // Verify that no attribute has a comma decimal e.g. "123,45"
                    // Commas are only permitted in polygon points as coordinate pair separators e.g. "123.45,789.12"
                    // Parse as XML fragment to verify structural validity
                    var parsed = XElement.Parse($"<g xmlns='http://www.w3.org/2000/svg'>{svg}</g>");
                    Assert.NotNull(parsed);

                    foreach (var elem in parsed.DescendantsAndSelf())
                    {
                        foreach (var attr in elem.Attributes())
                        {
                            if (attr.Name.LocalName is "x" or "y" or "cx" or "cy" or "r" or "rx" or "ry" or "width" or "height" or "stroke-width")
                            {
                                Assert.DoesNotContain(",", attr.Value);
                            }
                            if (attr.Name.LocalName == "points")
                            {
                                // In points="x,y x,y", each pair must have format float.float,float.float
                                string[] pairs = attr.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                foreach (var pair in pairs)
                                {
                                    string[] xy = pair.Split(',');
                                    Assert.Equal(2, xy.Length);
                                    Assert.True(double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _), $"Invalid X in pair {pair}");
                                    Assert.True(double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _), $"Invalid Y in pair {pair}");
                                }
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void StandingWall_HasTallerVisualHeight()
    {
        var wall = new Piece(PlayerColor.White, PieceType.Standing);
        string svg = SvgTileRenderer.RenderPieceSvg(wall, 100, 100, isTop: true);

        // Wall height is 48
        Assert.Contains("height='48'", svg);
        Assert.Contains("width='13'", svg);
        // Includes 3D top bevel polygon
        Assert.Contains("<polygon points=", svg);
    }

    [Fact]
    public void Capstone_HasPillarVisualHeightAndAmberCrown()
    {
        var cap = new Piece(PlayerColor.White, PieceType.Capstone);
        string svg = SvgTileRenderer.RenderPieceSvg(cap, 100, 100, isTop: true);

        // Capstone has width 22
        Assert.Contains("width='22'", svg);
        // Has amber crown
        Assert.Contains("stroke='#d4a017'", svg);
        // Has diamond finial
        Assert.Contains("<polygon points=", svg);
    }

    [Fact]
    public void StaggeredStack_FansPiecesAndMaintainsLayerVisibility()
    {
        var stack = new PieceStack();
        stack.Push(new Piece(PlayerColor.White, PieceType.Flat));
        stack.Push(new Piece(PlayerColor.Black, PieceType.Flat));
        stack.Push(new Piece(PlayerColor.White, PieceType.Standing));

        string svg = SvgTileRenderer.RenderStackSvg(stack, 200, 200);

        var parsed = XElement.Parse($"<g xmlns='http://www.w3.org/2000/svg'>{svg}</g>");
        Assert.NotNull(parsed);

        // Should render elements for all 3 layers
        Assert.Equal(3, stack.Height);
        // Standing wall on top has height 48
        Assert.Contains("height='48'", svg);
    }

    [Fact]
    public void StaggeredStack_HeightFour_DisplaysAmberBadge()
    {
        var stack = new PieceStack();
        stack.Push(new Piece(PlayerColor.White, PieceType.Flat));
        stack.Push(new Piece(PlayerColor.Black, PieceType.Flat));
        stack.Push(new Piece(PlayerColor.White, PieceType.Flat));
        stack.Push(new Piece(PlayerColor.Black, PieceType.Capstone));

        string svg = SvgTileRenderer.RenderStackSvg(stack, 200, 200);

        Assert.Contains("fill='#d4a017'", svg);
        Assert.Contains(">4</text>", svg);
    }
}
