namespace LexisNexis.DocumentIntake_Api.Validation
{
    /// <summary>
    /// Validates file content by checking magic bytes (file signatures).
    /// This prevents a malicious actor from uploading an executable disguised as a PDF.
    /// </summary>
    public class FileContentValidator
    {
        private static readonly Dictionary<string, byte[]> MagicBytes = new()
        {
            ["application/pdf"] = [0x25, 0x50, 0x44, 0x46],  // %PDF
            ["application/msword"] = [0xD0, 0xCF, 0x11, 0xE0], // DOC
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"]
                                 = [0x50, 0x4B, 0x03, 0x04],   // ZIP (DOCX)
            ["text/plain"] = []                           // No magic bytes for plain text
        };

        public bool IsContentTypeValid(IFormFile file, string declaredContentType)
        {
            if (!MagicBytes.TryGetValue(declaredContentType, out var magic))
            {
                return false;  // Unknown content type
            }

            if (magic.Length == 0)
            {
                return true;   // text/plain — no magic byte check
            }

            using var stream = file.OpenReadStream();
            var header = new byte[magic.Length];
            var bytesRead = stream.Read(header, 0, header.Length);

            return bytesRead == magic.Length && header.SequenceEqual(magic);
        }
    }
}
