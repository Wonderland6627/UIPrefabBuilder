---
name: managing-scenes
description: |
  Use for scene info, saving, finding objects. Triggers: 'scene', 'save scene', 'find by tag', 'find by name'.
metadata:
  author: indey
  version: "1.0"
---

# Managing Scenes

## API
- `SceneHelper.GetCurrentSceneName()` / `GetCurrentScenePath()`
- `SceneHelper.SaveScene()`
- `SceneHelper.MarkDirty()`
- `SceneHelper.FindByTag(tag)` / `FindByName(name)`
