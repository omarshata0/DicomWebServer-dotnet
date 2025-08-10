// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Threading.Tasks;
// using Microsoft.AspNetCore.Mvc;
// using DicomWebFsServer.Services;
// using Dicom;
// using Dicom.Imaging;
// using Dicom.Imaging.Codec;
// using Dicom.IO.Buffer;

// namespace DicomWebFsServer.Controllers
// {

//     [ApiController]
//     [Route("dicomweb/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/bulk")]
//     public class BulkController : ControllerBase
//     {
//         private readonly DicomFileService _svc;
//         public BulkController(DicomFileService svc) => _svc = svc;

//         [HttpGet("7fe00010")]
//         public IActionResult GetPixelData(string studyUid, string seriesUid, string instanceUid)
//         {
//             var dcm = _svc.GetDicomFile(studyUid, seriesUid, instanceUid);
//             if (dcm == null) return NotFound();

//             var pixelElement = dcm.Dataset.GetDicomItem<DicomElement>(DicomTag.PixelData);
//             var bytes = pixelElement.Buffer.Data;
//             return File(bytes, "application/octet-stream");
//         }
//     }


// }