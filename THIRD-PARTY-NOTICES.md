# Third-party notices

Aperture's own code is under the MIT License ([LICENSE](LICENSE)). Its release packages also contain the components
below, each under its own license. The MIT License of Aperture doesn't change or extend those licenses.

## QuestPDF

Creates the invoice and statement PDFs. © Marcin Ziąbek and QuestPDF contributors, https://www.questpdf.com.

QuestPDF is **not** under the MIT License: it's under the QuestPDF Community License, a source-available commercial
license (https://www.questpdf.com/license/). It's free for open-source projects like Aperture, for individuals, and for
businesses with less than USD 1M annual gross revenue. Running Aperture without changing its code counts as using
QuestPDF as a transitive dependency, which is free too. If you build on Aperture's code in an organisation that doesn't
qualify, you need a QuestPDF Professional or Enterprise license.

QuestPDF includes Skia (BSD 3-Clause License, © Google LLC) and qpdf (Apache License 2.0, © Jay Berkenbilt).

## Libraries

| Component | Used for | License | Copyright |
|---|---|---|---|
| .NET runtime, ASP.NET Core | runtime and web framework | MIT | © .NET Foundation and Contributors; its own notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT |
| Entity Framework Core, Microsoft.Data.Sqlite | database access | MIT | © .NET Foundation and Contributors |
| Radzen.Blazor | UI components | MIT | © Radzen Ltd. |
| ClosedXML, ClosedXML.Parser | Excel export | MIT | © Jan Havlíček, Francois Botha, Aleksei Pankratev, Manuel de Leon, Amir Ghezelbash |
| DocumentFormat.OpenXml | Excel export (used by ClosedXML) | MIT | © Microsoft Corporation |
| ExcelNumberFormat | Excel export (used by ClosedXML) | MIT | © ExcelNumberFormat developers |
| RBush | Excel export (used by ClosedXML) | MIT | © 2017-2024 Turning Code, LLC and others |
| SixLabors.Fonts | Excel export (used by ClosedXML) | Apache 2.0 | © Six Labors |
| Codecrete.SwissQRBill.Generator | Swiss QR-bill | MIT | © Manuel Bleichenbacher |
| Net.Codecrete.QrCodeGenerator | QR codes (used by the QR-bill) | MIT | © Manuel Bleichenbacher |
| SkiaSharp | graphics (used by the QR-bill) | MIT | © Microsoft Corporation; includes Skia, BSD 3-Clause, © Google LLC |
| SQLitePCLRaw | SQLite driver | Apache 2.0 | © 2014-2024 SourceGear, LLC |
| SQLite | the database engine | Public domain | https://sqlite.org/copyright.html |

## Fonts

| Font | Used for | License |
|---|---|---|
| Space Grotesk | app font, in `wwwroot/fonts` | SIL Open Font License 1.1, © The Space Grotesk Project Authors; license next to the font |
| Liberation Sans | text in PDFs, embedded in the app | SIL Open Font License 1.1, © Red Hat, Inc.; `src/Aperture.Web/Features/Invoices/Fonts/LICENSE-LiberationSans.txt` |
| Source Sans 3, Inter, Roboto Flex | bundled with Radzen.Blazor | SIL Open Font License 1.1; licenses in `wwwroot/_content/Radzen.Blazor/fonts` |
| Material Symbols | icons, bundled with Radzen.Blazor | Apache 2.0, © Google LLC |

## License texts

The components marked MIT above are distributed under the following terms, with the copyright notices given there:

```
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

The Apache License 2.0 is in [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt). The BSD 3-Clause License of Skia:

```
Redistribution and use in source and binary forms, with or without modification, are permitted provided that the
following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following
   disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following
   disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote
   products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
