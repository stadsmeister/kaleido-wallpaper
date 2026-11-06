using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace KaleidoWallpaper;

internal static class ImageUtil
{
    private static readonly ImageCodecInfo? Jpeg =
        ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

    /// <summary>Centre-crops to a square (some players report 16:9 thumbnails), scales to 512 px, returns a JPEG data URL.</summary>
    public static string? ToJpegDataUrl(byte[] data)
    {
        try
        {
            using var source = Image.FromStream(new MemoryStream(data));
            int side = Math.Min(source.Width, source.Height);
            var crop = new Rectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side);
            using var square = new Bitmap(512, 512);
            using (var g = Graphics.FromImage(square))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, 512, 512), crop, GraphicsUnit.Pixel);
            }
            using var output = new MemoryStream();
            if (Jpeg != null)
            {
                using var p = new EncoderParameters(1);
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                square.Save(output, Jpeg, p);
            }
            else
            {
                square.Save(output, ImageFormat.Jpeg);
            }
            return "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray());
        }
        catch (Exception)
        {
            return null;
        }
    }
}
