﻿// Decompiled with JetBrains decompiler
// Type: UnityEditor.ObjectSelectorReceiver
// Assembly: UnityEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 53BAA40C-AA1D-48D3-AA10-3FCF36D212BC
// Assembly location: C:\Program Files\Unity 5\Editor\Data\Managed\UnityEditor.dll

using UnityEngine;

namespace UnityEditor
{
  internal abstract class ObjectSelectorReceiver : ScriptableObject
  {
    public abstract void OnSelectionChanged(Object selection);

    public abstract void OnSelectionClosed(Object selection);
  }
}