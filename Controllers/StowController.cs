// using Microsoft.AspNetCore.Mvc;
// using Microsoft.AspNetCore.WebUtilities;
// using Dicom;
// using Dicom.IO;
// using DicomWebFsServer.Services;
// using System.Text;

// namespace DicomWebFsServer.Controllers
// {
//   [ApiController]
//   [Route("dicomweb/studies")]
//   public class StowController : ControllerBase
//   {
//     private readonly DicomFileService _dicomService;
//     public StowController(DicomFileService dicomService)
//     {
//       _dicomService = dicomService;
//     }

//     // STOW-RS: Store instances
//     [HttpPost]
//     [Consumes("multipart/related")]
//     public async Task<IActionResult> Upload()
//     {
//       try
//       {
//         var contentType = Request.ContentType;
//         if (string.IsNullOrEmpty(contentType) || !contentType.Contains("multipart/related"))
//           return BadRequest("Content-Type must be multipart/related");

//         var boundary = ExtractBoundary(contentType);
//         if (string.IsNullOrEmpty(boundary))
//           return BadRequest("Invalid multipart boundary");

//         var reader = new MultipartReader(boundary, Request.Body);
//         var uploadedInstances = new List<object>();
//         bool hasSuccess = false;
//         bool hasFailure = false;

//         MultipartSection section;
//         while ((section = await reader.ReadNextSectionAsync()) != null)
//         {
//           try
//           {
//             using var memoryStream = new MemoryStream();
//             await section.Body.CopyToAsync(memoryStream);
//             memoryStream.Position = 0;

//             var dicomFile = await DicomFile.OpenAsync(memoryStream);
//             bool saveResult = await _dicomService.SaveDicomToFilesystem(dicomFile);

//             var ds = dicomFile.Dataset;
//             if (saveResult)
//             {
//               hasSuccess = true;
//               uploadedInstances.Add(new
//               {
//                 StudyInstanceUID = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, ""),
//                 SeriesInstanceUID = ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, ""),
//                 SOPInstanceUID = ds.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, ""),
//                 Status = "Success"
//               });
//             }
//             else
//             {
//               hasFailure = true;
//               uploadedInstances.Add(new
//               {
//                 Status = "Failed",
//                 Error = "Failed to save DICOM file"
//               });
//             }
//           }
//           catch (Exception ex)
//           {
//             Console.WriteLine($"Error processing DICOM part: {ex.Message}");
//             hasFailure = true;
//             uploadedInstances.Add(new
//             {
//               Status = "Failed",
//               Error = ex.Message
//             });
//           }
//         }

//         // Determine overall status code based on results
//         int statusCode = 200; // Default to OK
//         if (hasSuccess && hasFailure)
//         {
//           statusCode = 202; // Accepted (partial success)
//         }
//         else if (!hasSuccess && hasFailure)
//         {
//           statusCode = 500; // Internal Server Error (complete failure)
//         }
//         // If only success, statusCode remains 200

//         var response = new
//         {
//           Status = statusCode == 200 ? "Success" : (statusCode == 202 ? "Partial Success" : "Failure"),
//           UploadedInstances = uploadedInstances,
//           TotalCount = uploadedInstances.Count
//         };

//         return StatusCode(statusCode, response);
//       }
//       catch (Exception ex)
//       {
//         Console.WriteLine($"Error in STOW upload: {ex.Message}");
//         return StatusCode(500, new { error = "Upload failed", message = ex.Message });
//       }
//     }

//     // STOW-RS: Store instances to specific study
//     [HttpPost("{studyUid}")]
//     [Consumes("multipart/related")]
//     public async Task<IActionResult> UploadToStudy(string studyUid)
//     {
//       try
//       {
//         var contentType = Request.ContentType;
//         if (string.IsNullOrEmpty(contentType) || !contentType.Contains("multipart/related"))
//           return BadRequest("Content-Type must be multipart/related");

//         var boundary = ExtractBoundary(contentType);
//         if (string.IsNullOrEmpty(boundary))
//           return BadRequest("Invalid multipart boundary");

//         var reader = new MultipartReader(boundary, Request.Body);
//         var uploadedInstances = new List<object>();
//         bool hasSuccess = false;
//         bool hasFailure = false;
//         bool studyMismatch = false;

//         MultipartSection section;
//         while ((section = await reader.ReadNextSectionAsync()) != null)
//         {
//           try
//           {
//             using var memoryStream = new MemoryStream();
//             await section.Body.CopyToAsync(memoryStream);
//             memoryStream.Position = 0;

//             var dicomFile = await DicomFile.OpenAsync(memoryStream);
//             var ds = dicomFile.Dataset;
//             var instanceStudyUid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");

//             if (instanceStudyUid != studyUid)
//             {
//               studyMismatch = true;
//               hasFailure = true;
//               uploadedInstances.Add(new
//               {
//                 Status = "Failed",
//                 Error = $"StudyInstanceUID mismatch. Expected: {studyUid}, Found: {instanceStudyUid}"
//               });
//               continue; // Skip saving this instance
//             }

//             bool saveResult = await _dicomService.SaveDicomToFilesystem(dicomFile);

//             if (saveResult)
//             {
//               hasSuccess = true;
//               uploadedInstances.Add(new
//               {
//                 StudyInstanceUID = instanceStudyUid,
//                 SeriesInstanceUID = ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, ""),
//                 SOPInstanceUID = ds.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, ""),
//                 Status = "Success"
//               });
//             }
//             else
//             {
//               hasFailure = true;
//               uploadedInstances.Add(new
//               {
//                 Status = "Failed",
//                 Error = "Failed to save DICOM file"
//               });
//             }
//           }
//           catch (Exception ex)
//           {
//             Console.WriteLine($"Error processing DICOM part for study {studyUid}: {ex.Message}");
//             hasFailure = true;
//             uploadedInstances.Add(new
//             {
//               Status = "Failed",
//               Error = ex.Message
//             });
//           }
//         }

//         // Determine overall status code
//         int statusCode = 200; // Default to OK
//         if (studyMismatch || (hasSuccess && hasFailure))
//         {
//           statusCode = 202; // Accepted (partial success or mismatch)
//         }
//         else if (!hasSuccess && hasFailure)
//         {
//           statusCode = 500; // Internal Server Error (complete failure)
//         }

//         var response = new
//         {
//           Status = statusCode == 200 ? "Success" : (statusCode == 202 ? "Partial Success" : "Failure"),
//           UploadedInstances = uploadedInstances,
//           TotalCount = uploadedInstances.Count
//         };

//         return StatusCode(statusCode, response);
//       }
//       catch (Exception ex)
//       {
//         Console.WriteLine($"Error in STOW upload to study {studyUid}: {ex.Message}");
//         return StatusCode(500, new { error = "Upload failed", message = ex.Message });
//       }
//     }

//     private string ExtractBoundary(string contentType)
//     {
//       try
//       {
//         var parts = contentType.Split(';');
//         foreach (var part in parts)
//         {
//           var trimmed = part.Trim();
//           if (trimmed.StartsWith("boundary="))
//           {
//             var boundary = trimmed.Substring("boundary=".Length);
//             return boundary.Trim('"');
//           }
//         }
//         return null;
//       }
//       catch
//       {
//         return null;
//       }
//     }
//   }
// }