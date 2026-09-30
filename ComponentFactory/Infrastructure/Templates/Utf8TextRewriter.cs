using System.Text;

namespace ComponentFactory.Infrastructure.Templates;

public sealed class Utf8TextRewriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task RewriteAsync(
        string filePath,
        NameReplacement replacement,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

        if (!TryDecode(bytes, out var original))
        {
            return;
        }

        var updated = replacement.Apply(original);

        if (updated == original)
        {
            return;
        }

        // Decoding retains the BOM character; re-encoding preserves it and line endings.
        await File.WriteAllBytesAsync(filePath, StrictUtf8.GetBytes(updated), cancellationToken);
    }

    private static bool TryDecode(byte[] bytes, out string text)
    {
        text = string.Empty;

        if (bytes.Contains((byte)0))
        {
            return false;
        }

        try
        {
            text = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
