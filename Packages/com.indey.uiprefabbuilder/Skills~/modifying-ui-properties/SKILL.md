---
name: modifying-ui-properties
description: |
  Use to change UI component properties. Triggers: 'set color', 'change text', 'update sprite', 'canvas group'.
metadata:
  author: indey
  version: "1.0"
---

# Modifying UI Properties

## API
- `ComponentHelper.SetText(go, "text")`
- `ComponentHelper.SetImageColor(go, Color.red)`
- `ComponentHelper.SetImageSprite(go, "Assets/...")`
- `ComponentHelper.AddCanvasGroup(go, alpha, interact, block)`
