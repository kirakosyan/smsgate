# SMSGate branding

The SMSGate mark combines a gateway with a message bubble. Use the same mark for the README and NuGet so the package is recognizable at small sizes.

| Asset | Use |
| --- | --- |
| `smsgate-logo.svg` | Editable wordmark and tagline on a neutral panel that remains readable on light and dark pages |
| `smsgate-logo.png` | 640 x 160 README export; use an absolute raw.githubusercontent.com URL so NuGet can display it |
| `smsgate-icon.svg` | Editable square mark |
| `smsgate-icon.png` | 128 x 128 NuGet icon, embedded as `/icon.png` by `Smpp.csproj` |
| `platforms/windows.svg`, `linux.svg`, `macos.svg` | 24 x 24 operating system logos for the support matrix |
| `platforms/*-ci.svg`, `macos-unverified.svg` | 28-pixel-high status badges for the README header |

The original SMSGate artwork is covered by the repository's MIT license. The wordmark uses the `Segoe UI, DejaVu Sans, sans-serif` font stack; the PNG is the fixed rendering used in the README. Keep the two SVG marks in sync when changing their geometry, and regenerate both PNG files from the SVG sources after editing them. The checked-in PNG files keep normal .NET builds independent of graphics tools.

## Colors

| Color | Value | Use |
| --- | --- | --- |
| Navy | `#102A43` | Icon tile and primary wordmark |
| Teal | `#3AD9C5` | Gateway stroke |
| Deep teal | `#007F78` | Wordmark accent |
| Slate | `#486581` | Tagline |
| Neutral | `#F1F5F9` | Wordmark panel and platform icon tiles |

## Platform icon attribution

The platform glyphs are third-party marks. Their inclusion identifies operating systems; the adjacent support matrix states qualification and does not imply endorsement. Each glyph has a neutral tile and a fixed color for visibility in light and dark themes. The original path geometry is retained, scaled to fit the tile.

- **Windows:** [Bootstrap Icons `windows.svg`](https://github.com/twbs/icons/blob/8d88686c03c3768a2d82ba4f20c3c4e1b100fa29/icons/windows.svg), commit `8d88686c03c3768a2d82ba4f20c3c4e1b100fa29`. Distributed under the [MIT license](licenses/bootstrap-icons-MIT.txt).
- **Linux:** [Simple Icons `linux.svg`](https://github.com/simple-icons/simple-icons/blob/521c96fd04b0ea93034db8715eda5a4de27a58bb/icons/linux.svg), commit `521c96fd04b0ea93034db8715eda5a4de27a58bb`. Distributed under [CC0](licenses/simple-icons-CC0.txt).
- **macOS:** [Simple Icons `apple.svg`](https://github.com/simple-icons/simple-icons/blob/521c96fd04b0ea93034db8715eda5a4de27a58bb/icons/apple.svg), commit `521c96fd04b0ea93034db8715eda5a4de27a58bb`. Distributed under [CC0](licenses/simple-icons-CC0.txt).

The OS icons and badges have text alternatives in the README, and the platform names and qualification status also appear as text. Green OS badges mean that the repository has a CI lane for that OS, not a live CI result; the separate CI badge links to current workflow status. macOS stays neutral and explicitly unvalidated until it has runtime qualification.
