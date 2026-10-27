# Third-party components

The romanization feature runs locally using these unmodified components. Their licenses and copyright notices are retained under `licenses/` and in the packaged dictionary.

| Component | Version | License / source |
| --- | --- | --- |
| Kawazu | 1.1.4 | MIT — https://github.com/Cutano/Kawazu |
| Romanization.NET | 0.9.5 | MIT — https://github.com/zedseven/Romanization.NET |
| LibNMeCab | 0.10.1 | GPL-2.0-or-later OR LGPL-2.1-or-later — https://github.com/komutan/NMeCab |
| LibNMeCab.IpaDicBin | 0.10.0 | Package metadata: GPL-2.0-or-later OR LGPL-2.1-or-later. Dictionary notices: `licenses/IpaDic-COPYING.txt`, `licenses/IpaDic-AUTHORS.txt` — https://github.com/komutan/NMeCab |

LibNMeCab is used as a separately compiled .NET library under the LGPL option; its source and build instructions are available at the upstream repository above. No changes have been made to its code. A normal folder publish can be built with `-p:PublishSingleFile=false` to use replaceable library DLLs. Single-file builds extract the bundled libraries and dictionary before execution. Keep these notices and the license files with distributed builds.

The Korean feature uses Revised Romanization of Hangul; Japanese uses Kawazu with the IPA dictionary and Hepburn output. Automatic readings can differ from names, deliberate lyric readings, dialect, or a singer's pronunciation. These components do not translate lyrics or grant rights to lyric content.
