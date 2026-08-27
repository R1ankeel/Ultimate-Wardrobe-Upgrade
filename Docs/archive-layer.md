# Archive Layer

> Sprint 0.2 - `src/UltimateWardrobe.Archives` - signature detection, P/Invoke wrappers, recursion, safety.

## Overview

The archive layer unpacks any Nexus donor archive (`.7z`, `.zip`, `.rar`, with nested archives) into `Project/Source/<ImportId>/` without FOMOD semantics. Detection is by magic bytes, not extension. All entry names are sanitized against path traversal.

## Components

| File | Purpose |
|------|---------|
| `ArchiveFormatDetector.cs:1` | Magic-byte detection (first 16 bytes) |
| `PathSanitizer.cs:1` | Central traversal guard - rejects `..`, absolute, drive-letter, UNC |
| `Native/SevenZipNative.cs:1` | P/Invoke COM-free wrapper over `runtimes/win-x64/native/7z.dll` - handles `SevenZip` and `Zip` |
| `Native/RarNative.cs:1` | P/Invoke wrapper over `runtimes/win-x64/native/UnRAR64.dll` (UnRAR DLL API) - handles `Rar` (RAR4/RAR5) |
| `Native/NativeContracts.cs:1` | `ISevenZipNative` / `IRarNative` and `NativeEngineNames` |
| `SevenZipExtractor.cs:1` | Native-first 7z.dll extractor with SharpCompress fallback - handles `SevenZip` and `Zip` |
| `RarExtractor.cs:1` | Native-first UnRAR64.dll extractor with SharpCompress fallback - handles `Rar` (RAR4/RAR5) |
| `CompositeExtractor.cs:1` | `Detect -> dispatch` + recursive handling + limits |
| `DonorImportService.cs:1` | `archive -> Source/<ImportId>/_meta.json` + `DonorAsset` |
| `ArchiveExceptions.cs:1` | `UnsupportedArchiveException`, `ArchiveTooLargeException`, `NativeLibraryNotFoundException` |

## Detection

```csharp
ArchiveFormatDetector.Detect(ReadOnlySpan<byte> header)
ArchiveFormatDetector.DetectFromFile(string path)
ArchiveFormatDetector.DetectFromFileAsync(string path, CancellationToken)
```

Magic:

- `7z`: `37 7A BC AF 27 1C`
- `rar`: `52 61 72 21 1A 07`
- `zip`: `50 4B 03 04` / `05 06` / `07 08`

Unknown returns `ArchiveFormat.Unknown` and causes `UnsupportedArchiveException` at the composite level.

Tests: `tests/UltimateWardrobe.Tests/Archives/ArchiveFormatDetectorTests.cs:1` - covers all magics, empty, truncated, file I/O.

## P/Invoke Strategy

- Native DLLs are at `runtimes/win-x64/native/7z.dll` and `UnRAR64.dll`, copied to output via `Content` in `UltimateWardrobe.Archives.csproj:11`
- `SevenZipExtractor` and `RarExtractor` are native-first: they route to the P/Invoke engine when the DLL is available and report the engine in `ExtractResult.Engine` (`7z.dll` / `UnRAR64.dll`). On `NativeLibraryNotFoundException` or `ArchiveOpenException` they fall back to `SharpCompress`.
- 7z/zip via `SevenZipNative`: the 7z.dll COM-free API is driven through `GetHandlerProperty2` + `CreateObject` with format CLSIDs (`7z` = `23170F69-40C1-278A-1000-000110070000`, `zip` = `23170F69-40C1-278A-1000-000110010000`). `IInArchive::Extract` is called with an explicit array of indices (`0..count-1`); the C#-side callbacks (`IArchiveExtractCallback`, `IInStream`, `IOutStream`) and vtable slots are provided via unmanaged delegates, and `IOutStream.Write` expects the `Flush` slot.
- rar via `RarNative`: `RAROpenArchiveEx` / `RARReadHeaderEx` / `RARProcessFileW` / `RARCloseArchive`. Packed structs match the official unrar `dll.hpp`: `RAROpenArchiveDataEx` (176 bytes) and `RARHeaderDataEx` (10244 bytes). Both buffers are zero-filled before use - UnRAR reads input fields that live in the legacy `Reserved` area (`ArcNameEx`, `FileNameEx`, `RedirName`, `RedirNameSize`) and dereferences them as pointers if left uninitialized. Directory entries are skipped (`RAR_SKIP`), unsanitized entries are skipped, and `destPath` is passed to `RARProcessFileW` as a wide string.
- `WarningsNotAsErrors` for `NU1900` is set to allow `SharpCompress 0.41.0` while keeping `TreatWarningsAsErrors` for project code.

Source: `src/UltimateWardrobe.Archives/SevenZipExtractor.cs:1`, `RarExtractor.cs:1`, `Native/SevenZipNative.cs:1`, `Native/RarNative.cs:1`

## Safety

- `PathSanitizer.IsSafeEntry(entryKey, out sanitized)` normalizes `\` to `/`, rejects `Path.IsPathRooted`, drive letters (`C:`), UNC (`//`), and any segment `..` or `.` or containing `:`
- `GetSafeFullPath(destDir, sanitized)` joins via `Path.Combine` per segment and verifies `Path.GetFullPath(result).StartsWith(Path.GetFullPath(destDir))`
- Entries failing sanitization are skipped, not extracted

Tests: `tests/UltimateWardrobe.Tests/Archives/PathSanitizerTests.cs:1`, `CompositeExtractorTests.cs:41` (traversal)

## Recursion

Algorithm in `CompositeExtractor.cs:35`:

```
ExtractAsync(archive, dest):
  1. Detect format -> dispatch to SevenZip/Rar
  2. Extract -> dest
  3. For depth in 0..MaxDepth (5):
       - Enumerate dest/**/*.{7z,zip,rar}
       - For each nested archive not seen before (SHA256 hash):
           Dispatch nested -> dest (flat, not subfolder)
           Delete nested archive file
           nestedHandled++
       - If none found, break
  4. Enforce MaxTotalBytes (10 GB) after each level
```

- `MaxDepth = 5`, `MaxTotalBytes = 10 * 1024^3`
- Hash-based cycle detection prevents re-extracting same content
- Returns `ExtractResult { ExtractedFiles, NestedHandled, Format }` where `ExtractedFiles` are only still-existing files

Tests: `tests/UltimateWardrobe.Tests/Archives/CompositeExtractorTests.cs:25` (nested zip via `ArchiveTestHelper.cs:18`), `ExtractorIntegrationTests.cs:1` on real mods.

## Donor Import

```csharp
DonorImportService.ImportAsync(string archivePath, string projectRoot, CancellationToken)
  -> DonorAsset
```

Steps (`src/UltimateWardrobe.Archives/DonorImportService.cs:17`):

1. `ImportId = Guid.NewGuid()`, `dest = <projectRoot>/Source/<ImportId>`
2. `SHA256` of original archive (hex lower)
3. `CompositeExtractor.ExtractAsync(archive, dest)`
4. Build `FileManifest` as relative `/`-separated sorted list of files under `dest`
5. Write `_meta.json` (UTF-8, indented): `{ importId, originalFileName, importedAtUtc, archiveHash, archiveFormat, extractedFilesCount, nestedHandled }`
6. Return `DonorAsset(ImportId, OriginalFileName, ExtractedPath=dest, ImportedAt, ArchiveHash, FullReplacer, manifest)`
7. On any failure, delete `dest` recursively

Tests: `tests/UltimateWardrobe.Tests/Archives/DonorImportServiceTests.cs:1` - manifest, hash stability, meta fields, failure cleanup.

## Progress and Cancellation

- All extractors accept `IProgress<ExtractProgress>` and `CancellationToken`
- Progress reported per file (`FilesDone`, `BytesDone`)
- `cancellationToken.ThrowIfCancellationRequested()` checked per entry and per recursion level - yields `OperationCanceledException`

Test: `tests/UltimateWardrobe.Tests/Archives/CompositeExtractorTests.cs:58`

## Build and Test

```powershell
dotnet build -c Release
dotnet test -c Release --filter "Category=Unit"      # no Skyrim / ModsForTests needed
dotnet test -c Release --filter "Category=Integration" # requires ModsForTests/Armor
dotnet test -c Release                                # all 96 tests green (Core + Archives)
```

- Unit tests create synthetic zips via `System.IO.Compression.ZipArchive` in `ArchiveTestHelper.cs:1` and clean up via `IDisposable` temp dirs (`%TEMP%/UW_*` deleted in `Dispose`)
- Golden archives (`tests/TestData/Archives/`) are extracted through the real native engines - `sample.7z`/`sample.zip` via `7z.dll`, `sample.rar` (RAR4) and `sample_rar5.rar` via `UnRAR64.dll`, `nested.7z` through both levels - and assert engine names and content hashes
- Integration goldens are real Nexus archives under `ModsForTests/` (never committed as test outputs) - tests skip if directory missing via `MemberData` guard

## Runtime Layout

```
<ProjectRoot>/Source/<ImportId>/
  _meta.json
  meshes/...
  textures/...
  fomod/...          # treated as plain folder - FOMOD ignored
  CalienteTools/...  # if present
```

No BSA handling in Phase 0.
