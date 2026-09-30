using System.Windows;
using PhoneMirror.Services;

namespace PhoneMirror.Tests;

public class VideoGeometryTests
{
    [Fact]
    public void ToVideo_SameAspect_Scales()
    {
        Assert.Equal((50, 100), VideoGeometry.ToVideo(200, 400, 100, 200, 100, 200));
        Assert.Equal((0, 0), VideoGeometry.ToVideo(200, 400, 100, 200, 0, 0));
        Assert.Equal((99, 199), VideoGeometry.ToVideo(200, 400, 100, 200, 199.9, 399.9));
        Assert.Null(VideoGeometry.ToVideo(200, 400, 100, 200, 200, 399));
    }

    [Fact]
    public void ToVideo_Pillarbox_BarsAreOutside()
    {
        // Area cuadrada 400×400, video vertical 100×200: escala 2, imagen 200×400 centrada (x de 100 a 300).
        Assert.Null(VideoGeometry.ToVideo(400, 400, 100, 200, 50, 10));
        Assert.Null(VideoGeometry.ToVideo(400, 400, 100, 200, 300, 10));
        Assert.Equal((0, 0), VideoGeometry.ToVideo(400, 400, 100, 200, 100, 0));
        Assert.Equal((99, 199), VideoGeometry.ToVideo(400, 400, 100, 200, 299.9, 399.9));
    }

    [Fact]
    public void ToVideo_Letterbox_BarsAreOutside()
    {
        // Area 400×400, video apaisado 200×100: escala 2, imagen 400×200 (y de 100 a 300).
        Assert.Null(VideoGeometry.ToVideo(400, 400, 200, 100, 10, 99));
        Assert.Null(VideoGeometry.ToVideo(400, 400, 200, 100, 10, 300));
        Assert.Equal((5, 0), VideoGeometry.ToVideo(400, 400, 200, 100, 10, 100));
    }

    [Fact]
    public void ToVideo_VideoBiggerThanTheArea()
    {
        // Video 1080×2400 en un area 540×1200: escala 0,5.
        Assert.Equal((1078, 2398), VideoGeometry.ToVideo(540, 1200, 1080, 2400, 539, 1199));
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 0)]
    [InlineData(0, 0)]
    public void ToVideo_WithoutVideoSize_IsNull(int w, int h) =>
        Assert.Null(VideoGeometry.ToVideo(400, 400, w, h, 10, 10));

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(5, -1)]
    public void ToVideo_NegativePoint_IsNull(double x, double y) =>
        Assert.Null(VideoGeometry.ToVideo(200, 400, 100, 200, x, y));

    [Fact]
    public void Clamp_KeepsInsidePointsAndPinsOutsideOnesToTheEdge()
    {
        Assert.Equal((50, 100), VideoGeometry.Clamp(400, 400, 100, 200, 200, 200));
        Assert.Equal((0, 0), VideoGeometry.Clamp(400, 400, 100, 200, 10, -50));
        Assert.Equal((99, 199), VideoGeometry.Clamp(400, 400, 100, 200, 1000, 1000));
        Assert.Equal((0, 199), VideoGeometry.Clamp(400, 400, 100, 200, -1000, 5000));
    }

    [Fact]
    public void Clamp_OnePixelVideo_AlwaysZero() =>
        Assert.Equal((0, 0), VideoGeometry.Clamp(400, 400, 1, 1, 399, 399));

    private static readonly Rect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void FitWindow_Portrait_FirstTime_ShrinksToTheWorkAreaAndMovesUp()
    {
        // Marco de 20×100; la superficie por defecto (480×800) con un video 1080×2400 no cabe de alto.
        var r = VideoGeometry.FitWindow(1080, 2400, new Size(500, 900), new Size(480, 800), new Size(420, 520), new Point(100, 100), Work);

        Assert.Equal(100, r.Left, 3);
        Assert.Equal(1016, r.Height, 3);           // 1040 - 24 de margen
        Assert.Equal(24, r.Top, 3);                // subida para no salirse por abajo
        Assert.Equal(916 * 1080.0 / 2400 + 20, r.Width, 3);
    }

    [Fact]
    public void FitWindow_Landscape_KeepsTheAreaAndTheMinimumHeight()
    {
        var r = VideoGeometry.FitWindow(2400, 1080, new Size(500, 900), new Size(480, 800), new Size(420, 520), new Point(1500, 900), Work);

        var mirrorWidth = Math.Sqrt(480.0 * 800 * 2400 / 1080);
        Assert.Equal(mirrorWidth + 20, r.Width, 3);
        Assert.Equal(520, r.Height, 3);            // el minimo manda sobre 415 + 100
        Assert.Equal(1920 - r.Width, r.Left, 3);   // no se sale por la derecha
        Assert.Equal(1040 - 520, r.Top, 3);
    }

    [Fact]
    public void FitWindow_BiggerMirror_KeepsItsArea()
    {
        var r = VideoGeometry.FitWindow(1000, 1000, new Size(1000, 1000), new Size(1000, 1000), new Size(420, 520), new Point(0, 0), Work);

        Assert.Equal(new Rect(0, 0, 1000, 1000), r);
    }

    [Fact]
    public void FitWindow_WindowSmallerThanMirror_NoNegativeChrome()
    {
        var r = VideoGeometry.FitWindow(480, 800, new Size(10, 10), new Size(480, 800), new Size(0, 0), new Point(0, 0), Work);

        Assert.Equal(480, r.Width, 3);
        Assert.Equal(800, r.Height, 3);
    }

    [Fact]
    public void FitWindow_SecondMonitorOnTheLeft_StaysInsideIt()
    {
        var work = new Rect(-1920, 0, 1920, 1040);

        var r = VideoGeometry.FitWindow(1080, 2400, new Size(500, 900), new Size(480, 800), new Size(420, 520), new Point(-5000, -50), work);

        Assert.Equal(-1920, r.Left, 3);
        Assert.Equal(0, r.Top, 3);
    }
}
