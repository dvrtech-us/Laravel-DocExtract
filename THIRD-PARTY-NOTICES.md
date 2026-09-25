# Third-party notices

DocExtract 1.0.0. Versions match `extractor/Directory.Packages.props`. License texts ship inside the restored NuGet packages and must stay with any redistribution of those packages, including the self-contained win-x64 folder.

The PHP package also links Symfony Process and the Laravel illuminate components. Those packages keep their own licenses.

| Package | Version | License | Project |
|---|---|---|---|
| PdfPig | 0.1.16 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| Tabula | 1.0.1 | MIT | https://github.com/BobLd/tabula-sharp |
| PDFtoImage | 5.4.0 | MIT | https://github.com/sungaila/PDFtoImage |
| bblanchon.PDFium (via PDFtoImage) | 152.0.7961 | BSD-3-Clause | https://github.com/bblanchon/pdfium-binaries |
| Tesseract (CharlesW wrapper) | 5.2.0 | Apache-2.0 | https://github.com/charlesw/tesseract |
| Tesseract OCR engine and Leptonica (native binaries in the Tesseract package) | 5.x | Apache-2.0 | https://github.com/tesseract-ocr/tesseract |
| tessdata_fast `eng.traineddata` | commit 923915d4ced2a7235221788285785a29c4a42d4a | Apache-2.0 | https://github.com/tesseract-ocr/tessdata_fast |
| DocumentFormat.OpenXml | 3.5.1 | MIT | https://github.com/dotnet/Open-XML-SDK |
| MimeKit | 4.18.1 | MIT | https://github.com/jstedfast/MimeKit |
| BouncyCastle.Cryptography (via MimeKit) | 2.7.0 | MIT | https://www.bouncycastle.org/ |
| MsgReader | 6.1.2 | MIT | https://github.com/Sicos1977/MsgReader |
| OpenMcdf | 3.3.0 | MPL-2.0 | https://github.com/ironfede/openmcdf |
| SkiaSharp | 4.152.1 | MIT | https://github.com/mono/SkiaSharp |
| SkiaSharp.NativeAssets.Win32 | 4.152.1 | MIT | https://github.com/mono/SkiaSharp |
| SkiaSharp.NativeAssets.macOS | 4.152.1 | MIT | https://github.com/mono/SkiaSharp |
| xunit | 2.9.3 | Apache-2.0 | https://github.com/xunit/xunit |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | https://github.com/microsoft/vstest |

Skia itself is under the BSD-style license included with SkiaSharp's native assets. PDFium is BSD-3-Clause. Apache-2.0 components include their NOTICE obligations; keep the NuGet license and NOTICE files beside a published build when you redistribute it.

ImageSharp (Six Labors) is intentionally not used.
