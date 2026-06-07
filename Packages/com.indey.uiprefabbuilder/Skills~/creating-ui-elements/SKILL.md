---
name: creating-ui-elements
description: |
  Use when creating UGUI elements — Canvas, Panel, Button, Text, Image, InputField, Slider, Toggle, ScrollView.
  Triggers: 'create UI', 'add button', 'make menu', 'build interface', 'add panel'.
metadata:
  author: indey
  version: "1.0"
---

# Creating UI Elements

## Instructions
1. Ensure Canvas exists (UICreator.CreateCanvas)
2. Create container Panel if grouping
3. Create child elements (CreateButton, CreateText, CreateImage, etc.)
4. Apply LayoutGroup if multiple children

## API
| Method | Description |
|--------|------------|
| CreateCanvas(name, mode) | Canvas + Scaler + Raycaster |
| CreatePanel(name, parent, color) | Panel with Image |
| CreateButton(name, parent, text, size) | Button + Text |
| CreateText(name, parent, content, fontSize, color) | TMP auto-detect |
| CreateImage(name, parent, spritePath, size) | Image |
| CreateBatch(items) | Batch create |

## Example
```csharp
var canvas = UICreator.CreateCanvas("MenuCanvas");
var panel = UICreator.CreatePanel("Panel", canvas.transform, new Color(0,0,0,0.8f));
RectTransformHelper.SetAnchorPreset(panel, AnchorPreset.MiddleCenter);
RectTransformHelper.SetSize(panel, 300, 400);
UICreator.CreateButton("Btn", panel.transform, "Click", new Vector2(200, 40));
LayoutHelper.AddVerticalLayout(panel, spacing: 12);
```
