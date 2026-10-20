using System.Buffers.Binary;
using SkiaSharp;

namespace NexFlow.API.Uploads;

internal sealed record ValidatedImage(byte[] Bytes, string Extension)
{
    internal const int MaxBytes = 5 * 1024 * 1024;
    private const long MaxPixels = 16_000_000;
    // CPU/memory admission only; this is not a business lease or durable work queue.
    private static readonly SemaphoreSlim DecodeSlots = new(2, 2);

    public static async Task<ValidatedImage> ReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > MaxBytes) throw new InvalidDataException("La imagen debe tener entre 1 byte y 5 MB.");
        using var source = file.OpenReadStream();
        using var input = new LimitedBuffer();
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) != 0)
            await input.WriteAsync(buffer.AsMemory(0, read), ct);
        if (input.Length != file.Length) throw new InvalidDataException("La imagen está incompleta.");
        var bytes = input.ToArray();
        var format = Envelope(bytes);
        var mime = file.ContentType?.Trim().ToLowerInvariant();
        var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
        if ((format == "PNG" && (mime != "image/png" || ext != ".png"))
            || (format == "JPEG" && (mime != "image/jpeg" || ext is not (".jpg" or ".jpeg")))
            || (format == "WEBP" && (mime != "image/webp" || ext != ".webp")))
            throw new InvalidDataException("El contenido de la imagen no coincide con su formato, extensión o MIME.");

        await DecodeSlots.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("No se puede decodificar la imagen.");
            var actual = codec.EncodedFormat;
            var expected = format switch { "PNG" => SKEncodedImageFormat.Png, "JPEG" => SKEncodedImageFormat.Jpeg, _ => SKEncodedImageFormat.Webp };
            var info = codec.Info;
            if (actual != expected || info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels
                || codec.FrameCount > 1)
                throw new InvalidDataException("Usa una imagen estática de hasta 16 megapíxeles.");
            var target = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var pixels = new SKBitmap(target);
            if (pixels.GetPixels() == IntPtr.Zero || codec.GetPixels(target, pixels.GetPixels()) != SKCodecResult.Success)
                throw new InvalidDataException("La imagen contiene datos corruptos o incompletos.");
            ct.ThrowIfCancellationRequested();
            using var image = SKImage.FromBitmap(pixels);
            using var encoded = image.Encode(expected, 90) ?? throw new InvalidDataException("No se pudo normalizar la imagen.");
            if (encoded.Size > MaxBytes) throw new InvalidDataException("La imagen normalizada supera 5 MB.");
            // Only re-encoded pixels leave this boundary; input metadata/suffixes do not.
            return new(encoded.ToArray(), format switch { "PNG" => ".png", "JPEG" => ".jpg", _ => ".webp" });
        }
        finally { DecodeSlots.Release(); }
    }

    private static string Envelope(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 20 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && bytes[^12..].SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 })) return "PNG";
        if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[^2] == 255 && bytes[^1] == 217) return "JPEG";
        if (bytes.Length >= 20 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) == bytes.Length - 8) return "WEBP";
        throw new InvalidDataException("Imagen inválida, truncada o formato no permitido. Usa JPG, PNG o WebP.");
    }

    private sealed class LimitedBuffer : MemoryStream
    {
        private void Check(int count) { if (Position > MaxBytes - count) throw new InvalidDataException("La imagen supera el límite de 5 MB."); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        { Check(count); return base.WriteAsync(buffer, offset, count, ct); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { Check(buffer.Length); return base.WriteAsync(buffer, ct); }
    }
}
