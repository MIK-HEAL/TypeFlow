# Third-party sources and assets

This is a development prototype, not a redistribution-ready installer.

- The surrounding librime checkout is BSD-3-Clause; see `../LICENSE`.
- `native/rime_bridge.cpp` calls its public API. The local development runtime comes from the official librime 1.17.0 clang x64 release. That release's `version-info.txt` also lists librime-lua (`ec52e48`), librime-octagram (`dfcc151`) and librime-predict (`920bd41`). A minimal custom runtime or a complete dependency and plugin license inventory is required before packaging for distribution.
- Rime's dependency archive supplies OpenCC conversion data and upstream native dependencies. See the respective projects' license terms; do not assume the top-level BSD license covers all binaries and data.
- The fetched `rime-luna-pinyin`, `rime-prelude`, and `rime-essay` snapshots include LGPL-3.0 license files, preserved in `rime-data/licenses/`. Repositories, immutable commits and archive hashes are recorded in `sources.json`.
- Optional Weasel 0.17.4 is GPL-3.0. The downloaded source retains its license. `integration/weasel/Apply-Bridge.ps1` modifies only its optional UI integration points and keeps a backup. Distributing a modified Weasel requires preserving its license and supplying corresponding source and changes.
- The SSF test fixture was supplied by the user. Its `General/skin_author` is `忧郁游戏`. Its images and derived screenshots are for local testing; no redistribution license has been declared. The original fixture is never modified and the converted artwork is not included in the product source tree.
- `fkxxyz/ssfconv` (GPL-3.0) was consulted to check BGR color order and the order of SSF margin fields. Its code and dependencies are not bundled or executed. The initial ZIP importer is implemented separately with .NET's built-in compression and encoding facilities; encrypted SSF conversion is not implemented.

TypeFlow-specific product code has not been assigned a separate distribution license yet. Resolve the product license and complete the upstream binary inventory before a public release.
