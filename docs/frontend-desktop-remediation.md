# Avalonia Desktop remediation — 2026-11-19

Work was isolated in `D:/qwq/qdq/.tmp/frontend-desktop` on the complete GUI
foundation `4313450`. Existing visuals and application capabilities are retained.
No commit, push or original-workspace edit was made by this agent.

## Audit disposition (68–99)

68. World deletion now confirms the exact instance and world, with explicit backup/recovery warning.
69. Restore shows the target, backup and current-world modification information before overwrite confirmation.
70. Backup deletion confirms the backup name and size/details and warns about removing the only recovery point.
71. Mod/resource deletion confirms instance, resource kind and filename, with explicit recovery guidance.
72. Server removal confirms the record and exposes a one-record undo retaining its name, address, port and description.
73. Batch mod enable/disable evaluates the current filter at execution time. Hidden resources remain untouched; buttons say current filter.
74. Log analysis loads a redacted preview first. Sending that preview requires a second confirmation displaying the receiving host/mode and exact content.
75. Log reading runs off the UI thread and reads at most 64 KiB from the file tail, rather than reading the entire file.
76. Failed AI prompts remain in the composer and expose a retry button.
77. Streaming follows only if the reader was near the bottom. A jump-to-latest control appears when reading older content.
78. AI document rendering now distinguishes headings, lists as separate readable lines, fenced monospace code with copying, and explicit safe HTTP(S) link buttons.
79. Clearing a conversation requires confirmation. Declining preserves it.
80. AI initialization failure shows a safe configuration/key-store repair hint and logs only the exception type.
81. Library and resource pages remain browseable while a task runs; conflicting writes still go through the owner gate.
82. Clicking a blocked action reports the currently running task instead of silently doing nothing.
83. Navigating during work labels data as pending refresh; task completion refreshes the visible page.
84. Full target-instance text is restored on world/mod/server pages and in the page chrome, with wrapping/tooltip.
85. Repeated top-level segmented navigation is hidden; the side navigation is the single main location model.
86. The side rail narrows from 208 to 164 pixels below 1000px window width.
87. Settings is fixed outside the scrolling navigation list.
88. An explicit readable-text setting raises small labels to at least 13px and scales other labels. Actual native screen-reader/high-contrast certification is not claimed.
89. Instance mod/save icon buttons have explicit names including the instance name. Native screen-reader behavior still needs device testing.
90. Filter changes retain the previous result list and trigger a safe refresh; failures retain those results with an error notice.
91. Resource discovery uses two columns below its narrow breakpoint and four when there is room.
92. Source/sort controls move to a second row in narrow layouts; the query uses the available first-row width.
93. Resource details load real upstream gallery, full body and selected-file changelog using the shared typed Details API. Failure presents a retry action; upstream absence is stated explicitly.
94. Real offset/page-size requests back previous/next paging. No fictional total is shown; next remains available for every nonempty batch because alias deduplication can shorten a page before upstream exhaustion.
95. Task logs have copy and expand/collapse actions and a larger initial area.
96. Up to 100 terminal tasks survive restart in an atomically replaced, bounded, redacted local JSON history. Running tasks are not resurrected; corrupt files remain intact and are reported.
97. Only explicit read/refresh operations get a retry capability. Deletes, overwrite, login, game launching and arbitrary operations are never replayed through this control.
98. Java wording is now “用于当前实例”, matching its effect.
99. Version installation reads the typed upstream plan in the background and confirms target, published download bytes, free space and existing-file policy. Unknown parts remain labelled unknown; known insufficient space blocks installation. Failure allows cancel/retry or an explicit continue, not an invented estimate.

## Verification

- `dotnet build src/HelL.Desktop/HelL.Desktop.csproj -c Release -v quiet`: passed.
  Fresh shared Core builds retain 16 pre-existing warnings; the final incremental
  Desktop build reported 0 warnings / 0 errors.
- `dotnet test tests/HelL.Desktop.Tests -c Release`: 6/6 passed. These assert bounded
  tail reads, token/localized-credential redaction, terminal-only persistent
  history, corrupt-history evidence preservation and restart behavior.
- Existing desktop UI smoke passed at 800/1060/1180 sizes, dark/light themes,
  task failure/cancellation, installed modules, navigation and invalid metadata.
- Extended actual desktop smoke verifies filtered batch state, declined delete
  preserving the file, structured code blocks, narrow navigation and task history.
  It exposed a queued-filter race first; the implementation now evaluates the
  current filter when the batch is submitted rather than trusting a stale view.

Evidence: `TestResults/desktop-remediation/desktop-recovery-final.trx`,
`TestResults/desktop-remediation-pass/{ui-smoke.json,frontend-recovery.json}`,
and `desktop-remediation-build-final.log`. Final-head smoke repeats these checks.

Native screen-reader, real clipboard/browser/document-provider interaction and
public upstream requests are not implied by fixture-hosted UI smoke. Those remain
explicit environment acceptance, not missing implementations. The shared root
agent supplied pagination/Details/InstallationPlan APIs; they are not part of this
agent's owned diff.
