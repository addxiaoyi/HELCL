# Mobile vector icons and navigation details

Baseline: `1d147f0090ca04f3c4cad3e9ab4b33bd64981125`.

The mobile client now renders the shared `HelL.Design.IconCatalog` drawings through `LauncherIcon`, using a 24-unit frame, 1.8-unit rounded strokes and a heavier active state. Existing Material icon markup and its package dependency are removed from this client. The brand mark, page layout and navigation destinations remain intact.

## Changes

- Launch, instance library, resources, AI, account and settings retain their semantic drawing when selected. Selection changes drawing weight and the existing foreground/background state rather than replacing the symbol with an unrelated shape.
- Settings previously became an account icon after navigation. It now consistently uses the settings drawing; its page title and tooltip also say “设置”. The launch tab's accessible name matches its visible “启动” label.
- The wide side rail now highlights its independent account entry on the account page, while the phone's five-tab navigation continues to place account under settings.
- Existing action names and 44-pixel mobile button targets are preserved. Icon controls are decorative, do not receive pointer input or keyboard focus, and use the Raw accessibility view. Text and owning buttons carry the action names.
- Navigation drawings inherit the owning button's foreground in dark and light themes. The primary add drawing follows its action foreground. User text scaling does not multiply icon geometry or change the existing safe-area layout.

## Failure-first and runtime evidence

The baseline full 390×844 smoke exited **1** with five explicit failures: selected settings used an account symbol, the settings page title said “我的”, the launch accessible name said “首页”, the settings tooltip said “我的”, and the side-rail account entry was never selected. The original screenshot and `mobile-icons.json` remain under `TestResults/frontend-icons-20261120/mobile/red-390x844/`.

`CheckMobileIconsAsync` is called by the existing full smoke chain, including Android native smoke. Its final receipt contains **69 checks**: actual navigation state and semantics, 24 transparent bitmap samples covering eight drawings at 16/20/24 pixels, decorative input behavior, active drawing weight, and theme foreground behavior. Pixel samples verify rendered outline ink rather than only checking a control type or a path string. The checks preserve all earlier draft, focus, pagination, safety and system-font regressions.

Final source was rebuilt with `--no-incremental` after the shared settings drawing was centered. The build exited **0** with **0 errors** and the baseline Core's **16 existing platform/obsolete-API warnings**; no warning policy was changed. The ordinary incremental final UI build exited **0**, with **0 warnings and 0 errors**. Mobile service tests passed **57/57**.

Full ordinary previews at **320×720** and **390×844**, full system-font **200%** previews at the same sizes, and remote preview **390×844** all exited **0**. Each ordinary/system-font run produced a successful 69-check icon receipt and preserved existing smoke success. Settings and home screenshots in both themes were inspected; the 200% narrow layout retains the main action and complete tab labels.

Evidence is in the parent workspace's `TestResults/frontend-icons-20261120/mobile/`, including `frozen-build.log`, `tests/mobile.trx`, `final-320x720/`, `final-390x844/`, `final-2x-320x720/`, `final-2x-390x844/` and `final-remote-390x844/`. Generated profiles, PNGs and temporary keys are excluded from the source handoff.

This increment's executed previews are Windows-hosted Avalonia UI. Native Android acceptance belongs to the integrated branch's CI. Physical touch, TalkBack and other vendor device behavior remain unverified here.
