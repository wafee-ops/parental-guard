#r "nuget: Microsoft.Windows.Compatibility, 8.0.0"

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var dir = @"c:\Users\wafee\Documents\codex-projects\parental-guard\src\ParentalGuard.UI\Icons";
Directory.CreateDirectory(dir);

var accent = (Color)ColorConverter.ConvertFromString("#3FD0AE");
var accentBrush = new SolidColorBrush(accent);

var sizes = new[] { 16, 32 };

foreach (var size in sizes)
{
    var path = new PathFigure
    {
        StartPoint = new Point(12, 6),
        IsClosed = true
    };
    path.Segments.Add(new LineSegment(new Point(28, 6), true));
    path.Segments.Add(new LineSegment(new Point(28, 16), true));
    path.Segments.Add(new LineSegment(new Point(24, 28), true));
    path.Segments.Add(new LineSegment(new Point(20, 30), true));
    path.Segments.Add(new LineSegment(new Point(16, 28), true));
    path.Segments.Add(new LineSegment(new Point(12, 16), true));

    var geo = new PathGeometry(new[] { path });
    geo.Freeze();

    var bounds = geo.Bounds;
    var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);

    var dv = new DrawingVisual();
    using (var ctx = dv.RenderOpen())
    {
        var scaleX = (double)size / bounds.Width;
        var scaleY = (double)size / bounds.Height;
        var scale = Math.Min(scaleX, scaleY);
        ctx.PushTransform(new ScaleTransform(scale, scale));
        ctx.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
        ctx.DrawGeometry(accentBrush, null, geo);
    }

    bmp.Render(dv);

    var pngPath = Path.Combine(dir, $"icon{size}.png");
    using (var stream = new FileStream(pngPath, FileMode.Create))
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        encoder.Save(stream);
    }

    Console.WriteLine($"Created {pngPath}");
}

var icon16Path = Path.Combine(dir, "icon16.png");
var icon32Path = Path.Combine(dir, "icon32.png");
var icoPath = Path.Combine(dir, "app.ico");

using (var fs = new FileStream(icoPath, FileMode.Create))
using (var w = new BinaryWriter(fs))
{
    w.Write((short)0);
    w.Write((short)1);
    w.Write((short)2);

    var png16 = File.ReadAllBytes(icon16Path);
    var png32 = File.ReadAllBytes(icon32Path);

    w.Write((byte)16);
    w.Write((byte)16);
    w.Write((byte)0);
    w.Write((byte)0);
    w.Write((short)1);
    w.Write((short)32);
    w.Write((int)png16.Length);
    w.Write((int)(6 + 16 + png32.Length));

    w.Write((byte)32);
    w.Write((byte)32);
    w.Write((byte)0);
    w.Write((byte)0);
    w.Write((short)1);
    w.Write((short)32);
    w.Write((int)png32.Length);
    w.Write((int)(6 + 16));

    w.Write(png32);
    w.Write(png16);
}

Console.WriteLine($"Created {icoPath}");
Console.WriteLine("Done!");
