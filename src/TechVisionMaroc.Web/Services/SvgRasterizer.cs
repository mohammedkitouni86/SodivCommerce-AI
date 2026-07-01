using SkiaSharp;
using Svg.Skia;
using System.Text;

namespace TechVisionMaroc.Services;

/// <summary>
/// Rasterise une chaîne SVG en PNG (via SkiaSharp). Utilisé à la fois pour servir
/// les images produit (afin que l'image AFFICHÉE soit identique à l'image HACHÉE)
/// et pour la recherche par image.
/// </summary>
public static class SvgRasterizer
{
    public static byte[]? SvgToPng(string svg)
    {
        try
        {
            using var skSvg = new SKSvg();
            using var flux = new MemoryStream(Encoding.UTF8.GetBytes(svg));
            var picture = skSvg.Load(flux);
            if (picture is null) return null;

            var rect = picture.CullRect;
            int w = (int)Math.Ceiling(rect.Width);
            int h = (int)Math.Ceiling(rect.Height);
            if (w <= 0 || h <= 0) { w = 500; h = 500; }

            using var bmp = new SKBitmap(w, h);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawPicture(picture);
                canvas.Flush();
            }
            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data?.ToArray();
        }
        catch { return null; }
    }

    /// <summary>Hash stable et déterministe d'une chaîne (indépendant du démarrage du process).</summary>
    public static int HashStable(string? s)
    {
        unchecked
        {
            int h = 23;
            foreach (var c in s ?? "") h = h * 31 + c;
            return h;
        }
    }
}
