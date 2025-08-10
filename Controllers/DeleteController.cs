// using Microsoft.AspNetCore.Mvc;
// using DicomWebFsServer.Services;

// namespace DicomWebFsServer.Controllers
// {
//   [ApiController]
//   [Route("dicomweb/delete")] // Or use [Route("dicomweb/studies")] and [HttpDelete("{studyUid}")]
//   public class DeleteController : ControllerBase
//   {
//     private readonly DicomFileService _dicomService;

//     public DeleteController(DicomFileService dicomService)
//     {
//       _dicomService = dicomService;
//     }

//     // DELETE Study
//     // DELETE /dicomweb/delete/studies/{studyUid}
//     [HttpDelete("studies/{studyUid}")]
//     public IActionResult DeleteStudy(string studyUid)
//     {
//       try
//       {
//         bool deleted = _dicomService.DeleteStudy(studyUid);
//         if (deleted)
//         {
//           // 200 OK is common for successful deletion in DICOMweb
//           // 204 No Content is also acceptable
//           return Ok(new { message = $"Study {studyUid} deleted successfully." });
//         }
//         else
//         {
//           // 404 Not Found if the study doesn't exist
//           return NotFound(new { error = $"Study {studyUid} not found." });
//         }
//       }
//       catch (Exception ex)
//       {
//         Console.WriteLine($"Error deleting study {studyUid}: {ex.Message}");
//         // 500 Internal Server Error for unexpected issues
//         return StatusCode(500, new { error = "Failed to delete study", message = ex.Message });
//       }
//     }

//     // TODO: Add DeleteSeries and DeleteInstance endpoints if needed
//     // [HttpDelete("studies/{studyUid}/series/{seriesUid}")]
//     // public IActionResult DeleteSeries(string studyUid, string seriesUid) { ... }

//     // [HttpDelete("studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}")]
//     // public IActionResult DeleteInstance(string studyUid, string seriesUid, string instanceUid) { ... }
//   }
// }