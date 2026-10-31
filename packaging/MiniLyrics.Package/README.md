# Microsoft Store MSIX packaging draft

This packaging project is a draft for the Partner Center MSIX product **Mini Lyrics for Spotify Desktop** (`9PPLJ3QQRNF3`). The official product identity is filled into `Package.appxmanifest`.

## Identity values still required

The manifest identity values were read from the product's Partner Center identity page:

- `Identity Name`: `19108TheRusun.MiniLyricsforSpotifyDesktop`.
- `Identity Publisher`: `CN=A13B6398-8001-4158-A92C-2EA07BE0669D`.
- `PublisherDisplayName`: `The Rusun`.

Do not copy the identity from the existing Win32 EXE/MSI product. Partner Center may assign a different product identity to this MSIX listing.

## Packaging prerequisites

- Finish installing Windows SDK components for MSIX packaging, including MakeAppx and MakePri, and the Windows SDK for UWP managed apps. Setup is currently downloading SDK components.
- The packaging project imports the Visual Studio DesktopBridge targets. Build with Visual Studio/MSBuild after SDK installation completes.
- Confirm the app executable at `artifacts/latest/MiniLyricsForSpotify.exe` is the release intended for Store submission and includes the latest source changes.

## Assets and submission

Package tile assets under `Assets/` were derived from the generated store logo. Store listing images in `store-assets/` remain separate listing artwork.

The MSIX product and its first submission draft now exist in Partner Center. Generate the package upload bundle using the assigned identity and validate the resulting package before uploading it to that draft.

The existing `artifacts/latest` contents and third-party license files are kept intact. The project copies the executable, `THIRD-PARTY-NOTICES.md`, and `licenses/` into the package when those build inputs are present.
