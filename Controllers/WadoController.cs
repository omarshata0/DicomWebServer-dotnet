// Controllers/WadoController.cs
using Microsoft.AspNetCore.Mvc;
using Dicom;
using Dicom.Imaging;
using Dicom.Imaging.Codec;
using DicomWebFsServer.Services;
using System;
using System.IO;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace DicomWebFsServer.Controllers
{
  [ApiController]
  [Route("dicomweb/studies")]
  public class WadoController : ControllerBase
  {
    private readonly DicomFileService _svc;
    public WadoController(DicomFileService svc) => _svc = svc;

    /* ================================================================
     *  WADO-RS: rendered thumbnail 
     * ================================================================ */
    [HttpGet("{studyUid}/series/{seriesUid}/instances/{instanceUid}/rendered")]
    [HttpGet("{studyUid}/series/{seriesUid}/instances/{instanceUid}/thumbnail")]
    public IActionResult GetRenderedInstance(string studyUid, string seriesUid, string instanceUid)
    {
      var fp = _svc.GetFilePath(studyUid, seriesUid, instanceUid);
      if (!System.IO.File.Exists(fp)) return NotFound();
      try
      {
        var dcm = DicomFile.Open(fp);

        // --- NEW: Check for Encapsulated PDF ---
        var mediaStorageSOPClassUID = dcm.FileMetaInfo?.MediaStorageSOPClassUID ??
                                      dcm.Dataset.GetSingleValueOrDefault<DicomUID>(DicomTag.SOPClassUID, null);

        if (mediaStorageSOPClassUID != null &&
            (mediaStorageSOPClassUID.UID == DicomUID.EncapsulatedPDFStorage.UID))
        {
          // Handle Encapsulated PDF
          if (dcm.Dataset.Contains(DicomTag.EncapsulatedDocument))
          {
            var encapsulatedDocumentElement = dcm.Dataset.GetDicomItem<DicomElement>(DicomTag.EncapsulatedDocument);
            if (encapsulatedDocumentElement != null && encapsulatedDocumentElement.Count > 0)
            {
              // PDF is stored as OB (Other Byte String)
              var pdfBytes = encapsulatedDocumentElement.Buffer.Data; // Get raw bytes
              return File(pdfBytes, "application/pdf"); // -> NOT octet-stream 
            }
          }
          // If PDF data not found or empty, return an error
          return StatusCode(500, new { error = "PDF data not found or empty in Encapsulated PDF instance." });
        }
        // --- END NEW ---

        // Existing logic for image rendering
        var jpeg = CreateJpegFromPixelData(dcm.Dataset, 0);
        return File(jpeg, "image/jpeg");
      }
      catch (Exception ex)
      {
        // Log the specific error for debugging
        Console.WriteLine($"GetRenderedInstance error for {studyUid}/{seriesUid}/{instanceUid}: {ex}");
        return StatusCode(500, new { error = "Render failed", msg = ex.Message });
      }
    }

    /* ================================================================
     *  WADO-RS: single frame (multipart/related)
     *  (This method is for image frames, not PDFs, so no change needed here for PDFs)
     * ================================================================ */
    [HttpGet("{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameNo}")]
    public IActionResult GetFrame(string studyUid, string seriesUid, string instanceUid, int frameNo)
    {
      var fp = _svc.GetFilePath(studyUid, seriesUid, instanceUid);
      if (!System.IO.File.Exists(fp)) return NotFound();

      try
      {
        var dcm = DicomFile.Open(fp);
        var px = DicomPixelData.Create(dcm.Dataset);
        if (frameNo < 1 || frameNo > px.NumberOfFrames) return BadRequest("Invalid frame");

        byte[] bytes = px.GetFrame(frameNo - 1).Data;
        var tsUid = dcm.FileMetaInfo?
                      .GetSingleValueOrDefault(DicomTag.TransferSyntaxUID,
                                               DicomTransferSyntax.ImplicitVRLittleEndian.UID)
                      .UID ?? DicomTransferSyntax.ImplicitVRLittleEndian.UID.UID;

        string boundary = Guid.NewGuid().ToString("N");
        string outerType = $"multipart/related; type=\"application/octet-stream\"; boundary={boundary}";
        string innerType = $"application/octet-stream; transfer-syntax={tsUid}";
        string location = $"{Request.Scheme}://{Request.Host}{Request.Path.Value}";

        var ms = new MemoryStream();
        WriteMultipartPart(ms, boundary, innerType, bytes, location);
        ms.Write(Encoding.UTF8.GetBytes($"--{boundary}--\r\n"));
        ms.Position = 0;

        Response.Headers["X-Powered-By"] = "DicomWebFsServer";
        Response.Headers["x-content-type-options"] = "nosniff";
        Response.ContentType = outerType;

        return File(ms, outerType);
      }
      catch (Exception ex)
      {
        return StatusCode(500, new { error = "Frame error", msg = ex.Message });
      }
    }


    /* ================================================================
     *  Image helpers
     * ================================================================ */
    private static byte[] CreateJpegFromPixelData(DicomDataset ds, int frameIdx)
    {
      var px = DicomPixelData.Create(ds);
      if (px.Syntax.IsEncapsulated)
        ds = new DicomTranscoder(px.Syntax, DicomTransferSyntax.ExplicitVRLittleEndian).Transcode(ds);

      px = DicomPixelData.Create(ds);
      var fb = px.GetFrame(frameIdx).Data;
      int w = ds.GetSingleValue<int>(DicomTag.Columns);
      int h = ds.GetSingleValue<int>(DicomTag.Rows);
      ushort b = ds.GetSingleValueOrDefault(DicomTag.BitsAllocated, (ushort)16);
      bool inv = ds.GetSingleValueOrDefault(DicomTag.PhotometricInterpretation, "MONOCHROME2") == "MONOCHROME1";

      using var ms = new MemoryStream();
      byte[] rgb = b == 8 ? WindowPixels8(fb, ds, inv) : WindowPixels16(fb, ds, inv);
      // Ensure L8 is the correct pixel format for the data
      Image.LoadPixelData<L8>(rgb, w, h).SaveAsJpeg(ms, new JpegEncoder { Quality = 90 });
      return ms.ToArray();
    }

    private static byte[] WindowPixels8(byte[] src, DicomDataset ds, bool invert)
    {
      double wc = ds.GetSingleValueOrDefault(DicomTag.WindowCenter, 127.5);
      double ww = ds.GetSingleValueOrDefault(DicomTag.WindowWidth, 255.0);
      double lo = wc - ww / 2.0, hi = wc + ww / 2.0, sc = 255.0 / Math.Max(1, hi - lo);
      var dst = new byte[src.Length];
      for (int i = 0; i < src.Length; i++)
      {
        double v = src[i];
        byte p = v <= lo ? (byte)0 : v >= hi ? (byte)255 : (byte)((v - lo) * sc);
        dst[i] = invert ? (byte)(255 - p) : p;
      }
      return dst;
    }

    private static byte[] WindowPixels16(byte[] raw, DicomDataset ds, bool invert)
    {
      int n = raw.Length / 2;
      ushort[] s = new ushort[n];
      Buffer.BlockCopy(raw, 0, s, 0, raw.Length);

      double slope = ds.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0);
      double icpt = ds.GetSingleValueOrDefault(DicomTag.RescaleIntercept, 0.0);
      double wc = ds.GetSingleValueOrDefault(DicomTag.WindowCenter, 2048.0);
      double ww = ds.GetSingleValueOrDefault(DicomTag.WindowWidth, 4096.0);
      double lo = wc - ww / 2.0, hi = wc + ww / 2.0, sc = 255.0 / Math.Max(1, hi - lo);

      var dst = new byte[n];
      for (int i = 0; i < n; i++)
      {
        double v = s[i] * slope + icpt;
        byte p = v <= lo ? (byte)0 : v >= hi ? (byte)255 : (byte)((v - lo) * sc);
        dst[i] = invert ? (byte)(255 - p) : p;
      }
      return dst;
    }

    private static void WriteMultipartPart(Stream body, string boundary,
                                           string ct, byte[] data, string loc)
    {
      var hdr = new StringBuilder()
        .Append("--").Append(boundary).Append("\r\n")
        .Append("Content-Location: ").Append(loc).Append("\r\n")
        .Append("Content-Type: ").Append(ct).Append("\r\n")
        .Append("Content-Length: ").Append(data.Length).Append("\r\n\r\n")
        .ToString();
      var pre = Encoding.UTF8.GetBytes(hdr);
      body.Write(pre, 0, pre.Length);
      body.Write(data, 0, data.Length);
      body.Write(Encoding.UTF8.GetBytes("\r\n"));
    }
  }
}