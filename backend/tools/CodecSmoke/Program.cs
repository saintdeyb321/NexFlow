using System.Runtime.InteropServices;
using SkiaSharp;

// No host startup, configuration, credentials, network or filesystem writes.
try
{
    if (args is not ["--require-arm64"] && args.Length != 0) return 2;
    Console.WriteLine($"Codec smoke: {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}");
    if (args.Length == 1 && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64))
        throw new InvalidOperationException("Linux ARM64 is required.");
    foreach (var format in new[] { SKEncodedImageFormat.Png, SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Webp })
    {
        using var bitmap = new SKBitmap(3, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90) ?? throw new InvalidDataException();
        using var codec = SKCodec.Create(encoded) ?? throw new InvalidDataException();
        var info = new SKImageInfo(3, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var pixels = new SKBitmap(info);
        if (codec.EncodedFormat != format || codec.Info.Width != 3 || codec.Info.Height != 2
            || codec.GetPixels(info, pixels.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException();
        using var normalized = SKImage.FromBitmap(pixels);
        using var output = normalized.Encode(format, 90) ?? throw new InvalidDataException();
        if (output.Size == 0) throw new InvalidDataException();
        Console.WriteLine($"PASS {format}: encode/decode/normalize");
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"FAIL codec smoke: {error.GetType().Name}");
    return 1;
}
