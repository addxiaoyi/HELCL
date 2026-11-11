# Third-party code and assets

HELCL original contributions are licensed under MIT, copyright (c) 2026 addxiaoyi.
The root LICENSE also retains the TipCL copyright notice; the upstream text is
preserved verbatim in `docs/licenses/TipCL-MIT.txt` and shipped in `licenses/`.
This does not relicense third-party code, artwork, Minecraft files or user content.

The portable desktop dependency graph currently includes:

| Component | Version | Declared license |
| --- | --- | --- |
| Avalonia UI and platform packages | 12.1.6 | MIT |
| Avalonia.BuildServices (build tool) | 11.3.2 | MIT |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | Bundled LICENSE (includes upstream notices) |
| SkiaSharp and native assets | 3.119.4 | MIT; native third-party notices also apply |
| HarfBuzzSharp and native assets | 8.3.1.3 | MIT; native third-party notices also apply |
| MicroCom.Runtime | 0.11.6 | MIT |
| Tmds.DBus.Protocol | 0.94.1 | MIT |
| System.Management / System.CodeDom | 10.0.0 | MIT |
| .NET runtime | selected by .NET 10 SDK | Runtime LICENSE and ThirdPartyNotices |

Versions/licenses were read from restored NuGet metadata on 2026-11-11. Build-only
and other-platform packages can appear in that graph without shipping in every
archive. Dependency metadata is not a complete audit of native transitive code.
Keep the license/notice files present in self-contained .NET publish output.

Minecraft, Java, mods, resource packs and downloaded project images remain subject
to their respective upstream licenses and terms. They are not licensed by the
HELCL root LICENSE. Catalog artwork comes from the corresponding upstream project;
it must not be reused as unrelated branding.

The HelL logo and historical imported/decompiled sources still need a complete
provenance inventory before a formal SignPath application. The maintainer's MIT
choice covers their original contributions, not an assertion of ownership over
all historical content. See `docs/license-provenance.md` for verified evidence.
