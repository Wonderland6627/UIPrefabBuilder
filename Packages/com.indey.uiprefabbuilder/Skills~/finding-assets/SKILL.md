---
name: finding-assets
description: |
  Use to search project assets. Triggers: 'find sprite', 'search prefab', 'locate asset', 'asset database'.
metadata:
  author: indey
  version: "1.0"
---

# Finding Assets

## API
- `AssetFinder.Find("t:Sprite icon", limit: 20)` returns paths
- `AssetFinder.GetInfo(path)` returns description
