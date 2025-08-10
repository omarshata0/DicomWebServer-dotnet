using System.Text;

namespace DicomWebFsServer.Helpers
{
  public static class MultipartHelper
  {
    public static byte[] CreateMultipartResponse(byte[] data, string contentType, string transferSyntax)
    {
      var boundary = Guid.NewGuid().ToString();
      var mediaType = $"{contentType}; transfer-syntax={transferSyntax}";

      using var stream = new MemoryStream();
      using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

      // Write multipart headers
      writer.WriteLine($"--{boundary}");
      writer.WriteLine($"Content-Type: {mediaType}");
      writer.WriteLine($"Content-Length: {data.Length}");
      writer.WriteLine(); // Empty line before content
      writer.Flush();

      // Write binary data
      stream.Write(data, 0, data.Length);

      // Write closing boundary
      writer.WriteLine();
      writer.WriteLine($"--{boundary}--");
      writer.Flush();

      return stream.ToArray();
    }

    public static string GetMultipartContentType(string mediaType, string transferSyntax)
    {
      var boundary = Guid.NewGuid().ToString();
      return $"multipart/related; type=\"{mediaType}; transfer-syntax={transferSyntax}\"; boundary={boundary}";
    }
  }
}