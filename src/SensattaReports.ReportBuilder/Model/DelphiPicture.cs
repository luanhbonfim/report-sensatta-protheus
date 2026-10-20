namespace SensattaReports.ReportBuilder.Model;

/// <summary>
/// Extrai os bytes de imagem de dentro do blob Picture.Data do Delphi.
/// O blob vem embrulhado (ex.: nome da classe 'TJPEGImage' + tamanho +
/// os bytes). Aqui pegamos o trecho JPEG real, de FFD8 ate FFD9.
/// </summary>
public static class DelphiPicture
{
    public static byte[] ExtractJpeg(byte[] blob)
    {
        if (blob.Length == 0) return [];

        var start = IndexOf(blob, 0xFF, 0xD8, 0);
        var end = LastIndexOf(blob, 0xFF, 0xD9);
        if (start >= 0 && end > start)
        {
            var len = end - start + 2;
            var jpeg = new byte[len];
            Array.Copy(blob, start, jpeg, 0, len);
            return jpeg;
        }
        return [];
    }

    private static int IndexOf(byte[] data, byte a, byte b, int from)
    {
        for (var i = from; i < data.Length - 1; i++)
            if (data[i] == a && data[i + 1] == b) return i;
        return -1;
    }

    private static int LastIndexOf(byte[] data, byte a, byte b)
    {
        for (var i = data.Length - 2; i >= 0; i--)
            if (data[i] == a && data[i + 1] == b) return i;
        return -1;
    }
}
