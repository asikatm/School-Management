using QRCoder;

namespace SchoolManagement.Infrastructure.Documents;

public interface IQrCodeService
{
    byte[] Png(string payload, int pixelsPerModule = 8);
}

public sealed class QrCodeService : IQrCodeService
{
    public byte[] Png(string payload, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
