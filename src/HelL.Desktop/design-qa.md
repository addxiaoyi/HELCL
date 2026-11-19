# HelL Desktop & Mobile Design QA — 2026-11-18

final result: passed for isolated desktop & mobile UI smoke; external physical-device / code-signing release gates remain open

## Target and evidence

Aligned via `/grill-me` user interview onto an **Apple macOS / iOS / iPadOS Native Minimalist** design system — eliminating AI-generated illustrations, rainbow category squares, nested card-in-card borders, and verbose slogan subtitles in favor of quiet achromatic hierarchy, 1px inset hairlines, native Segmented Controls, and generous whitespace:

- **Shared Design System (`src/HelL.Design/AvaloniaTokens.axaml`, `src/HelL.Desktop/Themes/Controls.axaml`)**:
  - **Apple Graphite Dark (`#18181A`)**: `#1D1D20` full-height sidebar, `#232327` Inset-Grouped sheets, `#2B2B30` recessed segmented tracks & inputs, `#12FFFFFF` 1px internal hairlines, `#F5F5F7` primary ink, `#8E8E93` muted ink, and crisp pure-white `#F5F5F7` pill primary CTAs (`#141416` ink).
  - **Apple Paper Light (`#F5F5F7`)**: `#ECECEF` sidebar, `#FFFFFF` Inset-Grouped sheets, `#EFEFF4` recessed fields, `#10000000` 1px hairlines, `#1D1D1F` primary ink, and crisp `#1D1D1F` obsidian pill primary CTAs (`#FFFFFF` ink).
  - 100% monochrome neutral iconography across all pages; zero decorative AI raster art (user-supplied local screenshot covers still display cleanly when set).

- **macOS Minimalist Desktop (`src/HelL.Desktop` -> `TestResults/macos-desktop-smoke/`)**:
  - Release build: **0 warnings, 0 errors**.
  - **Full-Height Seamless Sidebar (`196px`)** with geometric monochrome mark, 6 calm primary destinations (`启动`, `实例`, `资源`, `安装`, `助手`, `设置`), and compact single-line instance creator — removing the full-width top header bar and titlebar theme dropdown.
  - **Contextual macOS Segmented Controls**:
    - `实例` workspace segments: `实例 | 模组与资源 | 存档与日志 | 多人联机`
    - `设置` workspace segments: `通用与外观 | Java 环境 | 游玩统计 | 任务队列`
  - **Inset-Grouped Launch Studio**: Large instance heading, single rounded primary CTA, and one clean 4-row `1px`-divided sheet (`游戏版本`, `游戏账户`, `内存与窗口`, `Java 路径`).
  - Full `--ui-smoke` passed (`ui-smoke.json`: `ok = true`).

- **iOS / iPadOS Minimalist Mobile (`src/HelL.Mobile` -> `TestResults/macos-mobile-smoke/`)**:
  - Release build (`HelL.Mobile` + `HelL.Mobile.Preview`): **0 warnings, 0 errors**.
  - Synchronized onto the exact same Apple monochrome Inset-Grouped sheet & Segmented Control design across `390×844` portrait phone and `1180×740` wide iPadOS layout, preserving real Microsoft device-code OAuth and cloud SSE AI chat.
  - Full mobile smoke passed (`mobile-smoke.json`: `ok = true`).

## Remaining release gates

- Physical 1440p / 150%–200% DPI display captures, live modded Minecraft client launch verification, physical Android device/APK verification, and Authenticode signing for Windows `.NET Framework 4.8` executables remain open before formal release.
