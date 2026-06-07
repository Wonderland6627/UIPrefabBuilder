---
name: managing-prefabs
description: |
  Use for creating, instantiating, applying prefabs. Triggers: 'prefab', 'save prefab', 'instantiate', 'apply changes'.
metadata:
  author: indey
  version: "1.0"
---

# Managing Prefabs

## API
- `PrefabHelper.Create(source, "Assets/Prefabs/Name.prefab")`
- `PrefabHelper.Instantiate(path, parent, name)`
- `PrefabHelper.Apply(instance)`
- `PrefabHelper.FindInstances(path)`
