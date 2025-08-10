// Services/DicomFileService.cs
using Dicom;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
namespace DicomWebFsServer.Services
{
  /* Service for persisting DICOM files and exposing metadata/query helpers
   * used by WADO-RS and QIDO-RS controllers. */
  public class DicomFileService
  {
    private readonly string _storagePath;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string EncapsulatedPdfSopClassUid = "1.2.840.10008.5.1.4.1.1.104.1";
    private const string DocumentModality = "DOC";

    /* Constructor – reads storage path from configuration. */
    public DicomFileService(IConfiguration config, IHttpContextAccessor httpContextAccessor = null)
    {
      _storagePath = config["DicomStoragePath"] ?? "dicom-storage";
      _httpContextAccessor = httpContextAccessor;
      Console.WriteLine($"DicomFileService initialised – Storage: {_storagePath}");
    }
    /* ================================================================
     *                       PERSISTENCE HELPERS
     * ================================================================ */
    /* Save a DICOM file (Part-10) to disk at <storage>/<study>/<series>/<sop>.dcm */
    public async Task<bool> SaveDicomToFilesystem(DicomFile dicomFile)
    {
      try
      {
        var ds = dicomFile.Dataset;
        var studyUid = ds.GetSingleValue<string>(DicomTag.StudyInstanceUID);
        var seriesUid = ds.GetSingleValue<string>(DicomTag.SeriesInstanceUID);
        var sopUid = ds.GetSingleValue<string>(DicomTag.SOPInstanceUID);
        var dir = Path.Combine(_storagePath, studyUid, seriesUid);
        Directory.CreateDirectory(dir);
        var fp = Path.Combine(dir, $"{sopUid}.dcm");
        await dicomFile.SaveAsync(fp).ConfigureAwait(false);
        Console.WriteLine($"Saved DICOM → {fp}");
        return true;
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error saving DICOM: {ex.Message}");
        return false;
      }
    }
    /* Delete an entire study folder (all series/instances). */
    public bool DeleteStudy(string studyUid)
    {
      try
      {
        var p = Path.Combine(_storagePath, studyUid);
        if (!Directory.Exists(p))
        {
          Console.WriteLine($"DeleteStudy: no dir {p}");
          return false;
        }
        Directory.Delete(p, true);
        Console.WriteLine($"Deleted {p}");
        return true;
      }
      catch (Exception ex)
      {
        Console.WriteLine($"DeleteStudy error: {ex.Message}");
        return false;
      }
    }
    /* ================================================================
     *                  METADATA HELPERS – WADO-RS
     * ================================================================ */
    /* Build instance-level metadata (DICOM-JSON) for a series,
       optional filter on SOPInstanceUID. */
    public List<object> GetInstanceMetadata(string studyUid,
                                            string seriesUid,
                                            string sopInstanceUID = null)
    {
      var results = new List<object>();
      var seriesPath = Path.Combine(_storagePath, studyUid, seriesUid);
      if (!Directory.Exists(seriesPath))
        return results;
      foreach (var fp in Directory.GetFiles(seriesPath, "*.dcm"))
      {
        try
        {
          var dcm = DicomFile.Open(fp);
          var ds = dcm.Dataset;
          var instanceUid = ds.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, "");
          if (!string.IsNullOrEmpty(sopInstanceUID) && instanceUid != sopInstanceUID)
            continue;
          var meta = new Dictionary<string, object>();
          /* 0002,0010 – Transfer Syntax */
          var tsUid = dcm.FileMetaInfo?
                        .GetSingleValueOrDefault(DicomTag.TransferSyntaxUID,
                                                  DicomTransferSyntax.ImplicitVRLittleEndian.UID)
                        .UID ?? DicomTransferSyntax.ImplicitVRLittleEndian.UID.UID;
          meta["00020010"] = CreateDicomAttribute("UI", tsUid);
          /* iterate dataset */
          foreach (var item in ds)
          {
            var tagKey = $"{item.Tag.Group:X4}{item.Tag.Element:X4}";
            var vr = item.ValueRepresentation;
            /* PixelData → BulkDataURI */
            if (item.Tag == DicomTag.PixelData)
            {
              meta[tagKey] = new Dictionary<string, object>
              {
                ["vr"] = vr.ToString(),
                ["BulkDataURI"] = $"{GetServerBaseAddress()}/dicomweb/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/bulk/7fe00010"
              };
              continue;
            }
            /* Sequences */
            if (vr == DicomVR.SQ && ds.GetSequence(item.Tag) is { } sq)
            {
              var seqArr = new List<object>();
              foreach (var sqItem in sq.Items)
              {
                var sqDict = new Dictionary<string, object>();
                foreach (var el in sqItem)
                {
                  var elVal = ProcessDicomItemValue(el);
                  if (elVal == null) continue;
                  var elKey = $"{el.Tag.Group:X4}{el.Tag.Element:X4}";
                  sqDict[elKey] = new Dictionary<string, object>
                  {
                    ["vr"] = el.ValueRepresentation.ToString(),
                    ["Value"] = elVal
                  };
                }
                if (sqDict.Count > 0) seqArr.Add(sqDict);
              }
              if (seqArr.Count > 0)
              {
                meta[tagKey] = new Dictionary<string, object>
                {
                  ["vr"] = vr.ToString(),
                  ["Value"] = seqArr.ToArray()
                };
              }
              continue;
            }
            var val = ProcessDicomItemValue(item);
            if (val != null)
            {
              meta[tagKey] = new Dictionary<string, object>
              {
                ["vr"] = vr.ToString(),
                ["Value"] = val
              };
            }
          }
          /* guarantee core identifiers */
          if (!meta.ContainsKey("00080018"))
            meta["00080018"] = CreateDicomAttribute("UI", instanceUid);
          if (!meta.ContainsKey("0020000D"))
            meta["0020000D"] = CreateDicomAttribute("UI", studyUid);
          if (!meta.ContainsKey("0020000E"))
            meta["0020000E"] = CreateDicomAttribute("UI", seriesUid);

          // --- NEW: Override Modality for Encapsulated PDF ---
          var sopClassUid = ds.GetSingleValueOrDefault<DicomUID>(DicomTag.SOPClassUID, null)?.UID;
          if (sopClassUid == EncapsulatedPdfSopClassUid)
          {
            var modalityTagKey = $"{DicomTag.Modality.Group:X4}{DicomTag.Modality.Element:X4}";
            meta[modalityTagKey] = CreateDicomAttribute("CS", DocumentModality);
          }
          // --- END NEW ---

          results.Add(meta);
        }
        catch (Exception ex)
        {
          Console.WriteLine($"Instance metadata error {fp}: {ex}");
        }
      }
      return results;
    }
    /* Get all instance metadata for every series in a study (WADO helper). */
    public List<object> GetStudyInstanceMetadata(string studyUid)
    {
      var list = new List<object>();
      var studyPath = Path.Combine(_storagePath, studyUid);
      if (!Directory.Exists(studyPath)) return list;
      foreach (var seriesDir in Directory.EnumerateDirectories(studyPath))
      {
        var seriesUid = Path.GetFileName(seriesDir);
        list.AddRange(GetInstanceMetadata(studyUid, seriesUid));
      }
      return list;
    }
    /* ================================================================
     *                  METADATA HELPERS – QIDO-RS
     * ================================================================ */
    /* Build study-level metadata list (QIDO-RS) with optional filters. */
    public List<object> GetStudyMetadata(string patientName = null,
                                         string patientId = null,
                                         string studyDate = null,
                                         string studyInstanceUID = null)
    {
      var results = new List<object>();
      Console.WriteLine($"GetStudyMetadata called with: patientName={patientName}, patientId={patientId}, studyDate={studyDate}, studyInstanceUID={studyInstanceUID}");
      if (!Directory.Exists(_storagePath))
      {
        Console.WriteLine($"Storage path does not exist: {_storagePath}");
        return results;
      }
      /* collect files grouped by StudyInstanceUID */
      var studyGroups = new Dictionary<string, List<string>>();
      foreach (var studyDir in Directory.EnumerateDirectories(_storagePath))
      {
        foreach (var filePath in Directory.GetFiles(studyDir, "*.dcm", SearchOption.AllDirectories))
        {
          string currentStudyUid;
          try { currentStudyUid = DicomFile.Open(filePath).Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, ""); }
          catch (Exception ex)
          {
            Console.WriteLine($"Error reading {filePath}: {ex.Message}");
            continue;
          }
          if (string.IsNullOrEmpty(currentStudyUid)) continue;
          if (!string.IsNullOrEmpty(studyInstanceUID) && currentStudyUid != studyInstanceUID) continue;
          if (!studyGroups.ContainsKey(currentStudyUid))
            studyGroups[currentStudyUid] = new List<string>();
          studyGroups[currentStudyUid].Add(filePath);
        }
      }
      /* build metadata per study */
      foreach (var (studyUid, studyFiles) in studyGroups)
      {
        if (!studyFiles.Any()) continue;
        var ds = DicomFile.Open(studyFiles.First()).Dataset;
        /* filters */
        var nameValue = ds.GetSingleValueOrDefault(DicomTag.PatientName, "");
        if (!string.IsNullOrEmpty(patientName) &&
            (string.IsNullOrEmpty(nameValue) || !nameValue.Contains(patientName, StringComparison.OrdinalIgnoreCase)))
          continue;
        var pid = ds.GetSingleValueOrDefault(DicomTag.PatientID, "");
        if (!string.IsNullOrEmpty(patientId) && pid != patientId) continue;
        var sDate = ds.GetSingleValueOrDefault(DicomTag.StudyDate, "");
        if (!string.IsNullOrEmpty(studyDate) && sDate != studyDate) continue;
        /* modalities in study (first 10 files) */
        var modalitiesInStudy = new HashSet<string>();
        foreach (var fp in studyFiles.Take(10)) // Process more files to ensure we catch PDFs
        {
          try
          {
            var file = DicomFile.Open(fp);
            var sopClassUid = file.Dataset.GetSingleValueOrDefault<DicomUID>(DicomTag.SOPClassUID, null)?.UID;
            string mod;
            if (sopClassUid == EncapsulatedPdfSopClassUid)
            {
              mod = DocumentModality; // Override for PDF
            }
            else
            {
              mod = file.Dataset.GetSingleValueOrDefault(DicomTag.Modality, "");
            }
            if (!string.IsNullOrEmpty(mod)) modalitiesInStudy.Add(mod);
          }
          catch (Exception ex) { Console.WriteLine($"Error reading modality from {fp}: {ex.Message}"); }
        }
        /* metadata dictionary */
        var studyMetadata = new Dictionary<string, object>();
        foreach (var dicomItem in ds)
        {
          try
          {
            var tag = dicomItem.Tag;
            var tagKey = $"{tag.Group:X4}{tag.Element:X4}";
            var vr = dicomItem.ValueRepresentation;
            if (vr == DicomVR.PN)
            {
              AddPersonNameIfExists(studyMetadata, tagKey, ds, tag);
              continue;
            }
            // --- NEW: Override Modality for Encapsulated PDF in Study Metadata ---
            if (tag == DicomTag.Modality)
            {
              var sopClassUid = ds.GetSingleValueOrDefault<DicomUID>(DicomTag.SOPClassUID, null)?.UID;
              if (sopClassUid == EncapsulatedPdfSopClassUid)
              {
                // Override Modality tag value for this specific instance
                studyMetadata[tagKey] = CreateDicomAttribute("CS", DocumentModality);
                continue; // Skip default processing
              }
            }
            // --- END NEW ---
            var value = ProcessDicomItemValue(dicomItem);
            if (value != null)
            {
              studyMetadata[tagKey] = new Dictionary<string, object>
              {
                ["vr"] = vr.ToString(),
                ["Value"] = value
              };
            }
          }
          catch (Exception ex)
          {
            Console.WriteLine($"Warning processing study tag {dicomItem?.Tag}: {ex.Message}");
          }
        }
        /* calculated attributes */
        studyMetadata["00080061"] = CreateDicomAttribute("CS", modalitiesInStudy.ToArray()); // This now includes DOC
        if (!studyMetadata.ContainsKey("00201206"))
        {
          var seriesUids = new HashSet<string>(
            studyFiles.Select(fp =>
            {
              try { return DicomFile.Open(fp).Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, ""); }
              catch { return ""; }
            }).Where(u => !string.IsNullOrEmpty(u)));
          studyMetadata["00201206"] = CreateDicomAttribute("IS", seriesUids.Count.ToString());
        }
        if (!studyMetadata.ContainsKey("00201208"))
          studyMetadata["00201208"] = CreateDicomAttribute("IS", studyFiles.Count.ToString());
        var retrieveUrl = $"{GetServerBaseAddress()}/dicomweb/studies/{studyUid}";
        studyMetadata["00081190"] = CreateDicomAttribute("UR", retrieveUrl);
        results.Add(studyMetadata);
      }
      return results;
    }
    /* Build series-level metadata list for a study (QIDO-RS). */
    public List<object> GetSeriesMetadata(string studyUid,
                                          string modality = null,
                                          string seriesInstanceUID = null)
    {
      var results = new List<object>();
      var studyPath = Path.Combine(_storagePath, studyUid);
      if (!Directory.Exists(studyPath)) return results;
      var seriesGroups = new Dictionary<string, List<string>>();
      /* group files by SeriesInstanceUID */
      foreach (var seriesDir in Directory.EnumerateDirectories(studyPath))
      {
        foreach (var fp in Directory.GetFiles(seriesDir, "*.dcm"))
        {
          try
          {
            var dcm = DicomFile.Open(fp);
            var seriesUid = dcm.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "");
            if (string.IsNullOrEmpty(seriesUid)) continue;
            if (!string.IsNullOrEmpty(seriesInstanceUID) && seriesUid != seriesInstanceUID) continue;

            // --- MODIFIED: Check for Encapsulated PDF and override Modality filter ---
            var sopClassUid = dcm.Dataset.GetSingleValueOrDefault<DicomUID>(DicomTag.SOPClassUID, null)?.UID;
            string effectiveModality;
            if (sopClassUid == EncapsulatedPdfSopClassUid)
            {
              effectiveModality = DocumentModality; // Override for PDF
            }
            else
            {
              effectiveModality = dcm.Dataset.GetSingleValueOrDefault(DicomTag.Modality, "");
            }

            if (!string.IsNullOrEmpty(modality) && !effectiveModality.Equals(modality, StringComparison.OrdinalIgnoreCase)) continue;
            // --- END MODIFIED ---

            if (!seriesGroups.ContainsKey(seriesUid))
              seriesGroups[seriesUid] = new List<string>();
            seriesGroups[seriesUid].Add(fp);
          }
          catch (Exception ex) { Console.WriteLine($"Series meta error: {ex.Message}"); }
        }
      }
      /* build metadata per series */
      foreach (var (seriesUid, files) in seriesGroups)
      {
        // --- MODIFIED: Determine Modality from the first file, considering PDF override ---
        var firstFile = DicomFile.Open(files.First());
        var firstDs = firstFile.Dataset;
        string effectiveModalityForSeries;
        var firstSopClassUid = firstDs.GetSingleValueOrDefault<DicomUID>(DicomTag.SOPClassUID, null)?.UID;
        if (firstSopClassUid == EncapsulatedPdfSopClassUid)
        {
          effectiveModalityForSeries = DocumentModality;
        }
        else
        {
          effectiveModalityForSeries = firstDs.GetSingleValueOrDefault(DicomTag.Modality, "");
        }
        var seriesNumber = firstDs.GetSingleValueOrDefault(DicomTag.SeriesNumber, "1");
        var desc = firstDs.GetSingleValueOrDefault(DicomTag.SeriesDescription, "");

        results.Add(new Dictionary<string, object>
        {
          ["0020000D"] = CreateDicomAttribute("UI", studyUid),
          ["0020000E"] = CreateDicomAttribute("UI", seriesUid),
          ["00200011"] = CreateDicomAttribute("IS", seriesNumber),
          ["0008103E"] = CreateDicomAttribute("LO", desc),
          // --- MODIFIED: Use overridden Modality ---
          ["00080060"] = CreateDicomAttribute("CS", effectiveModalityForSeries), // This will be DOC for PDF series
          // --- END MODIFIED ---
          ["00201209"] = CreateDicomAttribute("IS", files.Count.ToString())
        });
      }
      return results;
    }
    /* ================================================================
     *                      VALUE & UTILITY HELPERS
     * ================================================================ */
    /* Process a single DICOM item into a JSON-serialisable “Value” array. */
    private object ProcessDicomItemValue(DicomItem item)
    {
      if (item == null) return null;
      try
      {
        if (item.ValueRepresentation == DicomVR.PN)
          return GetDicomValues<string>(item)?
                   .Where(v => !string.IsNullOrEmpty(v))
                   .Select(v => (object)new Dictionary<string, string> { { "Alphabetic", v } })
                   .ToArray() ?? Array.Empty<object>();
        if (IsStringVr(item.ValueRepresentation))
          return GetDicomValues<string>(item) ?? Array.Empty<string>();
        if (item.ValueRepresentation == DicomVR.US)
          return GetDicomValues<ushort>(item) ?? Array.Empty<ushort>();
        if (item.ValueRepresentation == DicomVR.UL)
          return GetDicomValues<uint>(item) ?? Array.Empty<uint>();
        if (item.ValueRepresentation == DicomVR.SS)
          return GetDicomValues<short>(item) ?? Array.Empty<short>();
        if (item.ValueRepresentation == DicomVR.SL)
          return GetDicomValues<int>(item) ?? Array.Empty<int>();
        if (item.ValueRepresentation == DicomVR.FL)
          return (GetDicomValues<float>(item) ?? Array.Empty<float>())
                  .Select(f => f.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (item.ValueRepresentation == DicomVR.FD)
          return (GetDicomValues<double>(item) ?? Array.Empty<double>())
                  .Select(d => d.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (item.ValueRepresentation == DicomVR.AT)
          return GetDicomValues<string>(item) ?? Array.Empty<string>();
        if (IsBinaryVr(item.ValueRepresentation))
          return Array.Empty<object>();
        return GetDicomValues<string>(item) ?? Array.Empty<string>();
      }
      catch { return null; }
    }
    /* Extract strongly-typed value array for a DICOM element. */
    private T[] GetDicomValues<T>(DicomItem item)
    {
      if (item is not DicomElement el) return null;
      try
      {
        var arr = new T[el.Count];
        for (int i = 0; i < el.Count; i++) arr[i] = el.Get<T>(i);
        return arr;
      }
      catch { return null; }
    }
    /* VR helpers */
    private static bool IsStringVr(DicomVR vr) =>
      vr == DicomVR.AE || vr == DicomVR.AS || vr == DicomVR.CS || vr == DicomVR.DA ||
      vr == DicomVR.DS || vr == DicomVR.DT || vr == DicomVR.IS || vr == DicomVR.LO ||
      vr == DicomVR.LT || vr == DicomVR.SH || vr == DicomVR.ST || vr == DicomVR.TM ||
      vr == DicomVR.UC || vr == DicomVR.UI || vr == DicomVR.UR || vr == DicomVR.UT;
    private static bool IsBinaryVr(DicomVR vr) =>
      vr == DicomVR.OB || vr == DicomVR.OW || vr == DicomVR.OF || vr == DicomVR.OL ||
      vr == DicomVR.OD || vr == DicomVR.UC || vr == DicomVR.UR || vr == DicomVR.UT;
    /* Build a DICOM-JSON attribute object { vr, Value }. */
    private Dictionary<string, object> CreateDicomAttribute(string vr, object value)
    {
      var arr = value is Array a ? a : new[] { value };
      return new Dictionary<string, object> { { "vr", vr }, { "Value", arr } };
    }
    /* Get the base URL of the current request or fallback to localhost. */
    private string GetServerBaseAddress()
    {
      if (_httpContextAccessor?.HttpContext?.Request is { } r)
        return $"{r.Scheme}://{r.Host}";
      return "http://localhost:5152";
    }
    /* ================================================================
     *                        FILE ACCESS HELPERS
     * ================================================================ */
    /* Build filesystem path for a DICOM instance. */
    public string GetFilePath(string studyUid, string seriesUid, string instanceUid) =>
      Path.Combine(_storagePath, studyUid, seriesUid, $"{instanceUid}.dcm");
    /* Open and return a DicomFile (or null if missing/invalid). */
    public DicomFile GetDicomFile(string studyUid, string seriesUid, string instanceUid)
    {
      var fp = GetFilePath(studyUid, seriesUid, instanceUid);
      if (!File.Exists(fp)) return null;
      try { return DicomFile.Open(fp); }
      catch (Exception ex)
      {
        Console.WriteLine($"Open error {fp}: {ex.Message}");
        return null;
      }
    }
    /* Add PN value to study metadata if present. */
    private void AddPersonNameIfExists(Dictionary<string, object> metadata,
                                       string tagKey,
                                       DicomDataset ds,
                                       DicomTag tag)
    {
      if (!ds.Contains(tag)) return;
      var pnValue = ds.GetSingleValueOrDefault(tag, "");
      if (string.IsNullOrEmpty(pnValue)) return;
      metadata[tagKey] = new Dictionary<string, object>
      {
        ["vr"] = "PN",
        ["Value"] = new[]
        {
          new Dictionary<string,string> { { "Alphabetic", pnValue } }
        }
      };
    }
  }
}