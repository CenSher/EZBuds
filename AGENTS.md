<!-- CODEGRAPH_START -->
## CodeGraph

When a `.codegraph/` directory exists, use CodeGraph before grep/find or file reads to understand or locate code. Use `codegraph_explore` with this project path, or `codegraph explore`. If no index exists, skip CodeGraph; indexing is the user's decision.
<!-- CODEGRAPH_END -->

## Desktop behavior

Read `docs/UI_CONTRACT.md` before changing startup, tray, popup lifecycle, shadow, material, animation or menu navigation. Preserve tray-only normal startup, automatic dismissal and a content-independent shadow. Run `tools/check-ui-contract.ps1` before publishing. Keep runtime acceptance separate from compilation and code checks.

Do not use Computer Use or perform automated UI/earphone testing; the user handles those tests. Do not launch previews as part of delivery. Use an isolated source snapshot and output directory if other work is modifying/building this workspace. Preserve concurrent changes and the frozen FreeClip 2 baseline.
