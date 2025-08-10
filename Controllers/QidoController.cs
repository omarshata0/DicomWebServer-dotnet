// Controllers/QidoController.cs
using Microsoft.AspNetCore.Mvc;
using DicomWebFsServer.Services;
using System;
using System.Linq;

namespace DicomWebFsServer.Controllers
{
  [ApiController]
  [Route("dicomweb/studies")]
  public class QidoController : ControllerBase
  {
    private readonly DicomFileService _svc;
    public QidoController(DicomFileService svc) => _svc = svc;

    /* ---------------------------------------------------------------
     *  QIDO-RS – studies
     * ------------------------------------------------------------- */
    [HttpGet]
    [Produces("application/dicom+json")]
    public IActionResult GetStudies([FromQuery] string PatientName = null,
                                    [FromQuery] string PatientID = null,
                                    [FromQuery] string StudyDate = null,
                                    [FromQuery] string StudyInstanceUID = null,
                                    [FromQuery] int limit = 100,
                                    [FromQuery] int offset = 0)
    {
      try
      {
        var q = HttpContext.Request.Query;
        string pn = PatientName ?? q["00100010"].FirstOrDefault();
        string pid = PatientID ?? q["00100020"].FirstOrDefault();
        string sdat = StudyDate ?? q["00080020"].FirstOrDefault();
        string suid = StudyInstanceUID ?? q["0020000D"].FirstOrDefault();

        var list = _svc.GetStudyMetadata(pn, pid, sdat, suid)
                       .Skip(offset).Take(limit).ToList();

        Response.ContentType = "application/dicom+json";
        return Ok(list);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"QIDO studies error: {ex.Message}");
        return StatusCode(500, new { error = "Internal", msg = ex.Message });
      }
    }

    /* ---------------------------------------------------------------
     *  QIDO-RS – series
     * ------------------------------------------------------------- */
    [HttpGet("{studyUid}/series")]
    [Produces("application/dicom+json")]
    public IActionResult GetSeries(string studyUid,
                                   [FromQuery] string Modality = null,
                                   [FromQuery] string SeriesInstanceUID = null)
    {
      try
      {
        var q = HttpContext.Request.Query;
        string mod = Modality ?? q["00080060"].FirstOrDefault();
        string sid = SeriesInstanceUID ?? q["0020000E"].FirstOrDefault();

        var list = _svc.GetSeriesMetadata(studyUid, mod, sid);

        Response.ContentType = "application/dicom+json";
        return Ok(list);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"QIDO series error: {ex.Message}");
        return StatusCode(500, new { error = "Internal", msg = ex.Message });
      }
    }

    /* ---------------------------------------------------------------
     *  QIDO-RS – series metadata
     * ------------------------------------------------------------- */

    [HttpGet("{studyUid}/series/{seriesUid}/metadata")]
    public IActionResult GetSeriesMetadata(string studyUid, string seriesUid)
    {
      var meta = _svc.GetInstanceMetadata(studyUid, seriesUid);
      if (!meta.Any()) return NotFound();
      Response.ContentType = "application/dicom+json";
      return Ok(meta);
    }

  }
}
