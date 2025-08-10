# DICOMweb Server using dotnet for OHIF Viewer

A lightweight DICOMweb server implementation built with .NET and C# from scratch. This server implements the minimal set of DICOMweb endpoints required for OHIF Viewer to function correctly.

## Features

This server implements the essential DICOMweb endpoints needed by OHIF Viewer:

### QIDO-RS (Query based on ID for Objects)

- `GET /dicomweb/studies` - Retrieve studies
- `GET /dicomweb/studies/{studyUid}/series` - Retrieve series for a specific study
- `GET /dicomweb/studies/{studyUid}/series/{seriesUid}/metadata` - Retrieve metadata for a specific series

### WADO-RS (Web Access to DICOM Objects)

- `GET /dicomweb/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/rendered` - Retrieve rendered instance
- `GET /dicomweb/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/thumbnail` - Retrieve thumbnail for instance
- `GET /dicomweb/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameNo}` - Retrieve specific frame from instance

## Prerequisites

- .NET 7.0 or higher
- DICOM files stored in a compatible format
- OHIF Viewer (for testing)

## Configuration

### OHIF Local Server Configuration

Create a `local-server.js` file with the following configuration:

```javascript
/** @type {AppTypes.Config} */

window.config = {
  name: "config/local-server.js",
  routerBasename: null,
  extensions: [],
  modes: [],
  customizationService: {},
  showStudyList: true,
  maxNumberOfWebWorkers: 3,
  showWarningMessageForCrossOrigin: true,
  showCPUFallbackMessage: true,
  showLoadingIndicator: true,
  experimentalStudyBrowserSort: false,
  strictZSpacingForVolumeViewport: true,
  groupEnabledModesFirst: true,
  allowMultiSelectExport: false,
  defaultDataSourceName: "local5152",
  dataSources: [
    {
      namespace: "@ohif/extension-default.dataSourcesModule.dicomweb",
      sourceName: "local5152",
      configuration: {
        friendlyName: "Local .NET DICOMWeb Server",
        name: "DotNetDICOMWeb",
        wadoUriRoot: "http://localhost:5152/dicomweb",
        qidoRoot: "http://localhost:5152/dicomweb",
        wadoRoot: "http://localhost:5152/dicomweb",
        qidoSupportsIncludeField: true,
        supportsStow: true,
        supportsReject: false,
        dicomUploadEnabled: true,
        imageRendering: "wadors",
        thumbnailRendering: "wadors",
        enableStudyLazyLoad: true,
        supportsFuzzyMatching: false,
        supportsWildcard: true,
        staticWado: false,
        omitQuotationForMultipartRequest: true,
        singlepart: "bulkdata,video,pdf",
        bulkDataURI: {
          enabled: true,
          relativeResolution: "studies",
        },
      },
    },
    {
      namespace: "@ohif/extension-default.dataSourcesModule.dicomjson",
      sourceName: "dicomjson",
      configuration: {
        friendlyName: "dicom json",
        name: "json",
      },
    },
    {
      namespace: "@ohif/extension-default.dataSourcesModule.dicomlocal",
      sourceName: "dicomlocal",
      configuration: {
        friendlyName: "dicom local",
      },
    },
  ],
  httpErrorHandler: (error) => {
    console.warn("HTTP Error:", error.status, error.statusText);
    console.warn("Error details:", error);
  },
};
```

## Setup and Installation

1. Clone the repository
2. Build the project:
   ```bash
   dotnet build
   ```
3. Run the server:
   ```bash
   dotnet run
   ```

## Usage

1. Start the DICOMweb server
2. Configure OHIF Viewer to use this server using the provided `local-server.js` configuration
3. Access OHIF Viewer and browse your DICOM studies

## API Endpoints

### Studies

```
GET /dicomweb/studies
```

Returns a list of available studies.

### Series

```
GET /dicomweb/studies/{studyUid}/series
```

Returns a list of series for the specified study.

### Metadata

```
GET /dicomweb/studies/{studyUid}/series/{seriesUid}/metadata
```

Returns metadata for the specified series.

### Rendered Instances

```
GET /dicomweb/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/rendered
```

Returns a rendered version of the specified instance.

### Thumbnails

```
GET /dicomweb/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/thumbnail
```

Returns a thumbnail image for the specified instance.

### Frame Data

```
GET /dicomweb/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameNo}
```

Returns the specified frame from a multi-frame instance.

## Limitations

- This implementation only includes endpoints required by OHIF Viewer
- Not all DICOMweb protocol endpoints are implemented
- Designed specifically for integration with OHIF Viewer

## Support

For issues related to OHIF Viewer integration, ensure your configuration matches the provided `local-server.js` example.
