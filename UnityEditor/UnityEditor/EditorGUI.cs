using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor.SceneManagement;
using UnityEditor.Scripting.ScriptCompilation;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.Scripting;

namespace UnityEditor
{
	public sealed class EditorGUI
	{
		public class DisabledGroupScope : GUI.Scope
		{
			public DisabledGroupScope(bool disabled)
			{
				EditorGUI.BeginDisabledGroup(disabled);
			}

			protected override void CloseScope()
			{
				EditorGUI.EndDisabledGroup();
			}
		}

		public struct DisabledScope : IDisposable
		{
			private bool m_Disposed;

			public DisabledScope(bool disabled)
			{
				this.m_Disposed = false;
				EditorGUI.BeginDisabled(disabled);
			}

			public void Dispose()
			{
				if (!this.m_Disposed)
				{
					this.m_Disposed = true;
					if (!GUIUtility.guiIsExiting)
					{
						EditorGUI.EndDisabled();
					}
				}
			}
		}

		public class ChangeCheckScope : GUI.Scope
		{
			private bool m_ChangeChecked;

			private bool m_Changed;

			public bool changed
			{
				get
				{
					if (!this.m_ChangeChecked)
					{
						this.m_ChangeChecked = true;
						this.m_Changed = EditorGUI.EndChangeCheck();
					}
					return this.m_Changed;
				}
			}

			public ChangeCheckScope()
			{
				EditorGUI.BeginChangeCheck();
			}

			protected override void CloseScope()
			{
				if (!this.m_ChangeChecked)
				{
					EditorGUI.EndChangeCheck();
				}
			}
		}

		internal class RecycledTextEditor : TextEditor
		{
			internal static bool s_ActuallyEditing = false;

			internal static bool s_AllowContextCutOrPaste = true;

			private IMECompositionMode m_IMECompositionModeBackup;

			internal bool IsEditingControl(int id)
			{
				return GUIUtility.keyboardControl == id && this.controlID == id && EditorGUI.RecycledTextEditor.s_ActuallyEditing && GUIView.current.hasFocus;
			}

			public virtual void BeginEditing(int id, string newText, Rect position, GUIStyle style, bool multiline, bool passwordField)
			{
				if (!this.IsEditingControl(id))
				{
					if (EditorGUI.activeEditor != null)
					{
						EditorGUI.activeEditor.EndEditing();
					}
					EditorGUI.activeEditor = this;
					this.controlID = id;
					EditorGUI.s_OriginalText = newText;
					base.text = newText;
					this.multiline = multiline;
					this.style = style;
					base.position = position;
					this.isPasswordField = passwordField;
					EditorGUI.RecycledTextEditor.s_ActuallyEditing = true;
					this.scrollOffset = Vector2.zero;
					UnityEditor.Undo.IncrementCurrentGroup();
					this.m_IMECompositionModeBackup = Input.imeCompositionMode;
					Input.imeCompositionMode = IMECompositionMode.On;
				}
			}

			public virtual void EndEditing()
			{
				if (EditorGUI.activeEditor == this)
				{
					EditorGUI.activeEditor = null;
				}
				this.controlID = 0;
				EditorGUI.RecycledTextEditor.s_ActuallyEditing = false;
				EditorGUI.RecycledTextEditor.s_AllowContextCutOrPaste = true;
				UnityEditor.Undo.IncrementCurrentGroup();
				Input.imeCompositionMode = this.m_IMECompositionModeBackup;
			}
		}

		internal sealed class DelayedTextEditor : EditorGUI.RecycledTextEditor
		{
			private int controlThatHadFocus = 0;

			private int messageControl = 0;

			internal string controlThatHadFocusValue = "";

			private GUIView viewThatHadFocus;

			private bool m_CommitCommandSentOnLostFocus;

			private const string CommitCommand = "DelayedControlShouldCommit";

			private bool m_IgnoreBeginGUI = false;

			public void BeginGUI()
			{
				if (!this.m_IgnoreBeginGUI)
				{
					if (GUIUtility.keyboardControl == this.controlID)
					{
						this.controlThatHadFocus = GUIUtility.keyboardControl;
						this.controlThatHadFocusValue = base.text;
						this.viewThatHadFocus = GUIView.current;
					}
					else
					{
						this.controlThatHadFocus = 0;
					}
				}
			}

			public void EndGUI(EventType type)
			{
				int num = 0;
				if (this.controlThatHadFocus != 0 && this.controlThatHadFocus != GUIUtility.keyboardControl)
				{
					num = this.controlThatHadFocus;
					this.controlThatHadFocus = 0;
				}
				if (num != 0 && !this.m_CommitCommandSentOnLostFocus)
				{
					this.messageControl = num;
					this.m_IgnoreBeginGUI = true;
					if (GUIView.current == this.viewThatHadFocus)
					{
						this.viewThatHadFocus.SetKeyboardControl(GUIUtility.keyboardControl);
					}
					this.viewThatHadFocus.SendEvent(EditorGUIUtility.CommandEvent("DelayedControlShouldCommit"));
					this.m_IgnoreBeginGUI = false;
					this.messageControl = 0;
				}
			}

			public override void EndEditing()
			{
				if (Event.current == null)
				{
					this.m_CommitCommandSentOnLostFocus = true;
					this.m_IgnoreBeginGUI = true;
					this.messageControl = this.controlID;
					int keyboardControl = GUIUtility.keyboardControl;
					this.viewThatHadFocus.SetKeyboardControl(0);
					this.viewThatHadFocus.SendEvent(EditorGUIUtility.CommandEvent("DelayedControlShouldCommit"));
					this.m_IgnoreBeginGUI = false;
					this.viewThatHadFocus.SetKeyboardControl(keyboardControl);
					this.messageControl = 0;
				}
				base.EndEditing();
			}

			public string OnGUI(int id, string value, out bool changed)
			{
				Event current = Event.current;
				string result;
				if (current.type == EventType.ExecuteCommand && current.commandName == "DelayedControlShouldCommit" && id == this.messageControl)
				{
					this.m_CommitCommandSentOnLostFocus = false;
					changed = (value != this.controlThatHadFocusValue);
					current.Use();
					this.messageControl = 0;
					result = this.controlThatHadFocusValue;
				}
				else
				{
					changed = false;
					result = value;
				}
				return result;
			}
		}

		internal sealed class PopupMenuEvent
		{
			public string commandName;

			public GUIView receiver;

			public PopupMenuEvent(string cmd, GUIView v)
			{
				this.commandName = cmd;
				this.receiver = v;
			}

			public void SendEvent()
			{
				if (this.receiver)
				{
					this.receiver.SendEvent(EditorGUIUtility.CommandEvent(this.commandName));
				}
				else
				{
					Debug.LogError("BUG: We don't have a receiver set up, please report");
				}
			}
		}

		public class IndentLevelScope : GUI.Scope
		{
			private int m_IndentOffset;

			public IndentLevelScope() : this(1)
			{
			}

			public IndentLevelScope(int increment)
			{
				this.m_IndentOffset = increment;
				EditorGUI.indentLevel += this.m_IndentOffset;
			}

			protected override void CloseScope()
			{
				EditorGUI.indentLevel -= this.m_IndentOffset;
			}
		}

		internal sealed class PopupCallbackInfo
		{
			public static EditorGUI.PopupCallbackInfo instance = null;

			internal const string kPopupMenuChangedMessage = "PopupMenuChanged";

			private int m_ControlID = 0;

			private int m_SelectedIndex = 0;

			private GUIView m_SourceView;

			public PopupCallbackInfo(int controlID)
			{
				this.m_ControlID = controlID;
				this.m_SourceView = GUIView.current;
			}

			public static int GetSelectedValueForControl(int controlID, int selected)
			{
				Event current = Event.current;
				int result;
				if (current.type == EventType.ExecuteCommand && current.commandName == "PopupMenuChanged")
				{
					if (EditorGUI.PopupCallbackInfo.instance == null)
					{
						Debug.LogError("Popup menu has no instance");
						result = selected;
						return result;
					}
					if (EditorGUI.PopupCallbackInfo.instance.m_ControlID == controlID)
					{
						selected = EditorGUI.PopupCallbackInfo.instance.m_SelectedIndex;
						EditorGUI.PopupCallbackInfo.instance = null;
						GUI.changed = true;
						current.Use();
					}
				}
				result = selected;
				return result;
			}

			internal void SetEnumValueDelegate(object userData, string[] options, int selected)
			{
				this.m_SelectedIndex = selected;
				if (this.m_SourceView)
				{
					this.m_SourceView.SendEvent(EditorGUIUtility.CommandEvent("PopupMenuChanged"));
				}
			}
		}

		private struct EnumData
		{
			public Enum[] values;

			public int[] flagValues;

			public string[] displayNames;

			public bool flags;

			public Type underlyingType;

			public bool unsigned;

			public bool serializable;
		}

		internal enum PropertyVisibility
		{
			All,
			OnlyVisible
		}

		public class PropertyScope : GUI.Scope
		{
			public GUIContent content
			{
				get;
				protected set;
			}

			public PropertyScope(Rect totalPosition, GUIContent label, SerializedProperty property)
			{
				this.content = EditorGUI.BeginProperty(totalPosition, label, property);
			}

			protected override void CloseScope()
			{
				EditorGUI.EndProperty();
			}
		}

		internal sealed class GUIContents
		{
			private class IconName : Attribute
			{
				private string m_Name;

				public virtual string name
				{
					get
					{
						return this.m_Name;
					}
				}

				public IconName(string name)
				{
					this.m_Name = name;
				}
			}

			[EditorGUI.GUIContents.IconName("_Popup")]
			internal static GUIContent titleSettingsIcon
			{
				get;
				private set;
			}

			[EditorGUI.GUIContents.IconName("_Help")]
			internal static GUIContent helpIcon
			{
				get;
				private set;
			}

			static GUIContents()
			{
				PropertyInfo[] properties = typeof(EditorGUI.GUIContents).GetProperties(BindingFlags.Static | BindingFlags.NonPublic);
				PropertyInfo[] array = properties;
				for (int i = 0; i < array.Length; i++)
				{
					PropertyInfo propertyInfo = array[i];
					EditorGUI.GUIContents.IconName[] array2 = (EditorGUI.GUIContents.IconName[])propertyInfo.GetCustomAttributes(typeof(EditorGUI.GUIContents.IconName), false);
					if (array2.Length > 0)
					{
						string name = array2[0].name;
						GUIContent value = EditorGUIUtility.IconContent(name);
						propertyInfo.SetValue(null, value, null);
					}
				}
			}
		}

		private static class Resizer
		{
			private static float s_StartSize;

			private static Vector2 s_MouseDeltaReaderStartPos;

			internal static float Resize(Rect position, float size, float minSize, float maxSize, bool horizontal, out bool hasControl)
			{
				int controlID = GUIUtility.GetControlID(EditorGUI.s_MouseDeltaReaderHash, FocusType.Passive, position);
				Event current = Event.current;
				switch (current.GetTypeForControl(controlID))
				{
				case EventType.MouseDown:
					if (GUIUtility.hotControl == 0 && position.Contains(current.mousePosition) && current.button == 0)
					{
						GUIUtility.hotControl = controlID;
						GUIUtility.keyboardControl = 0;
						EditorGUI.Resizer.s_MouseDeltaReaderStartPos = GUIClip.Unclip(current.mousePosition);
						EditorGUI.Resizer.s_StartSize = size;
						current.Use();
					}
					break;
				case EventType.MouseUp:
					if (GUIUtility.hotControl == controlID && current.button == 0)
					{
						GUIUtility.hotControl = 0;
						current.Use();
					}
					break;
				case EventType.MouseDrag:
					if (GUIUtility.hotControl == controlID)
					{
						current.Use();
						Vector2 a = GUIClip.Unclip(current.mousePosition);
						float num = (!horizontal) ? (a - EditorGUI.Resizer.s_MouseDeltaReaderStartPos).y : (a - EditorGUI.Resizer.s_MouseDeltaReaderStartPos).x;
						float num2 = EditorGUI.Resizer.s_StartSize + num;
						if (num2 >= minSize && num2 <= maxSize)
						{
							size = num2;
						}
						else
						{
							size = Mathf.Clamp(num2, minSize, maxSize);
						}
					}
					break;
				case EventType.Repaint:
				{
					MouseCursor mouse = (!horizontal) ? MouseCursor.SplitResizeUpDown : MouseCursor.SplitResizeLeftRight;
					EditorGUIUtility.AddCursorRect(position, mouse, controlID);
					break;
				}
				}
				hasControl = (GUIUtility.hotControl == controlID);
				return size;
			}
		}

		internal struct KnobContext
		{
			private readonly Rect position;

			private readonly Vector2 knobSize;

			private readonly float currentValue;

			private readonly float start;

			private readonly float end;

			private readonly string unit;

			private readonly Color activeColor;

			private readonly Color backgroundColor;

			private readonly bool showValue;

			private readonly int id;

			private const int kPixelRange = 250;

			private static Material knobMaterial;

			public KnobContext(Rect position, Vector2 knobSize, float currentValue, float start, float end, string unit, Color backgroundColor, Color activeColor, bool showValue, int id)
			{
				this.position = position;
				this.knobSize = knobSize;
				this.currentValue = currentValue;
				this.start = start;
				this.end = end;
				this.unit = unit;
				this.activeColor = activeColor;
				this.backgroundColor = backgroundColor;
				this.showValue = showValue;
				this.id = id;
			}

			public float Handle()
			{
				float result;
				if (this.KnobState().isEditing && this.CurrentEventType() != EventType.Repaint)
				{
					result = this.DoKeyboardInput();
				}
				else
				{
					switch (this.CurrentEventType())
					{
					case EventType.MouseDown:
						result = this.OnMouseDown();
						return result;
					case EventType.MouseUp:
						result = this.OnMouseUp();
						return result;
					case EventType.MouseDrag:
						result = this.OnMouseDrag();
						return result;
					case EventType.Repaint:
						result = this.OnRepaint();
						return result;
					}
					result = this.currentValue;
				}
				return result;
			}

			private EventType CurrentEventType()
			{
				return this.CurrentEvent().type;
			}

			private bool IsEmptyKnob()
			{
				return this.start == this.end;
			}

			private Event CurrentEvent()
			{
				return Event.current;
			}

			private float Clamp(float value)
			{
				return Mathf.Clamp(value, this.MinValue(), this.MaxValue());
			}

			private float ClampedCurrentValue()
			{
				return this.Clamp(this.currentValue);
			}

			private float MaxValue()
			{
				return Mathf.Max(this.start, this.end);
			}

			private float MinValue()
			{
				return Mathf.Min(this.start, this.end);
			}

			private float GetCurrentValuePercent()
			{
				return (this.ClampedCurrentValue() - this.MinValue()) / (this.MaxValue() - this.MinValue());
			}

			private float MousePosition()
			{
				return this.CurrentEvent().mousePosition.y - this.position.y;
			}

			private bool WasDoubleClick()
			{
				return this.CurrentEventType() == EventType.MouseDown && this.CurrentEvent().clickCount == 2;
			}

			private float ValuesPerPixel()
			{
				return 250f / (this.MaxValue() - this.MinValue());
			}

			private KnobState KnobState()
			{
				return (KnobState)GUIUtility.GetStateObject(typeof(KnobState), this.id);
			}

			private void StartDraggingWithValue(float dragStartValue)
			{
				KnobState knobState = this.KnobState();
				knobState.dragStartPos = this.MousePosition();
				knobState.dragStartValue = dragStartValue;
				knobState.isDragging = true;
			}

			private float OnMouseDown()
			{
				float result;
				if (!this.position.Contains(this.CurrentEvent().mousePosition) || this.KnobState().isEditing || this.IsEmptyKnob())
				{
					result = this.currentValue;
				}
				else
				{
					GUIUtility.hotControl = this.id;
					if (this.WasDoubleClick())
					{
						this.KnobState().isEditing = true;
					}
					else
					{
						this.StartDraggingWithValue(this.ClampedCurrentValue());
					}
					this.CurrentEvent().Use();
					result = this.currentValue;
				}
				return result;
			}

			private float OnMouseDrag()
			{
				float result;
				if (GUIUtility.hotControl != this.id)
				{
					result = this.currentValue;
				}
				else
				{
					KnobState knobState = this.KnobState();
					if (!knobState.isDragging)
					{
						result = this.currentValue;
					}
					else
					{
						GUI.changed = true;
						this.CurrentEvent().Use();
						float num = knobState.dragStartPos - this.MousePosition();
						float value = knobState.dragStartValue + num / this.ValuesPerPixel();
						result = this.Clamp(value);
					}
				}
				return result;
			}

			private float OnMouseUp()
			{
				if (GUIUtility.hotControl == this.id)
				{
					this.CurrentEvent().Use();
					GUIUtility.hotControl = 0;
					this.KnobState().isDragging = false;
				}
				return this.currentValue;
			}

			private void PrintValue()
			{
				Rect rect = new Rect(this.position.x + this.knobSize.x / 2f - 8f, this.position.y + this.knobSize.y / 2f - 8f, this.position.width, 20f);
				string str = this.currentValue.ToString("0.##");
				GUI.Label(rect, str + " " + this.unit);
			}

			private float DoKeyboardInput()
			{
				GUI.SetNextControlName("KnobInput");
				GUI.FocusControl("KnobInput");
				EditorGUI.BeginChangeCheck();
				Rect rect = new Rect(this.position.x + this.knobSize.x / 2f - 6f, this.position.y + this.knobSize.y / 2f - 7f, this.position.width, 20f);
				GUIStyle none = GUIStyle.none;
				none.normal.textColor = new Color(0.703f, 0.703f, 0.703f, 1f);
				none.fontStyle = FontStyle.Normal;
				string text = EditorGUI.DelayedTextField(rect, this.currentValue.ToString("0.##"), none);
				float result;
				if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(text))
				{
					this.KnobState().isEditing = false;
					float num;
					if (float.TryParse(text, out num) && num != this.currentValue)
					{
						result = this.Clamp(num);
						return result;
					}
				}
				result = this.currentValue;
				return result;
			}

			private static void CreateKnobMaterial()
			{
				if (!EditorGUI.KnobContext.knobMaterial)
				{
					Shader shader = AssetDatabase.GetBuiltinExtraResource(typeof(Shader), "Internal-GUITextureClip.shader") as Shader;
					EditorGUI.KnobContext.knobMaterial = new Material(shader);
					EditorGUI.KnobContext.knobMaterial.hideFlags = HideFlags.HideAndDontSave;
					EditorGUI.KnobContext.knobMaterial.mainTexture = EditorGUIUtility.FindTexture("KnobCShape");
					EditorGUI.KnobContext.knobMaterial.name = "Knob Material";
					if (EditorGUI.KnobContext.knobMaterial.mainTexture == null)
					{
						Debug.Log("Did not find 'KnobCShape'");
					}
				}
			}

			private Vector3 GetUVForPoint(Vector3 point)
			{
				Vector3 result = new Vector3((point.x - this.position.x) / this.knobSize.x, (point.y - this.position.y - this.knobSize.y) / -this.knobSize.y);
				return result;
			}

			private void VertexPointWithUV(Vector3 point)
			{
				GL.TexCoord(this.GetUVForPoint(point));
				GL.Vertex(point);
			}

			private void DrawValueArc(float angle)
			{
				if (Event.current.type == EventType.Repaint)
				{
					EditorGUI.KnobContext.CreateKnobMaterial();
					Vector3 point = new Vector3(this.position.x + this.knobSize.x / 2f, this.position.y + this.knobSize.y / 2f, 0f);
					Vector3 point2 = new Vector3(this.position.x + this.knobSize.x, this.position.y + this.knobSize.y / 2f, 0f);
					Vector3 vector = new Vector3(this.position.x + this.knobSize.x, this.position.y + this.knobSize.y, 0f);
					Vector3 vector2 = new Vector3(this.position.x, this.position.y + this.knobSize.y, 0f);
					Vector3 vector3 = new Vector3(this.position.x, this.position.y, 0f);
					Vector3 point3 = new Vector3(this.position.x + this.knobSize.x, this.position.y, 0f);
					EditorGUI.KnobContext.knobMaterial.SetPass(0);
					GL.Begin(7);
					GL.Color(this.backgroundColor);
					this.VertexPointWithUV(vector3);
					this.VertexPointWithUV(point3);
					this.VertexPointWithUV(vector);
					this.VertexPointWithUV(vector2);
					GL.End();
					GL.Begin(4);
					GL.Color(this.activeColor * ((!GUI.enabled) ? 0.5f : 1f));
					if (angle > 0f)
					{
						this.VertexPointWithUV(point);
						this.VertexPointWithUV(point2);
						this.VertexPointWithUV(vector);
						Vector3 point4 = vector;
						if (angle > 1.57079637f)
						{
							this.VertexPointWithUV(point);
							this.VertexPointWithUV(vector);
							this.VertexPointWithUV(vector2);
							point4 = vector2;
							if (angle > 3.14159274f)
							{
								this.VertexPointWithUV(point);
								this.VertexPointWithUV(vector2);
								this.VertexPointWithUV(vector3);
								point4 = vector3;
							}
						}
						if (angle == 4.712389f)
						{
							this.VertexPointWithUV(point);
							this.VertexPointWithUV(vector3);
							this.VertexPointWithUV(point3);
							this.VertexPointWithUV(point);
							this.VertexPointWithUV(point3);
							this.VertexPointWithUV(point2);
						}
						else
						{
							float num = angle + 0.7853982f;
							Vector3 point5;
							if (angle < 1.57079637f)
							{
								point5 = vector + new Vector3(this.knobSize.y / 2f * Mathf.Tan(1.57079637f - num) - this.knobSize.x / 2f, 0f, 0f);
							}
							else if (angle < 3.14159274f)
							{
								point5 = vector2 + new Vector3(0f, this.knobSize.x / 2f * Mathf.Tan(3.14159274f - num) - this.knobSize.y / 2f, 0f);
							}
							else
							{
								point5 = vector3 + new Vector3(-(this.knobSize.y / 2f * Mathf.Tan(4.712389f - num)) + this.knobSize.x / 2f, 0f, 0f);
							}
							this.VertexPointWithUV(point);
							this.VertexPointWithUV(point4);
							this.VertexPointWithUV(point5);
						}
					}
					GL.End();
				}
			}

			private float OnRepaint()
			{
				this.DrawValueArc(this.GetCurrentValuePercent() * 3.14159274f * 1.5f);
				float result;
				if (this.KnobState().isEditing)
				{
					result = this.DoKeyboardInput();
				}
				else
				{
					if (this.showValue)
					{
						this.PrintValue();
					}
					result = this.currentValue;
				}
				return result;
			}
		}

		[Flags]
		internal enum ObjectFieldValidatorOptions
		{
			None = 0,
			ExactObjectTypeValidation = 1
		}

		internal delegate UnityEngine.Object ObjectFieldValidator(UnityEngine.Object[] references, Type objType, SerializedProperty property, EditorGUI.ObjectFieldValidatorOptions options);

		internal enum ObjectFieldVisualType
		{
			IconAndText,
			LargePreview,
			MiniPreview
		}

		internal class VUMeter
		{
			public struct SmoothingData
			{
				public float lastValue;

				public float peakValue;

				public float peakValueTime;
			}

			private static Texture2D s_VerticalVUTexture;

			private static Texture2D s_HorizontalVUTexture;

			private const float VU_SPLIT = 0.9f;

			public static Texture2D verticalVUTexture
			{
				get
				{
					if (EditorGUI.VUMeter.s_VerticalVUTexture == null)
					{
						EditorGUI.VUMeter.s_VerticalVUTexture = EditorGUIUtility.LoadIcon("VUMeterTextureVertical");
					}
					return EditorGUI.VUMeter.s_VerticalVUTexture;
				}
			}

			public static Texture2D horizontalVUTexture
			{
				get
				{
					if (EditorGUI.VUMeter.s_HorizontalVUTexture == null)
					{
						EditorGUI.VUMeter.s_HorizontalVUTexture = EditorGUIUtility.LoadIcon("VUMeterTextureHorizontal");
					}
					return EditorGUI.VUMeter.s_HorizontalVUTexture;
				}
			}

			public static void HorizontalMeter(Rect position, float value, float peak, Texture2D foregroundTexture, Color peakColor)
			{
				if (Event.current.type == EventType.Repaint)
				{
					Color color = GUI.color;
					EditorStyles.progressBarBack.Draw(position, false, false, false, false);
					GUI.color = new Color(1f, 1f, 1f, (!GUI.enabled) ? 0.5f : 1f);
					float num = position.width * value - 2f;
					if (num < 2f)
					{
						num = 2f;
					}
					Rect position2 = new Rect(position.x + 1f, position.y + 1f, num, position.height - 2f);
					Rect texCoords = new Rect(0f, 0f, value, 1f);
					GUI.DrawTextureWithTexCoords(position2, foregroundTexture, texCoords);
					GUI.color = peakColor;
					float num2 = position.width * peak - 2f;
					if (num2 < 2f)
					{
						num2 = 2f;
					}
					position2 = new Rect(position.x + num2, position.y + 1f, 1f, position.height - 2f);
					GUI.DrawTexture(position2, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill);
					GUI.color = color;
				}
			}

			public static void VerticalMeter(Rect position, float value, float peak, Texture2D foregroundTexture, Color peakColor)
			{
				if (Event.current.type == EventType.Repaint)
				{
					Color color = GUI.color;
					EditorStyles.progressBarBack.Draw(position, false, false, false, false);
					GUI.color = new Color(1f, 1f, 1f, (!GUI.enabled) ? 0.5f : 1f);
					float num = (position.height - 2f) * value;
					if (num < 2f)
					{
						num = 2f;
					}
					Rect position2 = new Rect(position.x + 1f, position.y + position.height - 1f - num, position.width - 2f, num);
					Rect texCoords = new Rect(0f, 0f, 1f, value);
					GUI.DrawTextureWithTexCoords(position2, foregroundTexture, texCoords);
					GUI.color = peakColor;
					float num2 = (position.height - 2f) * peak;
					if (num2 < 2f)
					{
						num2 = 2f;
					}
					position2 = new Rect(position.x + 1f, position.y + position.height - 1f - num2, position.width - 2f, 1f);
					GUI.DrawTexture(position2, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill);
					GUI.color = color;
				}
			}

			public static void HorizontalMeter(Rect position, float value, ref EditorGUI.VUMeter.SmoothingData data, Texture2D foregroundTexture, Color peakColor)
			{
				if (Event.current.type == EventType.Repaint)
				{
					float value2;
					float peak;
					EditorGUI.VUMeter.SmoothVUMeterData(ref value, ref data, out value2, out peak);
					EditorGUI.VUMeter.HorizontalMeter(position, value2, peak, foregroundTexture, peakColor);
				}
			}

			public static void VerticalMeter(Rect position, float value, ref EditorGUI.VUMeter.SmoothingData data, Texture2D foregroundTexture, Color peakColor)
			{
				if (Event.current.type == EventType.Repaint)
				{
					float value2;
					float peak;
					EditorGUI.VUMeter.SmoothVUMeterData(ref value, ref data, out value2, out peak);
					EditorGUI.VUMeter.VerticalMeter(position, value2, peak, foregroundTexture, peakColor);
				}
			}

			private static void SmoothVUMeterData(ref float value, ref EditorGUI.VUMeter.SmoothingData data, out float renderValue, out float renderPeak)
			{
				if (value <= data.lastValue)
				{
					value = Mathf.Lerp(data.lastValue, value, Time.smoothDeltaTime * 7f);
				}
				else
				{
					value = Mathf.Lerp(value, data.lastValue, Time.smoothDeltaTime * 2f);
					data.peakValue = value;
					data.peakValueTime = Time.realtimeSinceStartup;
				}
				if (value > 1.11111116f)
				{
					value = 1.11111116f;
				}
				if (data.peakValue > 1.11111116f)
				{
					data.peakValue = 1.11111116f;
				}
				renderValue = value * 0.9f;
				renderPeak = data.peakValue * 0.9f;
				data.lastValue = value;
			}
		}

		private static EditorGUI.RecycledTextEditor activeEditor;

		internal static EditorGUI.DelayedTextEditor s_DelayedTextEditor = new EditorGUI.DelayedTextEditor();

		internal static EditorGUI.RecycledTextEditor s_RecycledEditor = new EditorGUI.RecycledTextEditor();

		internal static string s_OriginalText = "";

		internal static string s_RecycledCurrentEditingString;

		internal static double s_RecycledCurrentEditingFloat;

		internal static long s_RecycledCurrentEditingInt;

		private static bool bKeyEventActive = false;

		internal static bool s_DragToPosition = true;

		internal static bool s_Dragged = false;

		internal static bool s_PostPoneMove = false;

		internal static bool s_SelectAllOnMouseUp = true;

		private const double kFoldoutExpandTimeout = 0.7;

		private static double s_FoldoutDestTime;

		private static int s_DragUpdatedOverID = 0;

		private static int s_FoldoutHash = "Foldout".GetHashCode();

		private static int s_TagFieldHash = "s_TagFieldHash".GetHashCode();

		private static int s_PPtrHash = "s_PPtrHash".GetHashCode();

		private static int s_ObjectFieldHash = "s_ObjectFieldHash".GetHashCode();

		private static int s_ToggleHash = "s_ToggleHash".GetHashCode();

		private static int s_ColorHash = "s_ColorHash".GetHashCode();

		private static int s_CurveHash = "s_CurveHash".GetHashCode();

		private static int s_LayerMaskField = "s_LayerMaskField".GetHashCode();

		private static int s_MaskField = "s_MaskField".GetHashCode();

		private static int s_EnumFlagsField = "s_EnumFlagsField".GetHashCode();

		private static int s_GenericField = "s_GenericField".GetHashCode();

		private static int s_PopupHash = "EditorPopup".GetHashCode();

		private static int s_KeyEventFieldHash = "KeyEventField".GetHashCode();

		private static int s_TextFieldHash = "EditorTextField".GetHashCode();

		private static int s_SearchFieldHash = "EditorSearchField".GetHashCode();

		private static int s_TextAreaHash = "EditorTextField".GetHashCode();

		private static int s_PasswordFieldHash = "PasswordField".GetHashCode();

		private static int s_FloatFieldHash = "EditorTextField".GetHashCode();

		private static int s_DelayedTextFieldHash = "DelayedEditorTextField".GetHashCode();

		private static int s_ArraySizeFieldHash = "ArraySizeField".GetHashCode();

		private static int s_SliderHash = "EditorSlider".GetHashCode();

		private static int s_SliderKnobHash = "EditorSliderKnob".GetHashCode();

		private static int s_MinMaxSliderHash = "EditorMinMaxSlider".GetHashCode();

		private static int s_TitlebarHash = "GenericTitlebar".GetHashCode();

		private static int s_ProgressBarHash = "s_ProgressBarHash".GetHashCode();

		private static int s_SelectableLabelHash = "s_SelectableLabel".GetHashCode();

		private static int s_SortingLayerFieldHash = "s_SortingLayerFieldHash".GetHashCode();

		private static int s_TextFieldDropDownHash = "s_TextFieldDropDown".GetHashCode();

		private static int s_DragCandidateState = 0;

		private static float kDragDeadzone = 16f;

		private static Vector2 s_DragStartPos;

		private static double s_DragStartValue = 0.0;

		private static long s_DragStartIntValue = 0L;

		private static double s_DragSensitivity = 0.0;

		internal const float kMiniLabelW = 13f;

		internal const float kLabelW = 80f;

		internal const float kSpacing = 5f;

		internal const float kSpacingSubLabel = 2f;

		internal const float kSliderMinW = 60f;

		internal const float kSliderMaxW = 100f;

		internal const float kSingleLineHeight = 16f;

		internal const float kStructHeaderLineHeight = 16f;

		internal const float kObjectFieldThumbnailHeight = 64f;

		internal const float kObjectFieldMiniThumbnailHeight = 18f;

		internal const float kObjectFieldMiniThumbnailWidth = 32f;

		internal static string kFloatFieldFormatString = "g7";

		internal static string kDoubleFieldFormatString = "g15";

		internal static string kIntFieldFormatString = "#######0";

		internal static int ms_IndentLevel = 0;

		private const float kIndentPerLevel = 15f;

		internal const int kControlVerticalSpacing = 2;

		internal const int kVerticalSpacingMultiField = 0;

		internal static string s_UnitString = "";

		internal const int kInspTitlebarIconWidth = 16;

		internal const int kWindowToolbarHeight = 17;

		private static string kEnabledPropertyName = "m_Enabled";

		private static float[] s_Vector2Floats = new float[2];

		private static int[] s_Vector2Ints = new int[2];

		private static GUIContent[] s_XYLabels = new GUIContent[]
		{
			EditorGUIUtility.TextContent("X"),
			EditorGUIUtility.TextContent("Y")
		};

		private static float[] s_Vector3Floats = new float[3];

		private static int[] s_Vector3Ints = new int[3];

		private static GUIContent[] s_XYZLabels = new GUIContent[]
		{
			EditorGUIUtility.TextContent("X"),
			EditorGUIUtility.TextContent("Y"),
			EditorGUIUtility.TextContent("Z")
		};

		private static float[] s_Vector4Floats = new float[4];

		private static GUIContent[] s_XYZWLabels = new GUIContent[]
		{
			EditorGUIUtility.TextContent("X"),
			EditorGUIUtility.TextContent("Y"),
			EditorGUIUtility.TextContent("Z"),
			EditorGUIUtility.TextContent("W")
		};

		private static GUIContent[] s_WHLabels = new GUIContent[]
		{
			EditorGUIUtility.TextContent("W"),
			EditorGUIUtility.TextContent("H")
		};

		private static GUIContent s_CenterLabel = EditorGUIUtility.TrTextContent("Center", null, null);

		private static GUIContent s_ExtentLabel = EditorGUIUtility.TrTextContent("Extent", null, null);

		private static GUIContent s_PositionLabel = EditorGUIUtility.TrTextContent("Position", null, null);

		private static GUIContent s_SizeLabel = EditorGUIUtility.TrTextContent("Size", null, null);

		internal static readonly GUIContent s_ClipingPlanesLabel = EditorGUIUtility.TrTextContent("Clipping Planes", "Distances from the camera to start and stop rendering.", null);

		internal static readonly GUIContent[] s_NearAndFarLabels = new GUIContent[]
		{
			EditorGUIUtility.TrTextContent("Near", "The closest point relative to the camera that drawing will occur.", null),
			EditorGUIUtility.TrTextContent("Far", "The furthest point relative to the camera that drawing will occur.\n", null)
		};

		internal const float kNearFarLabelsWidth = 35f;

		private static int s_ColorPickID;

		private static int s_CurveID;

		internal static Color kCurveColor = Color.green;

		internal static Color kCurveBGColor = new Color(0.337f, 0.337f, 0.337f, 1f);

		internal static EditorGUIUtility.SkinnedColor kSplitLineSkinnedColor = new EditorGUIUtility.SkinnedColor(new Color(0.6f, 0.6f, 0.6f, 1.333f), new Color(0.12f, 0.12f, 0.12f, 1.333f));

		private const int kInspTitlebarToggleWidth = 16;

		private const int kInspTitlebarSpacing = 2;

		private static GUIContent s_PropertyFieldTempContent = new GUIContent();

		private static GUIContent s_IconDropDown;

		private static Material s_IconTextureInactive;

		private static bool s_HasPrefixLabel;

		private static GUIContent s_PrefixLabel = new GUIContent(null);

		private static Rect s_PrefixTotalRect;

		private static Rect s_PrefixRect;

		private static GUIStyle s_PrefixStyle;

		private static Color s_PrefixGUIColor;

		private static bool s_ShowMixedValue;

		private static GUIContent s_MixedValueContent = EditorGUIUtility.TrTextContent("—", "Mixed Values", null);

		private static Color s_MixedValueContentColor = new Color(1f, 1f, 1f, 0.5f);

		private static Color s_MixedValueContentColorTemp = Color.white;

		private static Stack<PropertyGUIData> s_PropertyStack = new Stack<PropertyGUIData>();

		private static Stack<bool> s_EnabledStack = new Stack<bool>();

		private static Stack<bool> s_ChangedStack = new Stack<bool>();

		internal static readonly string s_AllowedCharactersForFloat = "inftynaeINFTYNAE0123456789.,-*/+%^()";

		internal static readonly string s_AllowedCharactersForInt = "0123456789-*/+%^()";

		private static readonly Dictionary<Type, EditorGUI.EnumData> s_NonObsoleteEnumData = new Dictionary<Type, EditorGUI.EnumData>();

		private static SerializedProperty s_PendingPropertyKeyboardHandling = null;

		private static SerializedProperty s_PendingPropertyDelete = null;

		private static Material s_ColorMaterial;

		private static Material s_AlphaMaterial;

		private static Material s_TransparentMaterial;

		private static Material s_NormalmapMaterial;

		private static Material s_LightmapRGBMMaterial;

		private static Material s_LightmapDoubleLDRMaterial;

		private static Material s_LightmapFullHDRMaterial;

		private static string s_ArrayMultiInfoFormatString = EditorGUIUtility.TrTextContent("This field cannot display arrays with more than {0} elements when multiple objects are selected.", null, null).text;

		private static GUIContent s_ArrayMultiInfoContent = new GUIContent();

		internal static bool s_CollectingToolTips;

		private static readonly int s_GradientHash = "s_GradientHash".GetHashCode();

		private static int s_GradientID;

		private static int s_DropdownButtonHash = "DropdownButton".GetHashCode();

		private static int s_MouseDeltaReaderHash = "MouseDeltaReader".GetHashCode();

		private static Vector2 s_MouseDeltaReaderLastPos;

		private const string kEmptyDropDownElement = "--empty--";

		[CompilerGenerated]
		private static TargetChoiceHandler.TargetChoiceMenuFunction <>f__mg$cache0;

		[CompilerGenerated]
		private static GenericMenu.MenuFunction2 <>f__mg$cache1;

		[CompilerGenerated]
		private static EditorUtility.SelectMenuItemFunction <>f__mg$cache2;

		[CompilerGenerated]
		private static Func<string, string> <>f__mg$cache3;

		[CompilerGenerated]
		private static Func<string, string> <>f__mg$cache4;

		[CompilerGenerated]
		private static Func<string, string> <>f__mg$cache5;

		[CompilerGenerated]
		private static EditorGUI.ObjectFieldValidator <>f__mg$cache6;

		[CompilerGenerated]
		private static EditorGUI.ObjectFieldValidator <>f__mg$cache7;

		[CompilerGenerated]
		private static TargetChoiceHandler.TargetChoiceMenuFunction <>f__mg$cache8;

		public static bool showMixedValue
		{
			get
			{
				return EditorGUI.s_ShowMixedValue;
			}
			set
			{
				EditorGUI.s_ShowMixedValue = value;
			}
		}

		internal static GUIContent mixedValueContent
		{
			get
			{
				return EditorGUI.s_MixedValueContent;
			}
		}

		public static bool actionKey
		{
			get
			{
				bool result;
				if (Event.current == null)
				{
					result = false;
				}
				else if (Application.platform == RuntimePlatform.OSXEditor)
				{
					result = Event.current.command;
				}
				else
				{
					result = Event.current.control;
				}
				return result;
			}
		}

		public static int indentLevel
		{
			get
			{
				return EditorGUI.ms_IndentLevel;
			}
			set
			{
				EditorGUI.ms_IndentLevel = value;
			}
		}

		internal static float indent
		{
			get
			{
				return (float)EditorGUI.indentLevel * 15f;
			}
		}

		internal static Texture2D transparentCheckerTexture
		{
			get
			{
				Texture2D result;
				if (EditorGUIUtility.isProSkin)
				{
					result = (EditorGUIUtility.LoadRequired("Previews/Textures/textureCheckerDark.png") as Texture2D);
				}
				else
				{
					result = (EditorGUIUtility.LoadRequired("Previews/Textures/textureChecker.png") as Texture2D);
				}
				return result;
			}
		}

		internal static Material colorMaterial
		{
			get
			{
				return EditorGUI.GetPreviewMaterial(ref EditorGUI.s_ColorMaterial, "Previews/PreviewColor2D.shader");
			}
		}

		internal static Material alphaMaterial
		{
			get
			{
				return EditorGUI.GetPreviewMaterial(ref EditorGUI.s_AlphaMaterial, "Previews/PreviewAlpha.shader");
			}
		}

		internal static Material transparentMaterial
		{
			get
			{
				return EditorGUI.GetPreviewMaterial(ref EditorGUI.s_TransparentMaterial, "Previews/PreviewTransparent.shader");
			}
		}

		internal static Material normalmapMaterial
		{
			get
			{
				return EditorGUI.GetPreviewMaterial(ref EditorGUI.s_NormalmapMaterial, "Previews/PreviewEncodedNormals.shader");
			}
		}

		internal static Material lightmapRGBMMaterial
		{
			get
			{
				return EditorGUI.GetPreviewMaterial(ref EditorGUI.s_LightmapRGBMMaterial, "Previews/PreviewEncodedLightmapRGBM.shader");
			}
		}

		internal static Material lightmapDoubleLDRMaterial
		{
			get
			{
				return EditorGUI.GetPreviewMaterial(ref EditorGUI.s_LightmapDoubleLDRMaterial, "Previews/PreviewEncodedLightmapDoubleLDR.shader");
			}
		}

		internal static Material lightmapFullHDRMaterial
		{
			get
			{
				return EditorGUI.GetPreviewMaterial(ref EditorGUI.s_LightmapFullHDRMaterial, "Previews/PreviewEncodedLightmapFullHDR.shader");
			}
		}

		internal static bool isCollectingTooltips
		{
			get
			{
				return EditorGUI.s_CollectingToolTips;
			}
			set
			{
				EditorGUI.s_CollectingToolTips = value;
			}
		}

		[ExcludeFromDocs]
		public static void LabelField(Rect position, string label)
		{
			GUIStyle label2 = EditorStyles.label;
			EditorGUI.LabelField(position, label, label2);
		}

		public static void LabelField(Rect position, string label, [DefaultValue("EditorStyles.label")] GUIStyle style)
		{
			EditorGUI.LabelField(position, GUIContent.none, EditorGUIUtility.TempContent(label), style);
		}

		[ExcludeFromDocs]
		public static void LabelField(Rect position, GUIContent label)
		{
			GUIStyle label2 = EditorStyles.label;
			EditorGUI.LabelField(position, label, label2);
		}

		public static void LabelField(Rect position, GUIContent label, [DefaultValue("EditorStyles.label")] GUIStyle style)
		{
			EditorGUI.LabelField(position, GUIContent.none, label, style);
		}

		[ExcludeFromDocs]
		public static void LabelField(Rect position, string label, string label2)
		{
			GUIStyle label3 = EditorStyles.label;
			EditorGUI.LabelField(position, label, label2, label3);
		}

		public static void LabelField(Rect position, string label, string label2, [DefaultValue("EditorStyles.label")] GUIStyle style)
		{
			EditorGUI.LabelField(position, new GUIContent(label), EditorGUIUtility.TempContent(label2), style);
		}

		[ExcludeFromDocs]
		public static void LabelField(Rect position, GUIContent label, GUIContent label2)
		{
			GUIStyle label3 = EditorStyles.label;
			EditorGUI.LabelField(position, label, label2, label3);
		}

		public static void LabelField(Rect position, GUIContent label, GUIContent label2, [DefaultValue("EditorStyles.label")] GUIStyle style)
		{
			EditorGUI.LabelFieldInternal(position, label, label2, style);
		}

		[ExcludeFromDocs]
		public static bool ToggleLeft(Rect position, string label, bool value)
		{
			GUIStyle label2 = EditorStyles.label;
			return EditorGUI.ToggleLeft(position, label, value, label2);
		}

		public static bool ToggleLeft(Rect position, string label, bool value, [DefaultValue("EditorStyles.label")] GUIStyle labelStyle)
		{
			return EditorGUI.ToggleLeft(position, EditorGUIUtility.TempContent(label), value, labelStyle);
		}

		[ExcludeFromDocs]
		public static bool ToggleLeft(Rect position, GUIContent label, bool value)
		{
			GUIStyle label2 = EditorStyles.label;
			return EditorGUI.ToggleLeft(position, label, value, label2);
		}

		public static bool ToggleLeft(Rect position, GUIContent label, bool value, [DefaultValue("EditorStyles.label")] GUIStyle labelStyle)
		{
			return EditorGUI.ToggleLeftInternal(position, label, value, labelStyle);
		}

		[ExcludeFromDocs]
		public static string TextField(Rect position, string text)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.TextField(position, text, textField);
		}

		public static string TextField(Rect position, string text, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			return EditorGUI.TextFieldInternal(position, text, style);
		}

		[ExcludeFromDocs]
		public static string TextField(Rect position, string label, string text)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.TextField(position, label, text, textField);
		}

		public static string TextField(Rect position, string label, string text, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			return EditorGUI.TextField(position, EditorGUIUtility.TempContent(label), text, style);
		}

		[ExcludeFromDocs]
		public static string TextField(Rect position, GUIContent label, string text)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.TextField(position, label, text, textField);
		}

		public static string TextField(Rect position, GUIContent label, string text, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			return EditorGUI.TextFieldInternal(position, label, text, style);
		}

		[ExcludeFromDocs]
		public static string DelayedTextField(Rect position, string text)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.DelayedTextField(position, text, textField);
		}

		public static string DelayedTextField(Rect position, string text, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			return EditorGUI.DelayedTextField(position, GUIContent.none, text, style);
		}

		[ExcludeFromDocs]
		public static string DelayedTextField(Rect position, string label, string text)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.DelayedTextField(position, label, text, textField);
		}

		public static string DelayedTextField(Rect position, string label, string text, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			return EditorGUI.DelayedTextField(position, EditorGUIUtility.TempContent(label), text, style);
		}

		[ExcludeFromDocs]
		public static string DelayedTextField(Rect position, GUIContent label, string text)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.DelayedTextField(position, label, text, textField);
		}

		public static string DelayedTextField(Rect position, GUIContent label, string text, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TextFieldHash, FocusType.Keyboard, position);
			return EditorGUI.DelayedTextFieldInternal(position, controlID, label, text, null, style);
		}

		[ExcludeFromDocs]
		public static void DelayedTextField(Rect position, SerializedProperty property)
		{
			GUIContent label = null;
			EditorGUI.DelayedTextField(position, property, label);
		}

		public static void DelayedTextField(Rect position, SerializedProperty property, [DefaultValue("null")] GUIContent label)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TextFieldHash, FocusType.Keyboard, position);
			EditorGUI.DelayedTextFieldInternal(position, controlID, property, null, label);
		}

		[ExcludeFromDocs]
		public static string DelayedTextField(Rect position, GUIContent label, int controlId, string text)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.DelayedTextField(position, label, controlId, text, textField);
		}

		public static string DelayedTextField(Rect position, GUIContent label, int controlId, string text, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			return EditorGUI.DelayedTextFieldInternal(position, controlId, label, text, null, style);
		}

		[ExcludeFromDocs]
		public static string TextArea(Rect position, string text)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.TextArea(position, text, textField);
		}

		public static string TextArea(Rect position, string text, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			return EditorGUI.TextAreaInternal(position, text, style);
		}

		[ExcludeFromDocs]
		public static void SelectableLabel(Rect position, string text)
		{
			GUIStyle label = EditorStyles.label;
			EditorGUI.SelectableLabel(position, text, label);
		}

		public static void SelectableLabel(Rect position, string text, [DefaultValue("EditorStyles.label")] GUIStyle style)
		{
			EditorGUI.SelectableLabelInternal(position, text, style);
		}

		[ExcludeFromDocs]
		public static string PasswordField(Rect position, string password)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.PasswordField(position, password, textField);
		}

		public static string PasswordField(Rect position, string password, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			return EditorGUI.PasswordFieldInternal(position, password, style);
		}

		[ExcludeFromDocs]
		public static string PasswordField(Rect position, string label, string password)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.PasswordField(position, label, password, textField);
		}

		public static string PasswordField(Rect position, string label, string password, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			return EditorGUI.PasswordField(position, EditorGUIUtility.TempContent(label), password, style);
		}

		[ExcludeFromDocs]
		public static string PasswordField(Rect position, GUIContent label, string password)
		{
			GUIStyle textField = EditorStyles.textField;
			return EditorGUI.PasswordField(position, label, password, textField);
		}

		public static string PasswordField(Rect position, GUIContent label, string password, [DefaultValue("EditorStyles.textField")] GUIStyle style)
		{
			return EditorGUI.PasswordFieldInternal(position, label, password, style);
		}

		[ExcludeFromDocs]
		public static float FloatField(Rect position, float value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.FloatField(position, value, numberField);
		}

		public static float FloatField(Rect position, float value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.FloatFieldInternal(position, value, style);
		}

		[ExcludeFromDocs]
		public static float FloatField(Rect position, string label, float value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.FloatField(position, label, value, numberField);
		}

		public static float FloatField(Rect position, string label, float value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.FloatField(position, EditorGUIUtility.TempContent(label), value, style);
		}

		[ExcludeFromDocs]
		public static float FloatField(Rect position, GUIContent label, float value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.FloatField(position, label, value, numberField);
		}

		public static float FloatField(Rect position, GUIContent label, float value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.FloatFieldInternal(position, label, value, style);
		}

		[ExcludeFromDocs]
		public static float DelayedFloatField(Rect position, float value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DelayedFloatField(position, value, numberField);
		}

		public static float DelayedFloatField(Rect position, float value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DelayedFloatField(position, GUIContent.none, value, style);
		}

		[ExcludeFromDocs]
		public static float DelayedFloatField(Rect position, string label, float value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DelayedFloatField(position, label, value, numberField);
		}

		public static float DelayedFloatField(Rect position, string label, float value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DelayedFloatField(position, EditorGUIUtility.TempContent(label), value, style);
		}

		[ExcludeFromDocs]
		public static float DelayedFloatField(Rect position, GUIContent label, float value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DelayedFloatField(position, label, value, numberField);
		}

		public static float DelayedFloatField(Rect position, GUIContent label, float value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DelayedFloatFieldInternal(position, label, value, style);
		}

		[ExcludeFromDocs]
		public static void DelayedFloatField(Rect position, SerializedProperty property)
		{
			GUIContent label = null;
			EditorGUI.DelayedFloatField(position, property, label);
		}

		public static void DelayedFloatField(Rect position, SerializedProperty property, [DefaultValue("null")] GUIContent label)
		{
			EditorGUI.DelayedFloatFieldInternal(position, property, label);
		}

		[ExcludeFromDocs]
		public static double DoubleField(Rect position, double value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DoubleField(position, value, numberField);
		}

		public static double DoubleField(Rect position, double value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DoubleFieldInternal(position, value, style);
		}

		[ExcludeFromDocs]
		public static double DoubleField(Rect position, string label, double value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DoubleField(position, label, value, numberField);
		}

		public static double DoubleField(Rect position, string label, double value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DoubleField(position, EditorGUIUtility.TempContent(label), value, style);
		}

		[ExcludeFromDocs]
		public static double DoubleField(Rect position, GUIContent label, double value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DoubleField(position, label, value, numberField);
		}

		public static double DoubleField(Rect position, GUIContent label, double value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DoubleFieldInternal(position, label, value, style);
		}

		[ExcludeFromDocs]
		public static double DelayedDoubleField(Rect position, double value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DelayedDoubleField(position, value, numberField);
		}

		public static double DelayedDoubleField(Rect position, double value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DelayedDoubleFieldInternal(position, null, value, style);
		}

		[ExcludeFromDocs]
		public static double DelayedDoubleField(Rect position, string label, double value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DelayedDoubleField(position, label, value, numberField);
		}

		public static double DelayedDoubleField(Rect position, string label, double value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DelayedDoubleField(position, EditorGUIUtility.TempContent(label), value, style);
		}

		[ExcludeFromDocs]
		public static double DelayedDoubleField(Rect position, GUIContent label, double value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DelayedDoubleField(position, label, value, numberField);
		}

		public static double DelayedDoubleField(Rect position, GUIContent label, double value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DelayedDoubleFieldInternal(position, label, value, style);
		}

		[ExcludeFromDocs]
		public static int IntField(Rect position, int value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.IntField(position, value, numberField);
		}

		public static int IntField(Rect position, int value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.IntFieldInternal(position, value, style);
		}

		[ExcludeFromDocs]
		public static int IntField(Rect position, string label, int value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.IntField(position, label, value, numberField);
		}

		public static int IntField(Rect position, string label, int value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.IntField(position, EditorGUIUtility.TempContent(label), value, style);
		}

		[ExcludeFromDocs]
		public static int IntField(Rect position, GUIContent label, int value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.IntField(position, label, value, numberField);
		}

		public static int IntField(Rect position, GUIContent label, int value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.IntFieldInternal(position, label, value, style);
		}

		[ExcludeFromDocs]
		public static int DelayedIntField(Rect position, int value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DelayedIntField(position, value, numberField);
		}

		public static int DelayedIntField(Rect position, int value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DelayedIntField(position, GUIContent.none, value, style);
		}

		[ExcludeFromDocs]
		public static int DelayedIntField(Rect position, string label, int value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DelayedIntField(position, label, value, numberField);
		}

		public static int DelayedIntField(Rect position, string label, int value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DelayedIntField(position, EditorGUIUtility.TempContent(label), value, style);
		}

		[ExcludeFromDocs]
		public static int DelayedIntField(Rect position, GUIContent label, int value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.DelayedIntField(position, label, value, numberField);
		}

		public static int DelayedIntField(Rect position, GUIContent label, int value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.DelayedIntFieldInternal(position, label, value, style);
		}

		[ExcludeFromDocs]
		public static void DelayedIntField(Rect position, SerializedProperty property)
		{
			GUIContent label = null;
			EditorGUI.DelayedIntField(position, property, label);
		}

		public static void DelayedIntField(Rect position, SerializedProperty property, [DefaultValue("null")] GUIContent label)
		{
			EditorGUI.DelayedIntFieldInternal(position, property, label);
		}

		[ExcludeFromDocs]
		public static long LongField(Rect position, long value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.LongField(position, value, numberField);
		}

		public static long LongField(Rect position, long value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.LongFieldInternal(position, value, style);
		}

		[ExcludeFromDocs]
		public static long LongField(Rect position, string label, long value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.LongField(position, label, value, numberField);
		}

		public static long LongField(Rect position, string label, long value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.LongField(position, EditorGUIUtility.TempContent(label), value, style);
		}

		[ExcludeFromDocs]
		public static long LongField(Rect position, GUIContent label, long value)
		{
			GUIStyle numberField = EditorStyles.numberField;
			return EditorGUI.LongField(position, label, value, numberField);
		}

		public static long LongField(Rect position, GUIContent label, long value, [DefaultValue("EditorStyles.numberField")] GUIStyle style)
		{
			return EditorGUI.LongFieldInternal(position, label, value, style);
		}

		[ExcludeFromDocs]
		public static int Popup(Rect position, int selectedIndex, string[] displayedOptions)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.Popup(position, selectedIndex, displayedOptions, popup);
		}

		public static int Popup(Rect position, int selectedIndex, string[] displayedOptions, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.DoPopup(EditorGUI.IndentedRect(position), GUIUtility.GetControlID(EditorGUI.s_PopupHash, FocusType.Keyboard, position), selectedIndex, EditorGUIUtility.TempContent(displayedOptions), style);
		}

		[ExcludeFromDocs]
		public static int Popup(Rect position, int selectedIndex, GUIContent[] displayedOptions)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.Popup(position, selectedIndex, displayedOptions, popup);
		}

		public static int Popup(Rect position, int selectedIndex, GUIContent[] displayedOptions, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.DoPopup(EditorGUI.IndentedRect(position), GUIUtility.GetControlID(EditorGUI.s_PopupHash, FocusType.Keyboard, position), selectedIndex, displayedOptions, style);
		}

		[ExcludeFromDocs]
		public static int Popup(Rect position, string label, int selectedIndex, string[] displayedOptions)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.Popup(position, label, selectedIndex, displayedOptions, popup);
		}

		public static int Popup(Rect position, string label, int selectedIndex, string[] displayedOptions, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.PopupInternal(position, EditorGUIUtility.TempContent(label), selectedIndex, EditorGUIUtility.TempContent(displayedOptions), style);
		}

		[ExcludeFromDocs]
		public static int Popup(Rect position, GUIContent label, int selectedIndex, GUIContent[] displayedOptions)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.Popup(position, label, selectedIndex, displayedOptions, popup);
		}

		public static int Popup(Rect position, GUIContent label, int selectedIndex, GUIContent[] displayedOptions, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.PopupInternal(position, label, selectedIndex, displayedOptions, style);
		}

		[ExcludeFromDocs]
		public static Enum EnumPopup(Rect position, Enum selected)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.EnumPopup(position, selected, popup);
		}

		public static Enum EnumPopup(Rect position, Enum selected, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.EnumPopup(position, GUIContent.none, selected, style);
		}

		[ExcludeFromDocs]
		public static Enum EnumPopup(Rect position, string label, Enum selected)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.EnumPopup(position, label, selected, popup);
		}

		public static Enum EnumPopup(Rect position, string label, Enum selected, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.EnumPopup(position, EditorGUIUtility.TempContent(label), selected, style);
		}

		[ExcludeFromDocs]
		public static Enum EnumPopup(Rect position, GUIContent label, Enum selected)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.EnumPopup(position, label, selected, popup);
		}

		public static Enum EnumPopup(Rect position, GUIContent label, Enum selected, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.EnumPopupInternal(position, label, selected, style);
		}

		[ExcludeFromDocs]
		public static int IntPopup(Rect position, int selectedValue, string[] displayedOptions, int[] optionValues)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.IntPopup(position, selectedValue, displayedOptions, optionValues, popup);
		}

		public static int IntPopup(Rect position, int selectedValue, string[] displayedOptions, int[] optionValues, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.IntPopup(position, GUIContent.none, selectedValue, EditorGUIUtility.TempContent(displayedOptions), optionValues, style);
		}

		[ExcludeFromDocs]
		public static int IntPopup(Rect position, int selectedValue, GUIContent[] displayedOptions, int[] optionValues)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.IntPopup(position, selectedValue, displayedOptions, optionValues, popup);
		}

		public static int IntPopup(Rect position, int selectedValue, GUIContent[] displayedOptions, int[] optionValues, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.IntPopup(position, GUIContent.none, selectedValue, displayedOptions, optionValues, style);
		}

		[ExcludeFromDocs]
		public static int IntPopup(Rect position, GUIContent label, int selectedValue, GUIContent[] displayedOptions, int[] optionValues)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.IntPopup(position, label, selectedValue, displayedOptions, optionValues, popup);
		}

		public static int IntPopup(Rect position, GUIContent label, int selectedValue, GUIContent[] displayedOptions, int[] optionValues, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.IntPopupInternal(position, label, selectedValue, displayedOptions, optionValues, style);
		}

		[ExcludeFromDocs]
		public static void IntPopup(Rect position, SerializedProperty property, GUIContent[] displayedOptions, int[] optionValues)
		{
			GUIContent label = null;
			EditorGUI.IntPopup(position, property, displayedOptions, optionValues, label);
		}

		public static void IntPopup(Rect position, SerializedProperty property, GUIContent[] displayedOptions, int[] optionValues, [DefaultValue("null")] GUIContent label)
		{
			EditorGUI.IntPopupInternal(position, property, displayedOptions, optionValues, label);
		}

		[ExcludeFromDocs]
		public static int IntPopup(Rect position, string label, int selectedValue, string[] displayedOptions, int[] optionValues)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.IntPopup(position, label, selectedValue, displayedOptions, optionValues, popup);
		}

		public static int IntPopup(Rect position, string label, int selectedValue, string[] displayedOptions, int[] optionValues, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.IntPopupInternal(position, EditorGUIUtility.TempContent(label), selectedValue, EditorGUIUtility.TempContent(displayedOptions), optionValues, style);
		}

		[ExcludeFromDocs]
		public static string TagField(Rect position, string tag)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.TagField(position, tag, popup);
		}

		public static string TagField(Rect position, string tag, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.TagFieldInternal(position, EditorGUIUtility.TempContent(string.Empty), tag, style);
		}

		[ExcludeFromDocs]
		public static string TagField(Rect position, string label, string tag)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.TagField(position, label, tag, popup);
		}

		public static string TagField(Rect position, string label, string tag, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.TagFieldInternal(position, EditorGUIUtility.TempContent(label), tag, style);
		}

		[ExcludeFromDocs]
		public static string TagField(Rect position, GUIContent label, string tag)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.TagField(position, label, tag, popup);
		}

		public static string TagField(Rect position, GUIContent label, string tag, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.TagFieldInternal(position, label, tag, style);
		}

		[ExcludeFromDocs]
		public static int LayerField(Rect position, int layer)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.LayerField(position, layer, popup);
		}

		public static int LayerField(Rect position, int layer, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.LayerFieldInternal(position, GUIContent.none, layer, style);
		}

		[ExcludeFromDocs]
		public static int LayerField(Rect position, string label, int layer)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.LayerField(position, label, layer, popup);
		}

		public static int LayerField(Rect position, string label, int layer, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.LayerFieldInternal(position, EditorGUIUtility.TempContent(label), layer, style);
		}

		[ExcludeFromDocs]
		public static int LayerField(Rect position, GUIContent label, int layer)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.LayerField(position, label, layer, popup);
		}

		public static int LayerField(Rect position, GUIContent label, int layer, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.LayerFieldInternal(position, label, layer, style);
		}

		[ExcludeFromDocs]
		public static int MaskField(Rect position, GUIContent label, int mask, string[] displayedOptions)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.MaskField(position, label, mask, displayedOptions, popup);
		}

		public static int MaskField(Rect position, GUIContent label, int mask, string[] displayedOptions, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.MaskFieldInternal(position, label, mask, displayedOptions, style);
		}

		[ExcludeFromDocs]
		public static int MaskField(Rect position, string label, int mask, string[] displayedOptions)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.MaskField(position, label, mask, displayedOptions, popup);
		}

		public static int MaskField(Rect position, string label, int mask, string[] displayedOptions, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.MaskFieldInternal(position, GUIContent.Temp(label), mask, displayedOptions, style);
		}

		[ExcludeFromDocs]
		public static int MaskField(Rect position, int mask, string[] displayedOptions)
		{
			GUIStyle popup = EditorStyles.popup;
			return EditorGUI.MaskField(position, mask, displayedOptions, popup);
		}

		public static int MaskField(Rect position, int mask, string[] displayedOptions, [DefaultValue("EditorStyles.popup")] GUIStyle style)
		{
			return EditorGUI.MaskFieldInternal(position, mask, displayedOptions, style);
		}

		[ExcludeFromDocs]
		public static bool Foldout(Rect position, bool foldout, string content)
		{
			GUIStyle foldout2 = EditorStyles.foldout;
			return EditorGUI.Foldout(position, foldout, content, foldout2);
		}

		public static bool Foldout(Rect position, bool foldout, string content, [DefaultValue("EditorStyles.foldout")] GUIStyle style)
		{
			return EditorGUI.FoldoutInternal(position, foldout, EditorGUIUtility.TempContent(content), false, style);
		}

		[ExcludeFromDocs]
		public static bool Foldout(Rect position, bool foldout, string content, bool toggleOnLabelClick)
		{
			GUIStyle foldout2 = EditorStyles.foldout;
			return EditorGUI.Foldout(position, foldout, content, toggleOnLabelClick, foldout2);
		}

		public static bool Foldout(Rect position, bool foldout, string content, bool toggleOnLabelClick, [DefaultValue("EditorStyles.foldout")] GUIStyle style)
		{
			return EditorGUI.FoldoutInternal(position, foldout, EditorGUIUtility.TempContent(content), toggleOnLabelClick, style);
		}

		[ExcludeFromDocs]
		public static bool Foldout(Rect position, bool foldout, GUIContent content)
		{
			GUIStyle foldout2 = EditorStyles.foldout;
			return EditorGUI.Foldout(position, foldout, content, foldout2);
		}

		public static bool Foldout(Rect position, bool foldout, GUIContent content, [DefaultValue("EditorStyles.foldout")] GUIStyle style)
		{
			return EditorGUI.FoldoutInternal(position, foldout, content, false, style);
		}

		[ExcludeFromDocs]
		public static bool Foldout(Rect position, bool foldout, GUIContent content, bool toggleOnLabelClick)
		{
			GUIStyle foldout2 = EditorStyles.foldout;
			return EditorGUI.Foldout(position, foldout, content, toggleOnLabelClick, foldout2);
		}

		public static bool Foldout(Rect position, bool foldout, GUIContent content, bool toggleOnLabelClick, [DefaultValue("EditorStyles.foldout")] GUIStyle style)
		{
			return EditorGUI.FoldoutInternal(position, foldout, content, toggleOnLabelClick, style);
		}

		[ExcludeFromDocs]
		public static void HandlePrefixLabel(Rect totalPosition, Rect labelPosition, GUIContent label, int id)
		{
			GUIStyle label2 = EditorStyles.label;
			EditorGUI.HandlePrefixLabel(totalPosition, labelPosition, label, id, label2);
		}

		[ExcludeFromDocs]
		public static void HandlePrefixLabel(Rect totalPosition, Rect labelPosition, GUIContent label)
		{
			GUIStyle label2 = EditorStyles.label;
			int id = 0;
			EditorGUI.HandlePrefixLabel(totalPosition, labelPosition, label, id, label2);
		}

		public static void HandlePrefixLabel(Rect totalPosition, Rect labelPosition, GUIContent label, [DefaultValue("0")] int id, [DefaultValue("EditorStyles.label")] GUIStyle style)
		{
			EditorGUI.HandlePrefixLabelInternal(totalPosition, labelPosition, label, id, style);
		}

		[ExcludeFromDocs]
		public static void DrawTextureAlpha(Rect position, Texture image, ScaleMode scaleMode, float imageAspect)
		{
			float mipLevel = -1f;
			EditorGUI.DrawTextureAlpha(position, image, scaleMode, imageAspect, mipLevel);
		}

		[ExcludeFromDocs]
		public static void DrawTextureAlpha(Rect position, Texture image, ScaleMode scaleMode)
		{
			float mipLevel = -1f;
			float imageAspect = 0f;
			EditorGUI.DrawTextureAlpha(position, image, scaleMode, imageAspect, mipLevel);
		}

		[ExcludeFromDocs]
		public static void DrawTextureAlpha(Rect position, Texture image)
		{
			float mipLevel = -1f;
			float imageAspect = 0f;
			ScaleMode scaleMode = ScaleMode.StretchToFill;
			EditorGUI.DrawTextureAlpha(position, image, scaleMode, imageAspect, mipLevel);
		}

		public static void DrawTextureAlpha(Rect position, Texture image, [DefaultValue("ScaleMode.StretchToFill")] ScaleMode scaleMode, [DefaultValue("0")] float imageAspect, [DefaultValue("-1")] float mipLevel)
		{
			EditorGUI.DrawTextureAlphaInternal(position, image, scaleMode, imageAspect, mipLevel);
		}

		[ExcludeFromDocs]
		public static void DrawTextureTransparent(Rect position, Texture image, ScaleMode scaleMode, float imageAspect)
		{
			float mipLevel = -1f;
			EditorGUI.DrawTextureTransparent(position, image, scaleMode, imageAspect, mipLevel);
		}

		[ExcludeFromDocs]
		public static void DrawTextureTransparent(Rect position, Texture image, ScaleMode scaleMode)
		{
			float mipLevel = -1f;
			float imageAspect = 0f;
			EditorGUI.DrawTextureTransparent(position, image, scaleMode, imageAspect, mipLevel);
		}

		[ExcludeFromDocs]
		public static void DrawTextureTransparent(Rect position, Texture image)
		{
			float mipLevel = -1f;
			float imageAspect = 0f;
			ScaleMode scaleMode = ScaleMode.StretchToFill;
			EditorGUI.DrawTextureTransparent(position, image, scaleMode, imageAspect, mipLevel);
		}

		public static void DrawTextureTransparent(Rect position, Texture image, [DefaultValue("ScaleMode.StretchToFill")] ScaleMode scaleMode, [DefaultValue("0")] float imageAspect, [DefaultValue("-1")] float mipLevel)
		{
			EditorGUI.DrawTextureTransparentInternal(position, image, scaleMode, imageAspect, mipLevel);
		}

		[ExcludeFromDocs]
		public static void DrawPreviewTexture(Rect position, Texture image, Material mat, ScaleMode scaleMode, float imageAspect)
		{
			float mipLevel = -1f;
			EditorGUI.DrawPreviewTexture(position, image, mat, scaleMode, imageAspect, mipLevel);
		}

		[ExcludeFromDocs]
		public static void DrawPreviewTexture(Rect position, Texture image, Material mat, ScaleMode scaleMode)
		{
			float mipLevel = -1f;
			float imageAspect = 0f;
			EditorGUI.DrawPreviewTexture(position, image, mat, scaleMode, imageAspect, mipLevel);
		}

		[ExcludeFromDocs]
		public static void DrawPreviewTexture(Rect position, Texture image, Material mat)
		{
			float mipLevel = -1f;
			float imageAspect = 0f;
			ScaleMode scaleMode = ScaleMode.StretchToFill;
			EditorGUI.DrawPreviewTexture(position, image, mat, scaleMode, imageAspect, mipLevel);
		}

		[ExcludeFromDocs]
		public static void DrawPreviewTexture(Rect position, Texture image)
		{
			float mipLevel = -1f;
			float imageAspect = 0f;
			ScaleMode scaleMode = ScaleMode.StretchToFill;
			Material mat = null;
			EditorGUI.DrawPreviewTexture(position, image, mat, scaleMode, imageAspect, mipLevel);
		}

		public static void DrawPreviewTexture(Rect position, Texture image, [DefaultValue("null")] Material mat, [DefaultValue("ScaleMode.StretchToFill")] ScaleMode scaleMode, [DefaultValue("0")] float imageAspect, [DefaultValue("-1")] float mipLevel)
		{
			EditorGUI.DrawPreviewTextureInternal(position, image, mat, scaleMode, imageAspect, mipLevel);
		}

		public static float GetPropertyHeight(SerializedProperty property, bool includeChildren)
		{
			return EditorGUI.GetPropertyHeightInternal(property, null, includeChildren);
		}

		[ExcludeFromDocs]
		public static float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			bool includeChildren = true;
			return EditorGUI.GetPropertyHeight(property, label, includeChildren);
		}

		[ExcludeFromDocs]
		public static float GetPropertyHeight(SerializedProperty property)
		{
			bool includeChildren = true;
			GUIContent label = null;
			return EditorGUI.GetPropertyHeight(property, label, includeChildren);
		}

		public static float GetPropertyHeight(SerializedProperty property, [DefaultValue("null")] GUIContent label, [DefaultValue("true")] bool includeChildren)
		{
			return EditorGUI.GetPropertyHeightInternal(property, label, includeChildren);
		}

		[ExcludeFromDocs]
		public static bool PropertyField(Rect position, SerializedProperty property)
		{
			bool includeChildren = false;
			return EditorGUI.PropertyField(position, property, includeChildren);
		}

		public static bool PropertyField(Rect position, SerializedProperty property, [DefaultValue("false")] bool includeChildren)
		{
			return EditorGUI.PropertyFieldInternal(position, property, null, includeChildren);
		}

		[ExcludeFromDocs]
		public static bool PropertyField(Rect position, SerializedProperty property, GUIContent label)
		{
			bool includeChildren = false;
			return EditorGUI.PropertyField(position, property, label, includeChildren);
		}

		public static bool PropertyField(Rect position, SerializedProperty property, GUIContent label, [DefaultValue("false")] bool includeChildren)
		{
			return EditorGUI.PropertyFieldInternal(position, property, label, includeChildren);
		}

		internal static int Popup(Rect position, GUIContent label, int selectedIndex, string[] displayedOptions, GUIStyle style)
		{
			return EditorGUI.PopupInternal(position, label, selectedIndex, EditorGUIUtility.TempContent(displayedOptions), style);
		}

		internal static int Popup(Rect position, GUIContent label, int selectedIndex, string[] displayedOptions)
		{
			return EditorGUI.Popup(position, label, selectedIndex, displayedOptions, EditorStyles.popup);
		}

		internal static void BeginHandleMixedValueContentColor()
		{
			EditorGUI.s_MixedValueContentColorTemp = GUI.contentColor;
			GUI.contentColor = ((!EditorGUI.showMixedValue) ? GUI.contentColor : (GUI.contentColor * EditorGUI.s_MixedValueContentColor));
		}

		internal static void EndHandleMixedValueContentColor()
		{
			GUI.contentColor = EditorGUI.s_MixedValueContentColorTemp;
		}

		[RequiredByNativeCode]
		internal static bool IsEditingTextField()
		{
			return EditorGUI.RecycledTextEditor.s_ActuallyEditing && EditorGUI.activeEditor != null;
		}

		internal static void EndEditingActiveTextField()
		{
			if (EditorGUI.activeEditor != null)
			{
				EditorGUI.activeEditor.EndEditing();
			}
		}

		public static void FocusTextInControl(string name)
		{
			GUI.FocusControl(name);
			EditorGUIUtility.editingTextField = true;
		}

		internal static void ClearStacks()
		{
			EditorGUI.s_EnabledStack.Clear();
			EditorGUI.s_ChangedStack.Clear();
			EditorGUI.s_PropertyStack.Clear();
			ScriptAttributeUtility.s_DrawerStack.Clear();
		}

		public static void BeginDisabledGroup(bool disabled)
		{
			EditorGUI.BeginDisabled(disabled);
		}

		public static void EndDisabledGroup()
		{
			EditorGUI.EndDisabled();
		}

		internal static void BeginDisabled(bool disabled)
		{
			EditorGUI.s_EnabledStack.Push(GUI.enabled);
			GUI.enabled &= !disabled;
		}

		internal static void EndDisabled()
		{
			if (EditorGUI.s_EnabledStack.Count > 0)
			{
				GUI.enabled = EditorGUI.s_EnabledStack.Pop();
			}
		}

		public static void BeginChangeCheck()
		{
			EditorGUI.s_ChangedStack.Push(GUI.changed);
			GUI.changed = false;
		}

		public static bool EndChangeCheck()
		{
			bool changed = GUI.changed;
			GUI.changed |= EditorGUI.s_ChangedStack.Pop();
			return changed;
		}

		private static void ShowTextEditorPopupMenu()
		{
			GenericMenu genericMenu = new GenericMenu();
			if (EditorGUI.s_RecycledEditor.hasSelection && !EditorGUI.s_RecycledEditor.isPasswordField)
			{
				if (EditorGUI.RecycledTextEditor.s_AllowContextCutOrPaste)
				{
					genericMenu.AddItem(EditorGUIUtility.TrTextContent("Cut", null, null), false, new GenericMenu.MenuFunction(new EditorGUI.PopupMenuEvent("Cut", GUIView.current).SendEvent));
				}
				genericMenu.AddItem(EditorGUIUtility.TrTextContent("Copy", null, null), false, new GenericMenu.MenuFunction(new EditorGUI.PopupMenuEvent("Copy", GUIView.current).SendEvent));
			}
			else
			{
				if (EditorGUI.RecycledTextEditor.s_AllowContextCutOrPaste)
				{
					genericMenu.AddDisabledItem(EditorGUIUtility.TrTextContent("Cut", null, null));
				}
				genericMenu.AddDisabledItem(EditorGUIUtility.TrTextContent("Copy", null, null));
			}
			if (EditorGUI.s_RecycledEditor.CanPaste() && EditorGUI.RecycledTextEditor.s_AllowContextCutOrPaste)
			{
				genericMenu.AddItem(EditorGUIUtility.TrTextContent("Paste", null, null), false, new GenericMenu.MenuFunction(new EditorGUI.PopupMenuEvent("Paste", GUIView.current).SendEvent));
			}
			genericMenu.ShowAsContext();
		}

		internal static void BeginCollectTooltips()
		{
			EditorGUI.isCollectingTooltips = true;
		}

		internal static void EndCollectTooltips()
		{
			EditorGUI.isCollectingTooltips = false;
		}

		public static void DropShadowLabel(Rect position, string text)
		{
			EditorGUI.DoDropShadowLabel(position, EditorGUIUtility.TempContent(text), "PreOverlayLabel", 0.6f);
		}

		public static void DropShadowLabel(Rect position, GUIContent content)
		{
			EditorGUI.DoDropShadowLabel(position, content, "PreOverlayLabel", 0.6f);
		}

		public static void DropShadowLabel(Rect position, string text, GUIStyle style)
		{
			EditorGUI.DoDropShadowLabel(position, EditorGUIUtility.TempContent(text), style, 0.6f);
		}

		public static void DropShadowLabel(Rect position, GUIContent content, GUIStyle style)
		{
			EditorGUI.DoDropShadowLabel(position, content, style, 0.6f);
		}

		internal static void DoDropShadowLabel(Rect position, GUIContent content, GUIStyle style, float shadowOpa)
		{
			if (Event.current.type == EventType.Repaint)
			{
				EditorGUI.DrawLabelShadow(position, content, style, shadowOpa);
				style.Draw(position, content, false, false, false, false);
			}
		}

		internal static void DrawLabelShadow(Rect position, GUIContent content, GUIStyle style, float shadowOpa)
		{
			Color color = GUI.color;
			Color contentColor = GUI.contentColor;
			Color backgroundColor = GUI.backgroundColor;
			GUI.contentColor = new Color(0f, 0f, 0f, 0f);
			style.Draw(position, content, false, false, false, false);
			position.y += 1f;
			GUI.backgroundColor = new Color(0f, 0f, 0f, 0f);
			GUI.contentColor = contentColor;
			EditorGUI.Draw4(position, content, 1f, GUI.color.a * shadowOpa, style);
			EditorGUI.Draw4(position, content, 2f, GUI.color.a * shadowOpa * 0.42f, style);
			GUI.color = color;
			GUI.backgroundColor = backgroundColor;
		}

		private static void Draw4(Rect position, GUIContent content, float offset, float alpha, GUIStyle style)
		{
			GUI.color = new Color(0f, 0f, 0f, alpha);
			position.y -= offset;
			style.Draw(position, content, false, false, false, false);
			position.y += offset * 2f;
			style.Draw(position, content, false, false, false, false);
			position.y -= offset;
			position.x -= offset;
			style.Draw(position, content, false, false, false, false);
			position.x += offset * 2f;
			style.Draw(position, content, false, false, false, false);
		}

		internal static string DoTextField(EditorGUI.RecycledTextEditor editor, int id, Rect position, string text, GUIStyle style, string allowedletters, out bool changed, bool reset, bool multiline, bool passwordField)
		{
			Event current = Event.current;
			string text2 = text;
			if (text == null)
			{
				text = string.Empty;
			}
			if (EditorGUI.showMixedValue)
			{
				text = string.Empty;
			}
			if (EditorGUI.HasKeyboardFocus(id) && Event.current.type != EventType.Layout)
			{
				if (editor.IsEditingControl(id))
				{
					editor.position = position;
					editor.style = style;
					editor.controlID = id;
					editor.multiline = multiline;
					editor.isPasswordField = passwordField;
					editor.DetectFocusChange();
				}
				else if (EditorGUIUtility.editingTextField || (current.GetTypeForControl(id) == EventType.ExecuteCommand && current.commandName == "NewKeyboardFocus"))
				{
					editor.BeginEditing(id, text, position, style, multiline, passwordField);
					if (GUI.skin.settings.cursorColor.a > 0f)
					{
						editor.SelectAll();
					}
					if (current.GetTypeForControl(id) == EventType.ExecuteCommand)
					{
						current.Use();
					}
				}
			}
			if (editor.controlID == id && GUIUtility.keyboardControl != id)
			{
				editor.EndEditing();
			}
			bool flag = false;
			string text3 = editor.text;
			EventType typeForControl = current.GetTypeForControl(id);
			switch (typeForControl)
			{
			case EventType.MouseDown:
				if (position.Contains(current.mousePosition) && current.button == 0)
				{
					if (editor.IsEditingControl(id))
					{
						if (Event.current.clickCount == 2 && GUI.skin.settings.doubleClickSelectsWord)
						{
							editor.MoveCursorToPosition(Event.current.mousePosition);
							editor.SelectCurrentWord();
							editor.MouseDragSelectsWholeWords(true);
							editor.DblClickSnap(TextEditor.DblClickSnapping.WORDS);
							EditorGUI.s_DragToPosition = false;
						}
						else if (Event.current.clickCount == 3 && GUI.skin.settings.tripleClickSelectsLine)
						{
							editor.MoveCursorToPosition(Event.current.mousePosition);
							editor.SelectCurrentParagraph();
							editor.MouseDragSelectsWholeWords(true);
							editor.DblClickSnap(TextEditor.DblClickSnapping.PARAGRAPHS);
							EditorGUI.s_DragToPosition = false;
						}
						else
						{
							editor.MoveCursorToPosition(Event.current.mousePosition);
							EditorGUI.s_SelectAllOnMouseUp = false;
						}
					}
					else
					{
						GUIUtility.keyboardControl = id;
						editor.BeginEditing(id, text, position, style, multiline, passwordField);
						editor.MoveCursorToPosition(Event.current.mousePosition);
						if (GUI.skin.settings.cursorColor.a > 0f)
						{
							EditorGUI.s_SelectAllOnMouseUp = true;
						}
					}
					GUIUtility.hotControl = id;
					current.Use();
				}
				goto IL_9EB;
			case EventType.MouseUp:
				if (GUIUtility.hotControl == id)
				{
					if (EditorGUI.s_Dragged && EditorGUI.s_DragToPosition)
					{
						editor.MoveSelectionToAltCursor();
						flag = true;
					}
					else if (EditorGUI.s_PostPoneMove)
					{
						editor.MoveCursorToPosition(Event.current.mousePosition);
					}
					else if (EditorGUI.s_SelectAllOnMouseUp)
					{
						if (GUI.skin.settings.cursorColor.a > 0f)
						{
							editor.SelectAll();
						}
						EditorGUI.s_SelectAllOnMouseUp = false;
					}
					editor.MouseDragSelectsWholeWords(false);
					EditorGUI.s_DragToPosition = true;
					EditorGUI.s_Dragged = false;
					EditorGUI.s_PostPoneMove = false;
					if (current.button == 0)
					{
						GUIUtility.hotControl = 0;
						current.Use();
					}
				}
				goto IL_9EB;
			case EventType.MouseMove:
			case EventType.KeyUp:
			case EventType.ScrollWheel:
				IL_15B:
				switch (typeForControl)
				{
				case EventType.ValidateCommand:
					if (GUIUtility.keyboardControl == id)
					{
						string commandName = current.commandName;
						if (commandName != null)
						{
							if (!(commandName == "Cut") && !(commandName == "Copy"))
							{
								if (!(commandName == "Paste"))
								{
									if (!(commandName == "SelectAll") && !(commandName == "Delete"))
									{
										if (commandName == "UndoRedoPerformed")
										{
											editor.text = text;
											current.Use();
										}
									}
									else
									{
										current.Use();
									}
								}
								else if (editor.CanPaste())
								{
									current.Use();
								}
							}
							else if (editor.hasSelection)
							{
								current.Use();
							}
						}
					}
					goto IL_9EB;
				case EventType.ExecuteCommand:
					if (GUIUtility.keyboardControl == id)
					{
						string commandName2 = current.commandName;
						if (commandName2 != null)
						{
							if (!(commandName2 == "OnLostFocus"))
							{
								if (!(commandName2 == "Cut"))
								{
									if (!(commandName2 == "Copy"))
									{
										if (!(commandName2 == "Paste"))
										{
											if (!(commandName2 == "SelectAll"))
											{
												if (commandName2 == "Delete")
												{
													editor.BeginEditing(id, text, position, style, multiline, passwordField);
													if (SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX)
													{
														editor.Delete();
													}
													else
													{
														editor.Cut();
													}
													flag = true;
													current.Use();
												}
											}
											else
											{
												editor.SelectAll();
												current.Use();
											}
										}
										else
										{
											editor.BeginEditing(id, text, position, style, multiline, passwordField);
											editor.Paste();
											flag = true;
										}
									}
									else
									{
										editor.Copy();
										current.Use();
									}
								}
								else
								{
									editor.BeginEditing(id, text, position, style, multiline, passwordField);
									editor.Cut();
									flag = true;
								}
							}
							else
							{
								if (EditorGUI.activeEditor != null)
								{
									EditorGUI.activeEditor.EndEditing();
								}
								current.Use();
							}
						}
					}
					goto IL_9EB;
				case EventType.DragExited:
					goto IL_9EB;
				case EventType.ContextClick:
					if (position.Contains(current.mousePosition))
					{
						if (!editor.IsEditingControl(id))
						{
							GUIUtility.keyboardControl = id;
							editor.BeginEditing(id, text, position, style, multiline, passwordField);
							editor.MoveCursorToPosition(Event.current.mousePosition);
						}
						EditorGUI.ShowTextEditorPopupMenu();
						Event.current.Use();
					}
					goto IL_9EB;
				default:
					goto IL_9EB;
				}
				break;
			case EventType.MouseDrag:
				if (GUIUtility.hotControl == id)
				{
					if (!current.shift && editor.hasSelection && EditorGUI.s_DragToPosition)
					{
						editor.MoveAltCursorToPosition(Event.current.mousePosition);
					}
					else
					{
						if (current.shift)
						{
							editor.MoveCursorToPosition(Event.current.mousePosition);
						}
						else
						{
							editor.SelectToPosition(Event.current.mousePosition);
						}
						EditorGUI.s_DragToPosition = false;
						EditorGUI.s_SelectAllOnMouseUp = !editor.hasSelection;
					}
					EditorGUI.s_Dragged = true;
					current.Use();
				}
				goto IL_9EB;
			case EventType.KeyDown:
				if (GUIUtility.keyboardControl == id)
				{
					char character = current.character;
					if (editor.IsEditingControl(id) && editor.HandleKeyEvent(current))
					{
						current.Use();
						flag = true;
					}
					else if (current.keyCode == KeyCode.Escape)
					{
						if (editor.IsEditingControl(id))
						{
							if (style == EditorStyles.toolbarSearchField || style == EditorStyles.searchField)
							{
								EditorGUI.s_OriginalText = "";
							}
							editor.text = EditorGUI.s_OriginalText;
							editor.EndEditing();
							flag = true;
						}
					}
					else if (character == '\n' || character == '\u0003')
					{
						if (!editor.IsEditingControl(id))
						{
							editor.BeginEditing(id, text, position, style, multiline, passwordField);
							editor.SelectAll();
						}
						else
						{
							if (multiline && !current.alt && !current.shift && !current.control)
							{
								editor.Insert(character);
								flag = true;
								goto IL_9EB;
							}
							editor.EndEditing();
						}
						current.Use();
					}
					else if (character == '\t' || current.keyCode == KeyCode.Tab)
					{
						if (multiline && editor.IsEditingControl(id))
						{
							bool flag2 = allowedletters == null || allowedletters.IndexOf(character) != -1;
							bool flag3 = !current.alt && !current.shift && !current.control && character == '\t';
							if (flag3 && flag2)
							{
								editor.Insert(character);
								flag = true;
							}
						}
					}
					else if (character != '\u0019' && character != '\u001b')
					{
						if (editor.IsEditingControl(id))
						{
							bool flag4 = (allowedletters == null || allowedletters.IndexOf(character) != -1) && character != '\0';
							if (flag4)
							{
								editor.Insert(character);
								flag = true;
							}
							else
							{
								if (Input.compositionString != "")
								{
									editor.ReplaceSelection("");
									flag = true;
								}
								current.Use();
							}
						}
					}
				}
				goto IL_9EB;
			case EventType.Repaint:
			{
				string text4;
				if (editor.IsEditingControl(id))
				{
					text4 = ((!passwordField) ? editor.text : "".PadRight(editor.text.Length, '*'));
				}
				else if (EditorGUI.showMixedValue)
				{
					text4 = EditorGUI.s_MixedValueContent.text;
				}
				else
				{
					text4 = ((!passwordField) ? text : "".PadRight(text.Length, '*'));
				}
				if (!string.IsNullOrEmpty(EditorGUI.s_UnitString) && !passwordField)
				{
					text4 = text4 + " " + EditorGUI.s_UnitString;
				}
				if (GUIUtility.hotControl == 0)
				{
					EditorGUIUtility.AddCursorRect(position, MouseCursor.Text);
				}
				if (!editor.IsEditingControl(id))
				{
					EditorGUI.BeginHandleMixedValueContentColor();
					style.Draw(position, EditorGUIUtility.TempContent(text4), id, false);
					EditorGUI.EndHandleMixedValueContentColor();
				}
				else
				{
					editor.DrawCursor(text4);
				}
				goto IL_9EB;
			}
			}
			goto IL_15B;
			IL_9EB:
			if (GUIUtility.keyboardControl == id)
			{
				GUIUtility.textFieldInput = EditorGUIUtility.editingTextField;
			}
			editor.UpdateScrollOffsetIfNeeded(current);
			changed = false;
			if (flag)
			{
				changed = (text3 != editor.text);
				current.Use();
			}
			string result;
			if (changed)
			{
				GUI.changed = true;
				result = editor.text;
			}
			else
			{
				EditorGUI.RecycledTextEditor.s_AllowContextCutOrPaste = true;
				result = text2;
			}
			return result;
		}

		internal static Event KeyEventField(Rect position, Event evt)
		{
			return EditorGUI.DoKeyEventField(position, evt, GUI.skin.textField);
		}

		internal static Event DoKeyEventField(Rect position, Event _event, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_KeyEventFieldHash, FocusType.Passive, position);
			Event current = Event.current;
			Event result;
			switch (current.GetTypeForControl(controlID))
			{
			case EventType.MouseDown:
				if (position.Contains(current.mousePosition))
				{
					GUIUtility.hotControl = controlID;
					current.Use();
					if (EditorGUI.bKeyEventActive)
					{
						EditorGUI.bKeyEventActive = false;
					}
					else
					{
						EditorGUI.bKeyEventActive = true;
					}
				}
				result = _event;
				return result;
			case EventType.MouseUp:
				if (GUIUtility.hotControl == controlID)
				{
					GUIUtility.hotControl = controlID;
					current.Use();
				}
				result = _event;
				return result;
			case EventType.MouseDrag:
				if (GUIUtility.hotControl == controlID)
				{
					current.Use();
				}
				break;
			case EventType.KeyDown:
				if (GUIUtility.hotControl == controlID && EditorGUI.bKeyEventActive)
				{
					if (current.character == '\0')
					{
						if ((current.alt && (current.keyCode == KeyCode.AltGr || current.keyCode == KeyCode.LeftAlt || current.keyCode == KeyCode.RightAlt)) || (current.control && (current.keyCode == KeyCode.LeftControl || current.keyCode == KeyCode.RightControl)) || (current.command && (current.keyCode == KeyCode.LeftCommand || current.keyCode == KeyCode.RightCommand || current.keyCode == KeyCode.LeftWindows || current.keyCode == KeyCode.RightWindows)) || (current.shift && (current.keyCode == KeyCode.LeftShift || current.keyCode == KeyCode.RightShift || current.keyCode == KeyCode.None)))
						{
							result = _event;
							return result;
						}
					}
					EditorGUI.bKeyEventActive = false;
					GUI.changed = true;
					GUIUtility.hotControl = 0;
					Event @event = new Event(current);
					current.Use();
					result = @event;
					return result;
				}
				break;
			case EventType.Repaint:
				if (EditorGUI.bKeyEventActive)
				{
					GUIContent content = EditorGUIUtility.TempContent("[Please press a key]");
					style.Draw(position, content, controlID);
				}
				else
				{
					string t = InternalEditorUtility.TextifyEvent(_event);
					style.Draw(position, EditorGUIUtility.TempContent(t), controlID);
				}
				break;
			}
			result = _event;
			return result;
		}

		internal static Rect GetInspectorTitleBarObjectFoldoutRenderRect(Rect rect)
		{
			return new Rect(rect.x + 3f, rect.y + 3f, 16f, 16f);
		}

		private static bool IsValidForContextMenu(UnityEngine.Object target)
		{
			bool result;
			if (target == null)
			{
				result = false;
			}
			else
			{
				bool flag = target == null;
				result = ((flag && (target is MonoBehaviour || target is ScriptableObject)) || !flag);
			}
			return result;
		}

		internal static bool DoObjectMouseInteraction(bool foldout, Rect interactionRect, UnityEngine.Object[] targetObjs, int id)
		{
			bool enabled = GUI.enabled;
			GUI.enabled = true;
			Event current = Event.current;
			EventType typeForControl = current.GetTypeForControl(id);
			switch (typeForControl)
			{
			case EventType.MouseDown:
				if (interactionRect.Contains(current.mousePosition))
				{
					if (current.button == 1 && EditorGUI.IsValidForContextMenu(targetObjs[0]))
					{
						EditorUtility.DisplayObjectContextMenu(new Rect(current.mousePosition.x, current.mousePosition.y, 0f, 0f), targetObjs, 0);
						current.Use();
					}
					else if (current.button == 0 && (Application.platform != RuntimePlatform.OSXEditor || !current.control))
					{
						GUIUtility.hotControl = id;
						GUIUtility.keyboardControl = id;
						DragAndDropDelay dragAndDropDelay = (DragAndDropDelay)GUIUtility.GetStateObject(typeof(DragAndDropDelay), id);
						dragAndDropDelay.mouseDownPosition = current.mousePosition;
						current.Use();
					}
				}
				goto IL_362;
			case EventType.MouseUp:
				if (GUIUtility.hotControl == id)
				{
					GUIUtility.hotControl = 0;
					current.Use();
					if (interactionRect.Contains(current.mousePosition))
					{
						GUI.changed = true;
						foldout = !foldout;
					}
				}
				goto IL_362;
			case EventType.MouseMove:
			case EventType.KeyUp:
			case EventType.ScrollWheel:
			case EventType.Repaint:
			case EventType.Layout:
				IL_4D:
				if (typeForControl != EventType.ContextClick)
				{
					goto IL_362;
				}
				if (interactionRect.Contains(current.mousePosition) && EditorGUI.IsValidForContextMenu(targetObjs[0]))
				{
					EditorUtility.DisplayObjectContextMenu(new Rect(current.mousePosition.x, current.mousePosition.y, 0f, 0f), targetObjs, 0);
					current.Use();
				}
				goto IL_362;
			case EventType.MouseDrag:
				if (GUIUtility.hotControl == id)
				{
					DragAndDropDelay dragAndDropDelay2 = (DragAndDropDelay)GUIUtility.GetStateObject(typeof(DragAndDropDelay), id);
					if (dragAndDropDelay2.CanStartDrag())
					{
						GUIUtility.hotControl = 0;
						DragAndDrop.PrepareStartDrag();
						DragAndDrop.objectReferences = targetObjs;
						if (targetObjs.Length > 1)
						{
							DragAndDrop.StartDrag("<Multiple>");
						}
						else
						{
							DragAndDrop.StartDrag(ObjectNames.GetDragAndDropTitle(targetObjs[0]));
						}
					}
					current.Use();
				}
				goto IL_362;
			case EventType.KeyDown:
				if (GUIUtility.keyboardControl == id)
				{
					if (current.keyCode == KeyCode.LeftArrow)
					{
						foldout = false;
						current.Use();
					}
					if (current.keyCode == KeyCode.RightArrow)
					{
						foldout = true;
						current.Use();
					}
				}
				goto IL_362;
			case EventType.DragUpdated:
				if (EditorGUI.s_DragUpdatedOverID == id)
				{
					if (interactionRect.Contains(current.mousePosition))
					{
						if ((double)Time.realtimeSinceStartup > EditorGUI.s_FoldoutDestTime)
						{
							foldout = true;
							HandleUtility.Repaint();
						}
					}
					else
					{
						EditorGUI.s_DragUpdatedOverID = 0;
					}
				}
				else if (interactionRect.Contains(current.mousePosition))
				{
					EditorGUI.s_DragUpdatedOverID = id;
					EditorGUI.s_FoldoutDestTime = (double)Time.realtimeSinceStartup + 0.7;
				}
				if (interactionRect.Contains(current.mousePosition))
				{
					DragAndDrop.visualMode = InternalEditorUtility.InspectorWindowDrag(targetObjs, false);
					Event.current.Use();
				}
				goto IL_362;
			case EventType.DragPerform:
				if (interactionRect.Contains(current.mousePosition))
				{
					DragAndDrop.visualMode = InternalEditorUtility.InspectorWindowDrag(targetObjs, true);
					DragAndDrop.AcceptDrag();
					Event.current.Use();
				}
				goto IL_362;
			}
			goto IL_4D;
			IL_362:
			GUI.enabled = enabled;
			return foldout;
		}

		private static void DoObjectFoldoutInternal(bool foldout, Rect interactionRect, Rect renderRect, UnityEngine.Object[] targetObjs, int id)
		{
			bool enabled = GUI.enabled;
			GUI.enabled = true;
			Event current = Event.current;
			EventType typeForControl = current.GetTypeForControl(id);
			if (typeForControl == EventType.Repaint)
			{
				bool flag = GUIUtility.hotControl == id;
				EditorStyles.foldout.Draw(renderRect, flag, flag, foldout, false);
			}
			GUI.enabled = enabled;
		}

		internal static bool DoObjectFoldout(bool foldout, Rect interactionRect, Rect renderRect, UnityEngine.Object[] targetObjs, int id)
		{
			foldout = EditorGUI.DoObjectMouseInteraction(foldout, interactionRect, targetObjs, id);
			EditorGUI.DoObjectFoldoutInternal(foldout, interactionRect, renderRect, targetObjs, id);
			return foldout;
		}

		internal static void LabelFieldInternal(Rect position, GUIContent label, GUIContent label2, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FloatFieldHash, FocusType.Passive, position);
			position = EditorGUI.PrefixLabel(position, controlID, label);
			if (Event.current.type == EventType.Repaint)
			{
				style.Draw(position, label2, controlID);
			}
		}

		public static bool Toggle(Rect position, bool value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ToggleHash, FocusType.Keyboard, position);
			return EditorGUIInternal.DoToggleForward(EditorGUI.IndentedRect(position), controlID, value, GUIContent.none, EditorStyles.toggle);
		}

		public static bool Toggle(Rect position, string label, bool value)
		{
			return EditorGUI.Toggle(position, EditorGUIUtility.TempContent(label), value);
		}

		public static bool Toggle(Rect position, bool value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ToggleHash, FocusType.Keyboard, position);
			return EditorGUIInternal.DoToggleForward(position, controlID, value, GUIContent.none, style);
		}

		public static bool Toggle(Rect position, string label, bool value, GUIStyle style)
		{
			return EditorGUI.Toggle(position, EditorGUIUtility.TempContent(label), value, style);
		}

		public static bool Toggle(Rect position, GUIContent label, bool value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ToggleHash, FocusType.Keyboard, position);
			return EditorGUIInternal.DoToggleForward(EditorGUI.PrefixLabel(position, controlID, label), controlID, value, GUIContent.none, EditorStyles.toggle);
		}

		public static bool Toggle(Rect position, GUIContent label, bool value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ToggleHash, FocusType.Keyboard, position);
			return EditorGUIInternal.DoToggleForward(EditorGUI.PrefixLabel(position, controlID, label), controlID, value, GUIContent.none, style);
		}

		internal static bool ToggleLeftInternal(Rect position, GUIContent label, bool value, GUIStyle labelStyle)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ToggleHash, FocusType.Keyboard, position);
			Rect position2 = EditorGUI.IndentedRect(position);
			Rect labelPosition = EditorGUI.IndentedRect(position);
			labelPosition.xMin += (float)EditorStyles.toggle.padding.left;
			EditorGUI.HandlePrefixLabel(position, labelPosition, label, controlID, labelStyle);
			return EditorGUIInternal.DoToggleForward(position2, controlID, value, GUIContent.none, EditorStyles.toggle);
		}

		internal static bool DoToggle(Rect position, int id, bool value, GUIContent content, GUIStyle style)
		{
			return EditorGUIInternal.DoToggleForward(position, id, value, content, style);
		}

		internal static string TextFieldInternal(int id, Rect position, string text, GUIStyle style)
		{
			bool flag;
			text = EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, id, EditorGUI.IndentedRect(position), text, style, null, out flag, false, false, false);
			return text;
		}

		internal static string TextFieldInternal(Rect position, string text, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TextFieldHash, FocusType.Keyboard, position);
			bool flag;
			text = EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, controlID, EditorGUI.IndentedRect(position), text, style, null, out flag, false, false, false);
			return text;
		}

		internal static string TextFieldInternal(Rect position, GUIContent label, string text, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TextFieldHash, FocusType.Keyboard, position);
			bool flag;
			text = EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, controlID, EditorGUI.PrefixLabel(position, controlID, label), text, style, null, out flag, false, false, false);
			return text;
		}

		internal static string ToolbarSearchField(int id, Rect position, string text, bool showWithPopupArrow)
		{
			Rect position2 = position;
			position2.width -= 14f;
			bool flag;
			text = EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, id, position2, text, (!showWithPopupArrow) ? EditorStyles.toolbarSearchField : EditorStyles.toolbarSearchFieldPopup, null, out flag, false, false, false);
			Rect position3 = position;
			position3.x += position.width - 14f;
			position3.width = 14f;
			if (GUI.Button(position3, GUIContent.none, (!(text != "")) ? EditorStyles.toolbarSearchFieldCancelButtonEmpty : EditorStyles.toolbarSearchFieldCancelButton) && text != "")
			{
				text = (EditorGUI.s_RecycledEditor.text = "");
				GUIUtility.keyboardControl = 0;
			}
			return text;
		}

		internal static string ToolbarSearchField(Rect position, string[] searchModes, ref int searchMode, string text)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SearchFieldHash, FocusType.Keyboard, position);
			return EditorGUI.ToolbarSearchField(controlID, position, searchModes, ref searchMode, text);
		}

		internal static string ToolbarSearchField(int id, Rect position, string[] searchModes, ref int searchMode, string text)
		{
			bool flag = searchModes != null;
			if (flag)
			{
				searchMode = EditorGUI.PopupCallbackInfo.GetSelectedValueForControl(id, searchMode);
				Rect rect = position;
				rect.width = 20f;
				if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
				{
					EditorGUI.PopupCallbackInfo.instance = new EditorGUI.PopupCallbackInfo(id);
					EditorUtility.DisplayCustomMenu(position, EditorGUIUtility.TempContent(searchModes), searchMode, new EditorUtility.SelectMenuItemFunction(EditorGUI.PopupCallbackInfo.instance.SetEnumValueDelegate), null);
					if (EditorGUI.s_RecycledEditor.IsEditingControl(id))
					{
						Event.current.Use();
					}
				}
			}
			text = EditorGUI.ToolbarSearchField(id, position, text, flag);
			if (flag && text == "" && !EditorGUI.s_RecycledEditor.IsEditingControl(id) && Event.current.type == EventType.Repaint)
			{
				position.width -= 14f;
				using (new EditorGUI.DisabledScope(true))
				{
					EditorStyles.toolbarSearchFieldPopup.Draw(position, EditorGUIUtility.TempContent(searchModes[searchMode]), id, false);
				}
			}
			return text;
		}

		internal static string SearchField(Rect position, string text)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SearchFieldHash, FocusType.Keyboard, position);
			Rect position2 = position;
			position2.width -= 15f;
			bool flag;
			text = EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, controlID, position2, text, EditorStyles.searchField, null, out flag, false, false, false);
			Rect position3 = position;
			position3.x += position.width - 15f;
			position3.width = 15f;
			if (GUI.Button(position3, GUIContent.none, (!(text != "")) ? EditorStyles.searchFieldCancelButtonEmpty : EditorStyles.searchFieldCancelButton) && text != "")
			{
				text = (EditorGUI.s_RecycledEditor.text = "");
				GUIUtility.keyboardControl = 0;
			}
			return text;
		}

		internal static string ScrollableTextAreaInternal(Rect position, string text, ref Vector2 scrollPosition, GUIStyle style)
		{
			string result;
			if (Event.current.type == EventType.Layout)
			{
				result = text;
			}
			else
			{
				int controlID = GUIUtility.GetControlID(EditorGUI.s_TextAreaHash, FocusType.Keyboard, position);
				position = EditorGUI.IndentedRect(position);
				float height = style.CalcHeight(GUIContent.Temp(text), position.width);
				Rect rect = new Rect(0f, 0f, position.width, height);
				Vector2 contentOffset = style.contentOffset;
				if (position.height < rect.height)
				{
					Rect position2 = position;
					position2.width = GUI.skin.verticalScrollbar.fixedWidth;
					position2.height -= 2f;
					position2.y += 1f;
					position2.x = position.x + position.width - position2.width;
					position.width -= position2.width;
					height = style.CalcHeight(GUIContent.Temp(text), position.width);
					rect = new Rect(0f, 0f, position.width, height);
					if (position.Contains(Event.current.mousePosition) && Event.current.type == EventType.ScrollWheel)
					{
						float value = scrollPosition.y + Event.current.delta.y * 10f;
						scrollPosition.y = Mathf.Clamp(value, 0f, rect.height);
						Event.current.Use();
					}
					scrollPosition.y = GUI.VerticalScrollbar(position2, scrollPosition.y, position.height, 0f, rect.height);
					if (!EditorGUI.s_RecycledEditor.IsEditingControl(controlID))
					{
						style.contentOffset -= scrollPosition;
						style.Internal_clipOffset = scrollPosition;
					}
					else
					{
						EditorGUI.s_RecycledEditor.scrollOffset.y = scrollPosition.y;
					}
				}
				EventType type = Event.current.type;
				bool flag;
				string text2 = EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, controlID, position, text, style, null, out flag, false, true, false);
				if (type != Event.current.type)
				{
					scrollPosition = EditorGUI.s_RecycledEditor.scrollOffset;
				}
				style.contentOffset = contentOffset;
				style.Internal_clipOffset = Vector2.zero;
				result = text2;
			}
			return result;
		}

		internal static string TextAreaInternal(Rect position, string text, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TextAreaHash, FocusType.Keyboard, position);
			bool flag;
			text = EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, controlID, EditorGUI.IndentedRect(position), text, style, null, out flag, false, true, false);
			return text;
		}

		internal static void SelectableLabelInternal(Rect position, string text, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SelectableLabelHash, FocusType.Keyboard, position);
			Event current = Event.current;
			if (GUIUtility.keyboardControl == controlID && current.GetTypeForControl(controlID) == EventType.KeyDown)
			{
				KeyCode keyCode = current.keyCode;
				switch (keyCode)
				{
				case KeyCode.UpArrow:
				case KeyCode.DownArrow:
				case KeyCode.RightArrow:
				case KeyCode.LeftArrow:
				case KeyCode.Home:
				case KeyCode.End:
				case KeyCode.PageUp:
				case KeyCode.PageDown:
					goto IL_9F;
				case KeyCode.Insert:
					IL_64:
					if (keyCode != KeyCode.Space)
					{
						if (current.character != '\t')
						{
							current.Use();
						}
						goto IL_9F;
					}
					GUIUtility.hotControl = 0;
					GUIUtility.keyboardControl = 0;
					goto IL_9F;
				}
				goto IL_64;
				IL_9F:;
			}
			if (current.type == EventType.ExecuteCommand && (current.commandName == "Paste" || current.commandName == "Cut") && GUIUtility.keyboardControl == controlID)
			{
				current.Use();
			}
			Color cursorColor = GUI.skin.settings.cursorColor;
			GUI.skin.settings.cursorColor = new Color(0f, 0f, 0f, 0f);
			EditorGUI.RecycledTextEditor.s_AllowContextCutOrPaste = false;
			bool flag;
			text = EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, controlID, EditorGUI.IndentedRect(position), text, style, string.Empty, out flag, false, true, false);
			GUI.skin.settings.cursorColor = cursorColor;
		}

		[Obsolete("Use PasswordField instead.")]
		public static string DoPasswordField(int id, Rect position, string password, GUIStyle style)
		{
			bool flag;
			return EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, id, position, password, style, null, out flag, false, false, true);
		}

		[Obsolete("Use PasswordField instead.")]
		public static string DoPasswordField(int id, Rect position, GUIContent label, string password, GUIStyle style)
		{
			bool flag;
			return EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, id, EditorGUI.PrefixLabel(position, id, label), password, style, null, out flag, false, false, true);
		}

		internal static string PasswordFieldInternal(Rect position, string password, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_PasswordFieldHash, FocusType.Keyboard, position);
			bool flag;
			return EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, controlID, EditorGUI.IndentedRect(position), password, style, null, out flag, false, false, true);
		}

		internal static string PasswordFieldInternal(Rect position, GUIContent label, string password, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_PasswordFieldHash, FocusType.Keyboard, position);
			bool flag;
			return EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, controlID, EditorGUI.PrefixLabel(position, controlID, label), password, style, null, out flag, false, false, true);
		}

		internal static float FloatFieldInternal(Rect position, float value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FloatFieldHash, FocusType.Keyboard, position);
			return EditorGUI.DoFloatField(EditorGUI.s_RecycledEditor, EditorGUI.IndentedRect(position), new Rect(0f, 0f, 0f, 0f), controlID, value, EditorGUI.kFloatFieldFormatString, style, false);
		}

		internal static float FloatFieldInternal(Rect position, GUIContent label, float value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FloatFieldHash, FocusType.Keyboard, position);
			Rect position2 = EditorGUI.PrefixLabel(position, controlID, label);
			position.xMax = position2.x;
			return EditorGUI.DoFloatField(EditorGUI.s_RecycledEditor, position2, position, controlID, value, EditorGUI.kFloatFieldFormatString, style, true);
		}

		internal static double DoubleFieldInternal(Rect position, double value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FloatFieldHash, FocusType.Keyboard, position);
			return EditorGUI.DoDoubleField(EditorGUI.s_RecycledEditor, EditorGUI.IndentedRect(position), new Rect(0f, 0f, 0f, 0f), controlID, value, EditorGUI.kDoubleFieldFormatString, style, false);
		}

		internal static double DoubleFieldInternal(Rect position, GUIContent label, double value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FloatFieldHash, FocusType.Keyboard, position);
			Rect position2 = EditorGUI.PrefixLabel(position, controlID, label);
			position.xMax = position2.x;
			return EditorGUI.DoDoubleField(EditorGUI.s_RecycledEditor, position2, position, controlID, value, EditorGUI.kDoubleFieldFormatString, style, true);
		}

		private static void DragNumberValue(EditorGUI.RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, bool isDouble, ref double doubleVal, ref long longVal, string formatString, GUIStyle style, double dragSensitivity)
		{
			Event current = Event.current;
			switch (current.GetTypeForControl(id))
			{
			case EventType.MouseDown:
				if (dragHotZone.Contains(current.mousePosition) && current.button == 0)
				{
					EditorGUIUtility.editingTextField = false;
					GUIUtility.hotControl = id;
					if (EditorGUI.activeEditor != null)
					{
						EditorGUI.activeEditor.EndEditing();
					}
					current.Use();
					GUIUtility.keyboardControl = id;
					EditorGUI.s_DragCandidateState = 1;
					EditorGUI.s_DragStartValue = doubleVal;
					EditorGUI.s_DragStartIntValue = longVal;
					EditorGUI.s_DragStartPos = current.mousePosition;
					EditorGUI.s_DragSensitivity = dragSensitivity;
					current.Use();
					EditorGUIUtility.SetWantsMouseJumping(1);
				}
				break;
			case EventType.MouseUp:
				if (GUIUtility.hotControl == id && EditorGUI.s_DragCandidateState != 0)
				{
					GUIUtility.hotControl = 0;
					EditorGUI.s_DragCandidateState = 0;
					current.Use();
					EditorGUIUtility.SetWantsMouseJumping(0);
				}
				break;
			case EventType.MouseDrag:
				if (GUIUtility.hotControl == id)
				{
					int num = EditorGUI.s_DragCandidateState;
					if (num != 1)
					{
						if (num == 2)
						{
							if (isDouble)
							{
								doubleVal += (double)HandleUtility.niceMouseDelta * EditorGUI.s_DragSensitivity;
								doubleVal = MathUtils.RoundBasedOnMinimumDifference(doubleVal, EditorGUI.s_DragSensitivity);
							}
							else
							{
								longVal += (long)Math.Round((double)HandleUtility.niceMouseDelta * EditorGUI.s_DragSensitivity);
							}
							GUI.changed = true;
							current.Use();
						}
					}
					else
					{
						if ((Event.current.mousePosition - EditorGUI.s_DragStartPos).sqrMagnitude > EditorGUI.kDragDeadzone)
						{
							EditorGUI.s_DragCandidateState = 2;
							GUIUtility.keyboardControl = id;
						}
						current.Use();
					}
				}
				break;
			case EventType.KeyDown:
				if (GUIUtility.hotControl == id && current.keyCode == KeyCode.Escape && EditorGUI.s_DragCandidateState != 0)
				{
					doubleVal = EditorGUI.s_DragStartValue;
					longVal = EditorGUI.s_DragStartIntValue;
					GUI.changed = true;
					GUIUtility.hotControl = 0;
					current.Use();
				}
				break;
			case EventType.Repaint:
				EditorGUIUtility.AddCursorRect(dragHotZone, MouseCursor.SlideArrow);
				break;
			}
		}

		internal static float DoFloatField(EditorGUI.RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, float value, string formatString, GUIStyle style, bool draggable)
		{
			return EditorGUI.DoFloatField(editor, position, dragHotZone, id, value, formatString, style, draggable, (Event.current.GetTypeForControl(id) != EventType.MouseDown) ? 0f : ((float)NumericFieldDraggerUtility.CalculateFloatDragSensitivity(EditorGUI.s_DragStartValue)));
		}

		internal static float DoFloatField(EditorGUI.RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, float value, string formatString, GUIStyle style, bool draggable, float dragSensitivity)
		{
			long num = 0L;
			double value2 = (double)value;
			EditorGUI.DoNumberField(editor, position, dragHotZone, id, true, ref value2, ref num, formatString, style, draggable, (double)dragSensitivity);
			return MathUtils.ClampToFloat(value2);
		}

		internal static int DoIntField(EditorGUI.RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, int value, string formatString, GUIStyle style, bool draggable, float dragSensitivity)
		{
			double num = 0.0;
			long value2 = (long)value;
			EditorGUI.DoNumberField(editor, position, dragHotZone, id, false, ref num, ref value2, formatString, style, draggable, (double)dragSensitivity);
			return MathUtils.ClampToInt(value2);
		}

		internal static double DoDoubleField(EditorGUI.RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, double value, string formatString, GUIStyle style, bool draggable)
		{
			return EditorGUI.DoDoubleField(editor, position, dragHotZone, id, value, formatString, style, draggable, (Event.current.GetTypeForControl(id) != EventType.MouseDown) ? 0.0 : NumericFieldDraggerUtility.CalculateFloatDragSensitivity(EditorGUI.s_DragStartValue));
		}

		internal static double DoDoubleField(EditorGUI.RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, double value, string formatString, GUIStyle style, bool draggable, double dragSensitivity)
		{
			long num = 0L;
			EditorGUI.DoNumberField(editor, position, dragHotZone, id, true, ref value, ref num, formatString, style, draggable, dragSensitivity);
			return value;
		}

		internal static long DoLongField(EditorGUI.RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, long value, string formatString, GUIStyle style, bool draggable, double dragSensitivity)
		{
			double num = 0.0;
			EditorGUI.DoNumberField(editor, position, dragHotZone, id, false, ref num, ref value, formatString, style, draggable, dragSensitivity);
			return value;
		}

		private static bool HasKeyboardFocus(int controlID)
		{
			return GUIUtility.keyboardControl == controlID && GUIView.current.hasFocus;
		}

		internal static void DoNumberField(EditorGUI.RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, bool isDouble, ref double doubleVal, ref long longVal, string formatString, GUIStyle style, bool draggable, double dragSensitivity)
		{
			string allowedletters = (!isDouble) ? EditorGUI.s_AllowedCharactersForInt : EditorGUI.s_AllowedCharactersForFloat;
			if (draggable)
			{
				EditorGUI.DragNumberValue(editor, position, dragHotZone, id, isDouble, ref doubleVal, ref longVal, formatString, style, dragSensitivity);
			}
			Event current = Event.current;
			string text;
			if (EditorGUI.HasKeyboardFocus(id) || (current.type == EventType.MouseDown && current.button == 0 && position.Contains(current.mousePosition)))
			{
				if (!editor.IsEditingControl(id))
				{
					text = (EditorGUI.s_RecycledCurrentEditingString = ((!isDouble) ? longVal.ToString(formatString) : doubleVal.ToString(formatString)));
				}
				else
				{
					text = EditorGUI.s_RecycledCurrentEditingString;
					if (current.type == EventType.ValidateCommand && current.commandName == "UndoRedoPerformed")
					{
						text = ((!isDouble) ? longVal.ToString(formatString) : doubleVal.ToString(formatString));
					}
				}
			}
			else
			{
				text = ((!isDouble) ? longVal.ToString(formatString) : doubleVal.ToString(formatString));
			}
			if (GUIUtility.keyboardControl == id)
			{
				bool flag;
				text = EditorGUI.DoTextField(editor, id, position, text, style, allowedletters, out flag, false, false, false);
				if (flag)
				{
					GUI.changed = true;
					EditorGUI.s_RecycledCurrentEditingString = text;
					if (isDouble)
					{
						if (EditorGUI.StringToDouble(text, out doubleVal))
						{
							EditorGUI.s_RecycledCurrentEditingFloat = doubleVal;
						}
					}
					else
					{
						EditorGUI.StringToLong(text, out longVal);
						EditorGUI.s_RecycledCurrentEditingInt = longVal;
					}
				}
			}
			else
			{
				bool flag;
				text = EditorGUI.DoTextField(editor, id, position, text, style, allowedletters, out flag, false, false, false);
			}
		}

		internal static bool StringToDouble(string str, out double value)
		{
			string a = str.ToLower();
			bool result;
			if (a == "inf" || a == "infinity")
			{
				value = double.PositiveInfinity;
			}
			else if (a == "-inf" || a == "-infinity")
			{
				value = double.NegativeInfinity;
			}
			else
			{
				str = str.Replace(',', '.');
				if (!double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out value))
				{
					value = ExpressionEvaluator.Evaluate<double>(str);
					result = true;
					return result;
				}
				if (double.IsNaN(value))
				{
					value = 0.0;
				}
				result = true;
				return result;
			}
			result = false;
			return result;
		}

		internal static bool StringToLong(string str, out long value)
		{
			if (!long.TryParse(str, out value))
			{
				value = ExpressionEvaluator.Evaluate<long>(str);
			}
			return true;
		}

		internal static int ArraySizeField(Rect position, GUIContent label, int value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ArraySizeFieldHash, FocusType.Keyboard, position);
			EditorGUI.BeginChangeCheck();
			string s = EditorGUI.DelayedTextFieldInternal(position, controlID, label, value.ToString(EditorGUI.kIntFieldFormatString), "0123456789-", style);
			if (EditorGUI.EndChangeCheck())
			{
				try
				{
					value = int.Parse(s, CultureInfo.InvariantCulture.NumberFormat);
				}
				catch (FormatException)
				{
				}
			}
			return value;
		}

		internal static string DelayedTextFieldInternal(Rect position, string value, string allowedLetters, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_DelayedTextFieldHash, FocusType.Keyboard, position);
			return EditorGUI.DelayedTextFieldInternal(position, controlID, GUIContent.none, value, allowedLetters, style);
		}

		internal static string DelayedTextFieldInternal(Rect position, int id, GUIContent label, string value, string allowedLetters, GUIStyle style)
		{
			string text;
			if (EditorGUI.HasKeyboardFocus(id))
			{
				if (!EditorGUI.s_DelayedTextEditor.IsEditingControl(id))
				{
					text = (EditorGUI.s_RecycledCurrentEditingString = value);
				}
				else
				{
					text = EditorGUI.s_RecycledCurrentEditingString;
				}
				Event current = Event.current;
				if (current.type == EventType.ValidateCommand && current.commandName == "UndoRedoPerformed")
				{
					text = value;
				}
			}
			else
			{
				text = value;
			}
			bool changed = GUI.changed;
			bool flag;
			text = EditorGUI.s_DelayedTextEditor.OnGUI(id, text, out flag);
			GUI.changed = false;
			if (!flag)
			{
				text = EditorGUI.DoTextField(EditorGUI.s_DelayedTextEditor, id, EditorGUI.PrefixLabel(position, id, label), text, style, allowedLetters, out flag, false, false, false);
				GUI.changed = false;
				if (GUIUtility.keyboardControl == id)
				{
					if (!EditorGUI.s_DelayedTextEditor.IsEditingControl(id))
					{
						if (value != text)
						{
							GUI.changed = true;
							value = text;
						}
					}
					else
					{
						EditorGUI.s_RecycledCurrentEditingString = text;
					}
				}
			}
			else
			{
				GUI.changed = true;
				value = text;
			}
			GUI.changed |= changed;
			return value;
		}

		internal static void DelayedTextFieldInternal(Rect position, int id, SerializedProperty property, string allowedLetters, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			EditorGUI.BeginChangeCheck();
			string stringValue = EditorGUI.DelayedTextFieldInternal(position, id, label, property.stringValue, allowedLetters, EditorStyles.textField);
			if (EditorGUI.EndChangeCheck())
			{
				property.stringValue = stringValue;
			}
			EditorGUI.EndProperty();
		}

		internal static float DelayedFloatFieldInternal(Rect position, GUIContent label, float value, GUIStyle style)
		{
			float num = value;
			float num2 = num;
			EditorGUI.BeginChangeCheck();
			int controlID = GUIUtility.GetControlID(EditorGUI.s_DelayedTextFieldHash, FocusType.Keyboard, position);
			string s = EditorGUI.DelayedTextFieldInternal(position, controlID, label, num.ToString(), EditorGUI.s_AllowedCharactersForFloat, style);
			if (EditorGUI.EndChangeCheck())
			{
				if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out num2) && num2 != num)
				{
					value = num2;
					GUI.changed = true;
				}
			}
			return num2;
		}

		internal static void DelayedFloatFieldInternal(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			EditorGUI.BeginChangeCheck();
			float floatValue = EditorGUI.DelayedFloatFieldInternal(position, label, property.floatValue, EditorStyles.numberField);
			if (EditorGUI.EndChangeCheck())
			{
				property.floatValue = floatValue;
			}
			EditorGUI.EndProperty();
		}

		internal static double DelayedDoubleFieldInternal(Rect position, GUIContent label, double value, GUIStyle style)
		{
			double num = value;
			double num2 = num;
			if (label != null)
			{
				position = EditorGUI.PrefixLabel(position, label);
			}
			EditorGUI.BeginChangeCheck();
			string s = EditorGUI.DelayedTextFieldInternal(position, num.ToString(), EditorGUI.s_AllowedCharactersForFloat, style);
			if (EditorGUI.EndChangeCheck())
			{
				if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out num2) && num2 != num)
				{
					value = num2;
					GUI.changed = true;
				}
			}
			return num2;
		}

		internal static int DelayedIntFieldInternal(Rect position, GUIContent label, int value, GUIStyle style)
		{
			int num = value;
			int num2 = num;
			EditorGUI.BeginChangeCheck();
			int controlID = GUIUtility.GetControlID(EditorGUI.s_DelayedTextFieldHash, FocusType.Keyboard, position);
			string text = EditorGUI.DelayedTextFieldInternal(position, controlID, label, num.ToString(), EditorGUI.s_AllowedCharactersForInt, style);
			if (EditorGUI.EndChangeCheck())
			{
				if (int.TryParse(text, out num2))
				{
					if (num2 != num)
					{
						value = num2;
						GUI.changed = true;
					}
				}
				else
				{
					num2 = ExpressionEvaluator.Evaluate<int>(text);
					if (num2 != num)
					{
						value = num2;
						GUI.changed = true;
					}
				}
			}
			return num2;
		}

		internal static void DelayedIntFieldInternal(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			EditorGUI.BeginChangeCheck();
			int intValue = EditorGUI.DelayedIntFieldInternal(position, label, property.intValue, EditorStyles.numberField);
			if (EditorGUI.EndChangeCheck())
			{
				property.intValue = intValue;
			}
			EditorGUI.EndProperty();
		}

		internal static int IntFieldInternal(Rect position, int value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FloatFieldHash, FocusType.Keyboard, position);
			return EditorGUI.DoIntField(EditorGUI.s_RecycledEditor, EditorGUI.IndentedRect(position), new Rect(0f, 0f, 0f, 0f), controlID, value, EditorGUI.kIntFieldFormatString, style, false, (float)NumericFieldDraggerUtility.CalculateIntDragSensitivity((long)value));
		}

		internal static int IntFieldInternal(Rect position, GUIContent label, int value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FloatFieldHash, FocusType.Keyboard, position);
			Rect position2 = EditorGUI.PrefixLabel(position, controlID, label);
			position.xMax = position2.x;
			return EditorGUI.DoIntField(EditorGUI.s_RecycledEditor, position2, position, controlID, value, EditorGUI.kIntFieldFormatString, style, true, (float)NumericFieldDraggerUtility.CalculateIntDragSensitivity((long)value));
		}

		internal static long LongFieldInternal(Rect position, long value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FloatFieldHash, FocusType.Keyboard, position);
			return EditorGUI.DoLongField(EditorGUI.s_RecycledEditor, EditorGUI.IndentedRect(position), new Rect(0f, 0f, 0f, 0f), controlID, value, EditorGUI.kIntFieldFormatString, style, false, (double)NumericFieldDraggerUtility.CalculateIntDragSensitivity(value));
		}

		internal static long LongFieldInternal(Rect position, GUIContent label, long value, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FloatFieldHash, FocusType.Keyboard, position);
			Rect position2 = EditorGUI.PrefixLabel(position, controlID, label);
			position.xMax = position2.x;
			return EditorGUI.DoLongField(EditorGUI.s_RecycledEditor, position2, position, controlID, value, EditorGUI.kIntFieldFormatString, style, true, (double)NumericFieldDraggerUtility.CalculateIntDragSensitivity(value));
		}

		public static float Slider(Rect position, float value, float leftValue, float rightValue)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SliderHash, FocusType.Keyboard, position);
			return EditorGUI.DoSlider(EditorGUI.IndentedRect(position), EditorGUIUtility.DragZoneRect(position), controlID, value, leftValue, rightValue, EditorGUI.kFloatFieldFormatString);
		}

		public static float Slider(Rect position, string label, float value, float leftValue, float rightValue)
		{
			return EditorGUI.Slider(position, EditorGUIUtility.TempContent(label), value, leftValue, rightValue);
		}

		public static float Slider(Rect position, GUIContent label, float value, float leftValue, float rightValue)
		{
			return EditorGUI.PowerSlider(position, label, value, leftValue, rightValue, 1f);
		}

		internal static float Slider(Rect position, GUIContent label, float value, float sliderMin, float sliderMax, float textFieldMin, float textFieldMax)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SliderHash, FocusType.Keyboard, position);
			Rect position2 = EditorGUI.PrefixLabel(position, controlID, label);
			Rect dragZonePosition = (!EditorGUI.LabelHasContent(label)) ? default(Rect) : EditorGUIUtility.DragZoneRect(position);
			return EditorGUI.DoSlider(position2, dragZonePosition, controlID, value, sliderMin, sliderMax, EditorGUI.kFloatFieldFormatString, textFieldMin, textFieldMax, 1f, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb, null);
		}

		internal static float PowerSlider(Rect position, string label, float sliderValue, float leftValue, float rightValue, float power)
		{
			return EditorGUI.PowerSlider(position, EditorGUIUtility.TempContent(label), sliderValue, leftValue, rightValue, power);
		}

		internal static float PowerSlider(Rect position, GUIContent label, float sliderValue, float leftValue, float rightValue, float power)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SliderHash, FocusType.Keyboard, position);
			Rect position2 = EditorGUI.PrefixLabel(position, controlID, label);
			Rect dragZonePosition = (!EditorGUI.LabelHasContent(label)) ? default(Rect) : EditorGUIUtility.DragZoneRect(position);
			return EditorGUI.DoSlider(position2, dragZonePosition, controlID, sliderValue, leftValue, rightValue, EditorGUI.kFloatFieldFormatString, power);
		}

		private static float PowPreserveSign(float f, float p)
		{
			float num = Mathf.Pow(Mathf.Abs(f), p);
			return (f >= 0f) ? num : (-num);
		}

		private static void DoPropertyContextMenu(SerializedProperty property)
		{
			GenericMenu genericMenu = new GenericMenu();
			SerializedProperty serializedProperty = property.serializedObject.FindProperty(property.propertyPath);
			ScriptAttributeUtility.GetHandler(property).AddMenuItems(property, genericMenu);
			if (property.hasMultipleDifferentValues && !property.hasVisibleChildren)
			{
				GenericMenu arg_5C_0 = genericMenu;
				SerializedProperty arg_5C_1 = serializedProperty;
				if (EditorGUI.<>f__mg$cache0 == null)
				{
					EditorGUI.<>f__mg$cache0 = new TargetChoiceHandler.TargetChoiceMenuFunction(TargetChoiceHandler.SetToValueOfTarget);
				}
				TargetChoiceHandler.AddSetToValueOfTargetMenuItems(arg_5C_0, arg_5C_1, EditorGUI.<>f__mg$cache0);
			}
			if (property.serializedObject.targetObjects.Length == 1 && property.isInstantiatedPrefab)
			{
				GenericMenu arg_AD_0 = genericMenu;
				GUIContent arg_AD_1 = EditorGUIUtility.TrTextContent("Revert Value to Prefab", null, null);
				bool arg_AD_2 = false;
				if (EditorGUI.<>f__mg$cache1 == null)
				{
					EditorGUI.<>f__mg$cache1 = new GenericMenu.MenuFunction2(TargetChoiceHandler.SetPrefabOverride);
				}
				arg_AD_0.AddItem(arg_AD_1, arg_AD_2, EditorGUI.<>f__mg$cache1, serializedProperty);
			}
			if (property.propertyPath.LastIndexOf(']') == property.propertyPath.Length - 1)
			{
				string propertyPath = property.propertyPath.Substring(0, property.propertyPath.LastIndexOf(".Array.data["));
				SerializedProperty serializedProperty2 = property.serializedObject.FindProperty(propertyPath);
				if (!serializedProperty2.isFixedBuffer)
				{
					if (genericMenu.GetItemCount() > 0)
					{
						genericMenu.AddSeparator("");
					}
					genericMenu.AddItem(EditorGUIUtility.TrTextContent("Duplicate Array Element", null, null), false, delegate(object a)
					{
						TargetChoiceHandler.DuplicateArrayElement(a);
						EditorGUIUtility.editingTextField = false;
					}, serializedProperty);
					genericMenu.AddItem(EditorGUIUtility.TrTextContent("Delete Array Element", null, null), false, delegate(object a)
					{
						TargetChoiceHandler.DeleteArrayElement(a);
						EditorGUIUtility.editingTextField = false;
					}, serializedProperty);
				}
			}
			if (Event.current.shift)
			{
				if (genericMenu.GetItemCount() > 0)
				{
					genericMenu.AddSeparator("");
				}
				genericMenu.AddItem(EditorGUIUtility.TrTextContent("Print Property Path", null, null), false, delegate(object e)
				{
					Debug.Log(((SerializedProperty)e).propertyPath);
				}, serializedProperty);
			}
			if (EditorApplication.contextualPropertyMenu != null)
			{
				if (genericMenu.GetItemCount() > 0)
				{
					genericMenu.AddSeparator("");
				}
				EditorApplication.contextualPropertyMenu(genericMenu, property);
			}
			Event.current.Use();
			if (genericMenu.GetItemCount() != 0)
			{
				genericMenu.ShowAsContext();
			}
		}

		public static void Slider(Rect position, SerializedProperty property, float leftValue, float rightValue)
		{
			EditorGUI.Slider(position, property, leftValue, rightValue, property.displayName);
		}

		public static void Slider(Rect position, SerializedProperty property, float leftValue, float rightValue, string label)
		{
			EditorGUI.Slider(position, property, leftValue, rightValue, EditorGUIUtility.TempContent(label));
		}

		public static void Slider(Rect position, SerializedProperty property, float leftValue, float rightValue, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			EditorGUI.BeginChangeCheck();
			float floatValue = EditorGUI.Slider(position, label, property.floatValue, leftValue, rightValue);
			if (EditorGUI.EndChangeCheck())
			{
				property.floatValue = floatValue;
			}
			EditorGUI.EndProperty();
		}

		public static int IntSlider(Rect position, int value, int leftValue, int rightValue)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SliderHash, FocusType.Keyboard, position);
			return Mathf.RoundToInt(EditorGUI.DoSlider(EditorGUI.IndentedRect(position), EditorGUIUtility.DragZoneRect(position), controlID, (float)value, (float)leftValue, (float)rightValue, EditorGUI.kIntFieldFormatString));
		}

		public static int IntSlider(Rect position, string label, int value, int leftValue, int rightValue)
		{
			return EditorGUI.IntSlider(position, EditorGUIUtility.TempContent(label), value, leftValue, rightValue);
		}

		public static int IntSlider(Rect position, GUIContent label, int value, int leftValue, int rightValue)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SliderHash, FocusType.Keyboard, position);
			return Mathf.RoundToInt(EditorGUI.DoSlider(EditorGUI.PrefixLabel(position, controlID, label), EditorGUIUtility.DragZoneRect(position), controlID, (float)value, (float)leftValue, (float)rightValue, EditorGUI.kIntFieldFormatString));
		}

		public static void IntSlider(Rect position, SerializedProperty property, int leftValue, int rightValue)
		{
			EditorGUI.IntSlider(position, property, leftValue, rightValue, property.displayName);
		}

		public static void IntSlider(Rect position, SerializedProperty property, int leftValue, int rightValue, string label)
		{
			EditorGUI.IntSlider(position, property, leftValue, rightValue, EditorGUIUtility.TempContent(label));
		}

		public static void IntSlider(Rect position, SerializedProperty property, int leftValue, int rightValue, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			EditorGUI.BeginChangeCheck();
			int intValue = EditorGUI.IntSlider(position, label, property.intValue, leftValue, rightValue);
			if (EditorGUI.EndChangeCheck())
			{
				property.intValue = intValue;
			}
			EditorGUI.EndProperty();
		}

		internal static void DoTwoLabels(Rect rect, GUIContent leftLabel, GUIContent rightLabel, GUIStyle labelStyle)
		{
			if (Event.current.type == EventType.Repaint)
			{
				TextAnchor alignment = labelStyle.alignment;
				labelStyle.alignment = TextAnchor.UpperLeft;
				GUI.Label(rect, leftLabel, labelStyle);
				labelStyle.alignment = TextAnchor.UpperRight;
				GUI.Label(rect, rightLabel, labelStyle);
				labelStyle.alignment = alignment;
			}
		}

		private static float DoSlider(Rect position, Rect dragZonePosition, int id, float value, float left, float right, string formatString)
		{
			return EditorGUI.DoSlider(position, dragZonePosition, id, value, left, right, formatString, 1f);
		}

		private static float DoSlider(Rect position, Rect dragZonePosition, int id, float value, float left, float right, string formatString, float power)
		{
			return EditorGUI.DoSlider(position, dragZonePosition, id, value, left, right, formatString, power, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb, null);
		}

		private static float DoSlider(Rect position, Rect dragZonePosition, int id, float value, float left, float right, string formatString, float power, GUIStyle sliderStyle, GUIStyle thumbStyle, Texture2D sliderBackground)
		{
			return EditorGUI.DoSlider(position, dragZonePosition, id, value, left, right, formatString, left, right, power, sliderStyle, thumbStyle, sliderBackground);
		}

		private static float DoSlider(Rect position, Rect dragZonePosition, int id, float value, float sliderMin, float sliderMax, string formatString, float textFieldMin, float textFieldMax, float power, GUIStyle sliderStyle, GUIStyle thumbStyle, Texture2D sliderBackground)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SliderKnobHash, FocusType.Passive, position);
			sliderMin = Mathf.Clamp(sliderMin, -3.40282347E+38f, 3.40282347E+38f);
			sliderMax = Mathf.Clamp(sliderMax, -3.40282347E+38f, 3.40282347E+38f);
			float num = position.width;
			if (num >= 65f + EditorGUIUtility.fieldWidth)
			{
				float num2 = num - 5f - EditorGUIUtility.fieldWidth;
				EditorGUI.BeginChangeCheck();
				if (GUIUtility.keyboardControl == id && !EditorGUI.s_RecycledEditor.IsEditingControl(id))
				{
					GUIUtility.keyboardControl = controlID;
				}
				float start = sliderMin;
				float end = sliderMax;
				float num3 = value;
				if (power != 1f)
				{
					start = EditorGUI.PowPreserveSign(sliderMin, 1f / power);
					end = EditorGUI.PowPreserveSign(sliderMax, 1f / power);
					num3 = EditorGUI.PowPreserveSign(value, 1f / power);
				}
				Rect rect = new Rect(position.x, position.y, num2, position.height);
				if (sliderBackground != null && Event.current.type == EventType.Repaint)
				{
					Rect screenRect = sliderStyle.overflow.Add(sliderStyle.padding.Remove(rect));
					Graphics.DrawTexture(screenRect, sliderBackground, new Rect(0.5f / (float)sliderBackground.width, 0.5f / (float)sliderBackground.height, 1f - 1f / (float)sliderBackground.width, 1f - 1f / (float)sliderBackground.height), 0, 0, 0, 0, Color.grey);
				}
				num3 = GUI.Slider(rect, num3, 0f, start, end, sliderStyle, (!EditorGUI.showMixedValue) ? thumbStyle : "SliderMixed", true, controlID);
				if (power != 1f)
				{
					num3 = EditorGUI.PowPreserveSign(num3, power);
					num3 = Mathf.Clamp(num3, Mathf.Min(sliderMin, sliderMax), Mathf.Max(sliderMin, sliderMax));
				}
				if (EditorGUIUtility.sliderLabels.HasLabels())
				{
					Color color = GUI.color;
					GUI.color *= new Color(1f, 1f, 1f, 0.5f);
					Rect rect2 = new Rect(rect.x, rect.y + 10f, rect.width, rect.height);
					EditorGUI.DoTwoLabels(rect2, EditorGUIUtility.sliderLabels.leftLabel, EditorGUIUtility.sliderLabels.rightLabel, EditorStyles.miniLabel);
					GUI.color = color;
					EditorGUIUtility.sliderLabels.SetLabels(null, null);
				}
				if (GUIUtility.keyboardControl == controlID || GUIUtility.hotControl == controlID)
				{
					GUIUtility.keyboardControl = id;
				}
				if (GUIUtility.keyboardControl == id && Event.current.type == EventType.KeyDown && !EditorGUI.s_RecycledEditor.IsEditingControl(id) && (Event.current.keyCode == KeyCode.LeftArrow || Event.current.keyCode == KeyCode.RightArrow))
				{
					float num4 = MathUtils.GetClosestPowerOfTen(Mathf.Abs((sliderMax - sliderMin) * 0.01f));
					if (formatString == EditorGUI.kIntFieldFormatString && num4 < 1f)
					{
						num4 = 1f;
					}
					if (Event.current.shift)
					{
						num4 *= 10f;
					}
					if (Event.current.keyCode == KeyCode.LeftArrow)
					{
						num3 -= num4 * 0.5001f;
					}
					else
					{
						num3 += num4 * 0.5001f;
					}
					num3 = MathUtils.RoundToMultipleOf(num3, num4);
					GUI.changed = true;
					Event.current.Use();
				}
				if (EditorGUI.EndChangeCheck())
				{
					float f = (sliderMax - sliderMin) / (num2 - (float)GUI.skin.horizontalSlider.padding.horizontal - GUI.skin.horizontalSliderThumb.fixedWidth);
					num3 = MathUtils.RoundBasedOnMinimumDifference(num3, Mathf.Abs(f));
					value = Mathf.Clamp(num3, Mathf.Min(sliderMin, sliderMax), Mathf.Max(sliderMin, sliderMax));
					if (EditorGUI.s_RecycledEditor.IsEditingControl(id))
					{
						EditorGUI.s_RecycledEditor.EndEditing();
					}
				}
				EditorGUI.BeginChangeCheck();
				float value2 = EditorGUI.DoFloatField(EditorGUI.s_RecycledEditor, new Rect(position.x + num2 + 5f, position.y, EditorGUIUtility.fieldWidth, position.height), dragZonePosition, id, value, formatString, EditorStyles.numberField, true);
				if (EditorGUI.EndChangeCheck())
				{
					value = Mathf.Clamp(value2, Mathf.Min(textFieldMin, textFieldMax), Mathf.Max(textFieldMin, textFieldMax));
				}
			}
			else
			{
				num = Mathf.Min(EditorGUIUtility.fieldWidth, num);
				position.x = position.xMax - num;
				position.width = num;
				value = EditorGUI.DoFloatField(EditorGUI.s_RecycledEditor, position, dragZonePosition, id, value, formatString, EditorStyles.numberField, true);
				value = Mathf.Clamp(value, Mathf.Min(textFieldMin, textFieldMax), Mathf.Max(textFieldMin, textFieldMax));
			}
			return value;
		}

		[Obsolete("Switch the order of the first two parameters.")]
		public static void MinMaxSlider(GUIContent label, Rect position, ref float minValue, ref float maxValue, float minLimit, float maxLimit)
		{
			EditorGUI.MinMaxSlider(position, label, ref minValue, ref maxValue, minLimit, maxLimit);
		}

		public static void MinMaxSlider(Rect position, string label, ref float minValue, ref float maxValue, float minLimit, float maxLimit)
		{
			EditorGUI.MinMaxSlider(position, EditorGUIUtility.TempContent(label), ref minValue, ref maxValue, minLimit, maxLimit);
		}

		public static void MinMaxSlider(Rect position, GUIContent label, ref float minValue, ref float maxValue, float minLimit, float maxLimit)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_MinMaxSliderHash, FocusType.Passive);
			EditorGUI.DoMinMaxSlider(EditorGUI.PrefixLabel(position, controlID, label), controlID, ref minValue, ref maxValue, minLimit, maxLimit);
		}

		public static void MinMaxSlider(Rect position, ref float minValue, ref float maxValue, float minLimit, float maxLimit)
		{
			EditorGUI.DoMinMaxSlider(EditorGUI.IndentedRect(position), GUIUtility.GetControlID(EditorGUI.s_MinMaxSliderHash, FocusType.Passive), ref minValue, ref maxValue, minLimit, maxLimit);
		}

		private static void DoMinMaxSlider(Rect position, int id, ref float minValue, ref float maxValue, float minLimit, float maxLimit)
		{
			float num = maxValue - minValue;
			EditorGUI.BeginChangeCheck();
			EditorGUIExt.DoMinMaxSlider(position, id, ref minValue, ref num, minLimit, maxLimit, minLimit, maxLimit, GUI.skin.horizontalSlider, EditorStyles.minMaxHorizontalSliderThumb, true);
			if (EditorGUI.EndChangeCheck())
			{
				maxValue = minValue + num;
			}
		}

		private static int PopupInternal(Rect position, string label, int selectedIndex, string[] displayedOptions, GUIStyle style)
		{
			return EditorGUI.PopupInternal(position, EditorGUIUtility.TempContent(label), selectedIndex, EditorGUIUtility.TempContent(displayedOptions), style);
		}

		private static int PopupInternal(Rect position, GUIContent label, int selectedIndex, string[] displayedOptions, GUIStyle style)
		{
			return EditorGUI.PopupInternal(position, label, selectedIndex, EditorGUIUtility.TempContent(displayedOptions), style);
		}

		private static int PopupInternal(Rect position, GUIContent label, int selectedIndex, GUIContent[] displayedOptions, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_PopupHash, FocusType.Keyboard, position);
			if (label != null)
			{
				position = EditorGUI.PrefixLabel(position, controlID, label);
			}
			return EditorGUI.DoPopup(position, controlID, selectedIndex, displayedOptions, style);
		}

		private static void Popup(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginChangeCheck();
			int enumValueIndex = EditorGUI.Popup(position, label, (!property.hasMultipleDifferentValues) ? property.enumValueIndex : -1, EditorGUIUtility.TempContent(property.enumLocalizedDisplayNames));
			if (EditorGUI.EndChangeCheck())
			{
				property.enumValueIndex = enumValueIndex;
			}
		}

		internal static void Popup(Rect position, SerializedProperty property, GUIContent[] displayedOptions, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			EditorGUI.BeginChangeCheck();
			int intValue = EditorGUI.Popup(position, label, (!property.hasMultipleDifferentValues) ? property.intValue : -1, displayedOptions);
			if (EditorGUI.EndChangeCheck())
			{
				property.intValue = intValue;
			}
			EditorGUI.EndProperty();
		}

		private static Enum EnumPopupInternal(Rect position, GUIContent label, Enum selected, GUIStyle style)
		{
			Type type = selected.GetType();
			if (!type.IsEnum)
			{
				throw new ArgumentException("Parameter selected must be of type System.Enum", "selected");
			}
			bool flag = EditorUtility.IsUnityAssembly(type);
			EditorGUI.EnumData nonObsoleteEnumData = EditorGUI.GetNonObsoleteEnumData(type);
			int num = Array.IndexOf<Enum>(nonObsoleteEnumData.values, selected);
			num = EditorGUI.Popup(position, label, num, (!flag) ? EditorGUIUtility.TempContent(nonObsoleteEnumData.displayNames) : EditorGUIUtility.TrTempContent(nonObsoleteEnumData.displayNames), style);
			return (num >= 0 && num < nonObsoleteEnumData.flagValues.Length) ? nonObsoleteEnumData.values[num] : selected;
		}

		private static int IntPopupInternal(Rect position, GUIContent label, int selectedValue, GUIContent[] displayedOptions, int[] optionValues, GUIStyle style)
		{
			int num;
			if (optionValues != null)
			{
				num = 0;
				while (num < optionValues.Length && selectedValue != optionValues[num])
				{
					num++;
				}
			}
			else
			{
				num = selectedValue;
			}
			num = EditorGUI.PopupInternal(position, label, num, displayedOptions, style);
			int result;
			if (optionValues == null)
			{
				result = num;
			}
			else if (num < 0 || num >= optionValues.Length)
			{
				result = selectedValue;
			}
			else
			{
				result = optionValues[num];
			}
			return result;
		}

		internal static void IntPopupInternal(Rect position, SerializedProperty property, GUIContent[] displayedOptions, int[] optionValues, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			EditorGUI.BeginChangeCheck();
			int intValue = EditorGUI.IntPopupInternal(position, label, property.intValue, displayedOptions, optionValues, EditorStyles.popup);
			if (EditorGUI.EndChangeCheck())
			{
				property.intValue = intValue;
			}
			EditorGUI.EndProperty();
		}

		internal static void SortingLayerField(Rect position, GUIContent label, SerializedProperty layerID, GUIStyle style, GUIStyle labelStyle)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SortingLayerFieldHash, FocusType.Keyboard, position);
			position = EditorGUI.PrefixLabel(position, controlID, label, labelStyle);
			Event current = Event.current;
			int selectedValueForControl = EditorGUI.PopupCallbackInfo.GetSelectedValueForControl(controlID, -1);
			if (selectedValueForControl != -1)
			{
				int[] sortingLayerUniqueIDs = InternalEditorUtility.sortingLayerUniqueIDs;
				if (selectedValueForControl >= sortingLayerUniqueIDs.Length)
				{
					TagManagerInspector.ShowWithInitialExpansion(TagManagerInspector.InitialExpansionState.SortingLayers);
				}
				else
				{
					layerID.intValue = sortingLayerUniqueIDs[selectedValueForControl];
				}
			}
			if ((current.type == EventType.MouseDown && position.Contains(current.mousePosition)) || current.MainActionKeyForControl(controlID))
			{
				int[] sortingLayerUniqueIDs2 = InternalEditorUtility.sortingLayerUniqueIDs;
				string[] sortingLayerNames = InternalEditorUtility.sortingLayerNames;
				int i;
				for (i = 0; i < sortingLayerUniqueIDs2.Length; i++)
				{
					if (sortingLayerUniqueIDs2[i] == layerID.intValue)
					{
						break;
					}
				}
				ArrayUtility.Add<string>(ref sortingLayerNames, "");
				ArrayUtility.Add<string>(ref sortingLayerNames, "Add Sorting Layer...");
				EditorGUI.DoPopup(position, controlID, i, EditorGUIUtility.TempContent(sortingLayerNames), style);
			}
			else if (Event.current.type == EventType.Repaint)
			{
				GUIContent content;
				if (layerID.hasMultipleDifferentValues)
				{
					content = EditorGUI.mixedValueContent;
				}
				else
				{
					content = EditorGUIUtility.TempContent(InternalEditorUtility.GetSortingLayerNameFromUniqueID(layerID.intValue));
				}
				EditorGUI.showMixedValue = layerID.hasMultipleDifferentValues;
				EditorGUI.BeginHandleMixedValueContentColor();
				style.Draw(position, content, controlID, false);
				EditorGUI.EndHandleMixedValueContentColor();
				EditorGUI.showMixedValue = false;
			}
		}

		internal static int DoPopup(Rect position, int controlID, int selected, GUIContent[] popupValues, GUIStyle style)
		{
			selected = EditorGUI.PopupCallbackInfo.GetSelectedValueForControl(controlID, selected);
			GUIContent gUIContent = null;
			if (gUIContent == null)
			{
				if (EditorGUI.showMixedValue)
				{
					gUIContent = EditorGUI.s_MixedValueContent;
				}
				else if (selected < 0 || selected >= popupValues.Length)
				{
					gUIContent = GUIContent.none;
				}
				else
				{
					gUIContent = popupValues[selected];
				}
			}
			Event current = Event.current;
			EventType type = current.type;
			if (type != EventType.Repaint)
			{
				if (type != EventType.MouseDown)
				{
					if (type == EventType.KeyDown)
					{
						if (current.MainActionKeyForControl(controlID))
						{
							if (Application.platform == RuntimePlatform.OSXEditor)
							{
								position.y = position.y - (float)(selected * 16) - 19f;
							}
							EditorGUI.PopupCallbackInfo.instance = new EditorGUI.PopupCallbackInfo(controlID);
							EditorUtility.DisplayCustomMenu(position, popupValues, (!EditorGUI.showMixedValue) ? selected : -1, new EditorUtility.SelectMenuItemFunction(EditorGUI.PopupCallbackInfo.instance.SetEnumValueDelegate), null);
							current.Use();
						}
					}
				}
				else if (current.button == 0 && position.Contains(current.mousePosition))
				{
					if (Application.platform == RuntimePlatform.OSXEditor)
					{
						position.y = position.y - (float)(selected * 16) - 19f;
					}
					EditorGUI.PopupCallbackInfo.instance = new EditorGUI.PopupCallbackInfo(controlID);
					EditorUtility.DisplayCustomMenu(position, popupValues, (!EditorGUI.showMixedValue) ? selected : -1, new EditorUtility.SelectMenuItemFunction(EditorGUI.PopupCallbackInfo.instance.SetEnumValueDelegate), null);
					GUIUtility.keyboardControl = controlID;
					current.Use();
				}
			}
			else
			{
				Font font = style.font;
				if (font && EditorGUIUtility.GetBoldDefaultFont() && font == EditorStyles.miniFont)
				{
					style.font = EditorStyles.miniBoldFont;
				}
				EditorGUI.BeginHandleMixedValueContentColor();
				style.Draw(position, gUIContent, controlID, false);
				EditorGUI.EndHandleMixedValueContentColor();
				style.font = font;
			}
			return selected;
		}

		internal static string TagFieldInternal(Rect position, string tag, GUIStyle style)
		{
			position = EditorGUI.IndentedRect(position);
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TagFieldHash, FocusType.Keyboard, position);
			Event current = Event.current;
			int selectedValueForControl = EditorGUI.PopupCallbackInfo.GetSelectedValueForControl(controlID, -1);
			if (selectedValueForControl != -1)
			{
				string[] tags = InternalEditorUtility.tags;
				if (selectedValueForControl >= tags.Length)
				{
					TagManagerInspector.ShowWithInitialExpansion(TagManagerInspector.InitialExpansionState.Tags);
				}
				else
				{
					tag = tags[selectedValueForControl];
				}
			}
			string result;
			if ((current.type == EventType.MouseDown && position.Contains(current.mousePosition)) || current.MainActionKeyForControl(controlID))
			{
				string[] tags2 = InternalEditorUtility.tags;
				int i;
				for (i = 0; i < tags2.Length; i++)
				{
					if (tags2[i] == tag)
					{
						break;
					}
				}
				ArrayUtility.Add<string>(ref tags2, "");
				ArrayUtility.Add<string>(ref tags2, L10n.Tr("Add Tag..."));
				EditorGUI.DoPopup(position, controlID, i, EditorGUIUtility.TempContent(tags2), style);
				result = tag;
			}
			else
			{
				if (Event.current.type == EventType.Repaint)
				{
					EditorGUI.BeginHandleMixedValueContentColor();
					style.Draw(position, (!EditorGUI.showMixedValue) ? EditorGUIUtility.TempContent(tag) : EditorGUI.s_MixedValueContent, controlID, false);
					EditorGUI.EndHandleMixedValueContentColor();
				}
				result = tag;
			}
			return result;
		}

		internal static string TagFieldInternal(Rect position, GUIContent label, string tag, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TagFieldHash, FocusType.Keyboard, position);
			position = EditorGUI.PrefixLabel(position, controlID, label);
			Event current = Event.current;
			int selectedValueForControl = EditorGUI.PopupCallbackInfo.GetSelectedValueForControl(controlID, -1);
			if (selectedValueForControl != -1)
			{
				string[] tags = InternalEditorUtility.tags;
				if (selectedValueForControl >= tags.Length)
				{
					TagManagerInspector.ShowWithInitialExpansion(TagManagerInspector.InitialExpansionState.Tags);
				}
				else
				{
					tag = tags[selectedValueForControl];
				}
			}
			string result;
			if ((current.type == EventType.MouseDown && position.Contains(current.mousePosition)) || current.MainActionKeyForControl(controlID))
			{
				string[] tags2 = InternalEditorUtility.tags;
				int i;
				for (i = 0; i < tags2.Length; i++)
				{
					if (tags2[i] == tag)
					{
						break;
					}
				}
				ArrayUtility.Add<string>(ref tags2, "");
				ArrayUtility.Add<string>(ref tags2, L10n.Tr("Add Tag..."));
				EditorGUI.DoPopup(position, controlID, i, EditorGUIUtility.TempContent(tags2), style);
				result = tag;
			}
			else
			{
				if (Event.current.type == EventType.Repaint)
				{
					style.Draw(position, EditorGUIUtility.TempContent(tag), controlID, false);
				}
				result = tag;
			}
			return result;
		}

		internal static int LayerFieldInternal(Rect position, GUIContent label, int layer, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TagFieldHash, FocusType.Keyboard, position);
			position = EditorGUI.PrefixLabel(position, controlID, label);
			Event current = Event.current;
			bool changed = GUI.changed;
			int selectedValueForControl = EditorGUI.PopupCallbackInfo.GetSelectedValueForControl(controlID, -1);
			if (selectedValueForControl != -1)
			{
				if (selectedValueForControl >= InternalEditorUtility.layers.Length)
				{
					TagManagerInspector.ShowWithInitialExpansion(TagManagerInspector.InitialExpansionState.Layers);
					GUI.changed = changed;
				}
				else
				{
					int num = 0;
					for (int i = 0; i < 32; i++)
					{
						if (InternalEditorUtility.GetLayerName(i).Length != 0)
						{
							if (num == selectedValueForControl)
							{
								layer = i;
								break;
							}
							num++;
						}
					}
				}
			}
			int result;
			if ((current.type == EventType.MouseDown && position.Contains(current.mousePosition)) || current.MainActionKeyForControl(controlID))
			{
				int num2 = 0;
				for (int j = 0; j < 32; j++)
				{
					if (InternalEditorUtility.GetLayerName(j).Length != 0)
					{
						if (j == layer)
						{
							break;
						}
						num2++;
					}
				}
				string[] layersWithId = InternalEditorUtility.GetLayersWithId();
				ArrayUtility.Add<string>(ref layersWithId, "");
				ArrayUtility.Add<string>(ref layersWithId, L10n.Tr("Add Layer..."));
				EditorGUI.DoPopup(position, controlID, num2, EditorGUIUtility.TempContent(layersWithId), style);
				Event.current.Use();
				result = layer;
			}
			else
			{
				if (current.type == EventType.Repaint)
				{
					style.Draw(position, EditorGUIUtility.TempContent(InternalEditorUtility.GetLayerName(layer)), controlID, false);
				}
				result = layer;
			}
			return result;
		}

		private static EditorGUI.EnumData GetNonObsoleteEnumData(Type enumType)
		{
			EditorGUI.EnumData enumData;
			if (!EditorGUI.s_NonObsoleteEnumData.TryGetValue(enumType, out enumData))
			{
				enumData = default(EditorGUI.EnumData);
				enumData.underlyingType = Enum.GetUnderlyingType(enumType);
				enumData.unsigned = (enumData.underlyingType == typeof(byte) || enumData.underlyingType == typeof(ushort) || enumData.underlyingType == typeof(uint) || enumData.underlyingType == typeof(ulong));
				enumData.displayNames = (from n in Enum.GetNames(enumType)
				where enumType.GetField(n).GetCustomAttributes(typeof(ObsoleteAttribute), false).Length == 0
				select n).ToArray<string>();
				enumData.values = (from n in enumData.displayNames
				select (Enum)Enum.Parse(enumType, n)).ToArray<Enum>();
				int[] arg_15A_1;
				if (enumData.unsigned)
				{
					arg_15A_1 = (from v in enumData.values
					select (int)Convert.ToUInt64(v)).ToArray<int>();
				}
				else
				{
					arg_15A_1 = (from v in enumData.values
					select (int)Convert.ToInt64(v)).ToArray<int>();
				}
				enumData.flagValues = arg_15A_1;
				int i = 0;
				int num = enumData.displayNames.Length;
				while (i < num)
				{
					enumData.displayNames[i] = ObjectNames.NicifyVariableName(enumData.displayNames[i]);
					i++;
				}
				if (enumData.underlyingType == typeof(ushort))
				{
					int j = 0;
					int num2 = enumData.flagValues.Length;
					while (j < num2)
					{
						if ((long)enumData.flagValues[j] == 65535L)
						{
							enumData.flagValues[j] = -1;
						}
						j++;
					}
				}
				else if (enumData.underlyingType == typeof(byte))
				{
					int k = 0;
					int num3 = enumData.flagValues.Length;
					while (k < num3)
					{
						if ((long)enumData.flagValues[k] == 255L)
						{
							enumData.flagValues[k] = -1;
						}
						k++;
					}
				}
				enumData.flags = (enumType.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0);
				enumData.serializable = (enumData.underlyingType != typeof(long) && enumData.underlyingType != typeof(ulong));
				EditorGUI.s_NonObsoleteEnumData[enumType] = enumData;
			}
			return enumData;
		}

		internal static int MaskFieldInternal(Rect position, GUIContent label, int mask, string[] displayedOptions, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_MaskField, FocusType.Keyboard, position);
			position = EditorGUI.PrefixLabel(position, controlID, label);
			return MaskFieldGUI.DoMaskField(position, controlID, mask, displayedOptions, style);
		}

		internal static int MaskFieldInternal(Rect position, GUIContent label, int mask, string[] displayedOptions, int[] optionValues, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_MaskField, FocusType.Keyboard, position);
			position = EditorGUI.PrefixLabel(position, controlID, label);
			return MaskFieldGUI.DoMaskField(position, controlID, mask, displayedOptions, optionValues, style);
		}

		internal static int MaskFieldInternal(Rect position, int mask, string[] displayedOptions, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_MaskField, FocusType.Keyboard, position);
			return MaskFieldGUI.DoMaskField(EditorGUI.IndentedRect(position), controlID, mask, displayedOptions, style);
		}

		public static Enum EnumFlagsField(Rect position, Enum enumValue)
		{
			return EditorGUI.EnumFlagsField(position, enumValue, EditorStyles.popup);
		}

		public static Enum EnumFlagsField(Rect position, Enum enumValue, GUIStyle style)
		{
			return EditorGUI.EnumFlagsField(position, GUIContent.none, enumValue, style);
		}

		public static Enum EnumFlagsField(Rect position, string label, Enum enumValue)
		{
			return EditorGUI.EnumFlagsField(position, label, enumValue, EditorStyles.popup);
		}

		public static Enum EnumFlagsField(Rect position, string label, Enum enumValue, GUIStyle style)
		{
			return EditorGUI.EnumFlagsField(position, EditorGUIUtility.TempContent(label), enumValue, style);
		}

		public static Enum EnumFlagsField(Rect position, GUIContent label, Enum enumValue)
		{
			return EditorGUI.EnumFlagsField(position, label, enumValue, EditorStyles.popup);
		}

		public static Enum EnumFlagsField(Rect position, GUIContent label, Enum enumValue, GUIStyle style)
		{
			int num;
			bool flag;
			return EditorGUI.EnumFlagsField(position, label, enumValue, out num, out flag, style);
		}

		internal static Enum EnumFlagsField(Rect position, GUIContent label, Enum enumValue, out int changedFlags, out bool changedToValue, GUIStyle style)
		{
			Type type = enumValue.GetType();
			if (!type.IsEnum)
			{
				throw new ArgumentException("Parameter enumValue must be of type System.Enum", "enumValue");
			}
			EditorGUI.EnumData nonObsoleteEnumData = EditorGUI.GetNonObsoleteEnumData(type);
			if (!nonObsoleteEnumData.serializable)
			{
				throw new NotSupportedException(string.Format("Unsupported enum base type for {0}", type.Name));
			}
			int controlID = GUIUtility.GetControlID(EditorGUI.s_EnumFlagsField, FocusType.Keyboard, position);
			position = EditorGUI.PrefixLabel(position, controlID, label);
			int num = EditorGUI.EnumFlagsToInt(nonObsoleteEnumData, enumValue);
			EditorGUI.BeginChangeCheck();
			num = MaskFieldGUI.DoMaskField(position, controlID, num, nonObsoleteEnumData.displayNames, nonObsoleteEnumData.flagValues, style, out changedFlags, out changedToValue);
			Enum result;
			if (!EditorGUI.EndChangeCheck())
			{
				result = enumValue;
			}
			else
			{
				result = EditorGUI.IntToEnumFlags(type, num);
			}
			return result;
		}

		public static void ObjectField(Rect position, SerializedProperty property)
		{
			EditorGUI.ObjectField(position, property, null, null, EditorStyles.objectField);
		}

		public static void ObjectField(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.ObjectField(position, property, null, label, EditorStyles.objectField);
		}

		public static void ObjectField(Rect position, SerializedProperty property, Type objType)
		{
			EditorGUI.ObjectField(position, property, objType, null, EditorStyles.objectField);
		}

		public static void ObjectField(Rect position, SerializedProperty property, Type objType, GUIContent label)
		{
			EditorGUI.ObjectField(position, property, objType, label, EditorStyles.objectField);
		}

		internal static void ObjectField(Rect position, SerializedProperty property, Type objType, GUIContent label, GUIStyle style)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			EditorGUI.ObjectFieldInternal(position, property, objType, label, style);
			EditorGUI.EndProperty();
		}

		private static void ObjectFieldInternal(Rect position, SerializedProperty property, Type objType, GUIContent label, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_PPtrHash, FocusType.Keyboard, position);
			position = EditorGUI.PrefixLabel(position, controlID, label);
			bool allowSceneObjects = false;
			if (property != null)
			{
				UnityEngine.Object targetObject = property.serializedObject.targetObject;
				if (targetObject != null && !EditorUtility.IsPersistent(targetObject))
				{
					allowSceneObjects = true;
				}
			}
			EditorGUI.DoObjectField(position, position, controlID, null, objType, property, null, allowSceneObjects, style);
		}

		public static UnityEngine.Object ObjectField(Rect position, UnityEngine.Object obj, Type objType, bool allowSceneObjects)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ObjectFieldHash, FocusType.Keyboard, position);
			return EditorGUI.DoObjectField(EditorGUI.IndentedRect(position), EditorGUI.IndentedRect(position), controlID, obj, objType, null, null, allowSceneObjects);
		}

		[Obsolete("Check the docs for the usage of the new parameter 'allowSceneObjects'.")]
		public static UnityEngine.Object ObjectField(Rect position, UnityEngine.Object obj, Type objType)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ObjectFieldHash, FocusType.Keyboard, position);
			return EditorGUI.DoObjectField(position, position, controlID, obj, objType, null, null, true);
		}

		public static UnityEngine.Object ObjectField(Rect position, string label, UnityEngine.Object obj, Type objType, bool allowSceneObjects)
		{
			return EditorGUI.ObjectField(position, EditorGUIUtility.TempContent(label), obj, objType, allowSceneObjects);
		}

		[Obsolete("Check the docs for the usage of the new parameter 'allowSceneObjects'.")]
		public static UnityEngine.Object ObjectField(Rect position, string label, UnityEngine.Object obj, Type objType)
		{
			return EditorGUI.ObjectField(position, EditorGUIUtility.TempContent(label), obj, objType, true);
		}

		public static UnityEngine.Object ObjectField(Rect position, GUIContent label, UnityEngine.Object obj, Type objType, bool allowSceneObjects)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ObjectFieldHash, FocusType.Keyboard, position);
			position = EditorGUI.PrefixLabel(position, controlID, label);
			if (EditorGUIUtility.HasObjectThumbnail(objType) && position.height > 16f)
			{
				float num = Mathf.Min(position.width, position.height);
				position.height = num;
				position.xMin = position.xMax - num;
			}
			return EditorGUI.DoObjectField(position, position, controlID, obj, objType, null, null, allowSceneObjects);
		}

		internal static void GetRectsForMiniThumbnailField(Rect position, out Rect thumbRect, out Rect labelRect)
		{
			thumbRect = EditorGUI.IndentedRect(position);
			thumbRect.y -= 1f;
			thumbRect.height = 18f;
			thumbRect.width = 32f;
			float num = thumbRect.x + 30f;
			labelRect = new Rect(num, position.y, thumbRect.x + EditorGUIUtility.labelWidth - num, position.height);
		}

		internal static UnityEngine.Object MiniThumbnailObjectField(Rect position, GUIContent label, UnityEngine.Object obj, Type objType)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ObjectFieldHash, FocusType.Keyboard, position);
			Rect rect;
			Rect labelPosition;
			EditorGUI.GetRectsForMiniThumbnailField(position, out rect, out labelPosition);
			EditorGUI.HandlePrefixLabel(position, labelPosition, label, controlID, EditorStyles.label);
			return EditorGUI.DoObjectField(rect, rect, controlID, obj, objType, null, null, false);
		}

		[Obsolete("Check the docs for the usage of the new parameter 'allowSceneObjects'.")]
		public static UnityEngine.Object ObjectField(Rect position, GUIContent label, UnityEngine.Object obj, Type objType)
		{
			return EditorGUI.ObjectField(position, label, obj, objType, true);
		}

		internal static GameObject GetGameObjectFromObject(UnityEngine.Object obj)
		{
			GameObject gameObject = obj as GameObject;
			if (gameObject == null && obj is Component)
			{
				gameObject = ((Component)obj).gameObject;
			}
			return gameObject;
		}

		internal static bool CheckForCrossSceneReferencing(UnityEngine.Object obj1, UnityEngine.Object obj2)
		{
			GameObject gameObjectFromObject = EditorGUI.GetGameObjectFromObject(obj1);
			bool result;
			if (gameObjectFromObject == null)
			{
				result = false;
			}
			else
			{
				GameObject gameObjectFromObject2 = EditorGUI.GetGameObjectFromObject(obj2);
				result = (!(gameObjectFromObject2 == null) && !EditorUtility.IsPersistent(gameObjectFromObject) && !EditorUtility.IsPersistent(gameObjectFromObject2) && gameObjectFromObject.scene.IsValid() && gameObjectFromObject2.scene.IsValid() && gameObjectFromObject.scene != gameObjectFromObject2.scene);
			}
			return result;
		}

		private static bool ValidateObjectReferenceValue(SerializedProperty property, UnityEngine.Object obj, EditorGUI.ObjectFieldValidatorOptions options)
		{
			bool result;
			if ((options & EditorGUI.ObjectFieldValidatorOptions.ExactObjectTypeValidation) == EditorGUI.ObjectFieldValidatorOptions.ExactObjectTypeValidation)
			{
				result = property.ValidateObjectReferenceValueExact(obj);
			}
			else
			{
				result = property.ValidateObjectReferenceValue(obj);
			}
			return result;
		}

		internal static UnityEngine.Object ValidateObjectFieldAssignment(UnityEngine.Object[] references, Type objType, SerializedProperty property, EditorGUI.ObjectFieldValidatorOptions options)
		{
			UnityEngine.Object result;
			if (references.Length > 0)
			{
				bool flag = DragAndDrop.objectReferences.Length > 0;
				bool flag2 = references[0] != null && references[0].GetType() == typeof(Texture2D);
				if (objType == typeof(Sprite) && flag2 && flag)
				{
					result = SpriteUtility.TextureToSprite(references[0] as Texture2D);
					return result;
				}
				if (property != null)
				{
					if (references[0] != null && EditorGUI.ValidateObjectReferenceValue(property, references[0], options))
					{
						if (EditorSceneManager.preventCrossSceneReferences && EditorGUI.CheckForCrossSceneReferencing(references[0], property.serializedObject.targetObject))
						{
							result = null;
							return result;
						}
						if (objType == null)
						{
							result = references[0];
							return result;
						}
						if (references[0].GetType() == typeof(GameObject) && typeof(Component).IsAssignableFrom(objType))
						{
							GameObject gameObject = (GameObject)references[0];
							references = gameObject.GetComponents(typeof(Component));
						}
						UnityEngine.Object[] array = references;
						for (int i = 0; i < array.Length; i++)
						{
							UnityEngine.Object @object = array[i];
							if (@object != null && objType.IsAssignableFrom(@object.GetType()))
							{
								result = @object;
								return result;
							}
						}
					}
					string a = property.type;
					if (property.type == "vector")
					{
						a = property.arrayElementType;
					}
					if ((a == "PPtr<Sprite>" || a == "PPtr<$Sprite>") && flag2 && flag)
					{
						result = SpriteUtility.TextureToSprite(references[0] as Texture2D);
						return result;
					}
				}
				else
				{
					if (references[0] != null && references[0].GetType() == typeof(GameObject) && typeof(Component).IsAssignableFrom(objType))
					{
						GameObject gameObject2 = (GameObject)references[0];
						references = gameObject2.GetComponents(typeof(Component));
					}
					UnityEngine.Object[] array2 = references;
					for (int j = 0; j < array2.Length; j++)
					{
						UnityEngine.Object object2 = array2[j];
						if (object2 != null && objType.IsAssignableFrom(object2.GetType()))
						{
							result = object2;
							return result;
						}
					}
				}
			}
			result = null;
			return result;
		}

		private static UnityEngine.Object HandleTextureToSprite(Texture2D tex)
		{
			UnityEngine.Object[] array = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(tex));
			UnityEngine.Object result;
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i].GetType() == typeof(Sprite))
				{
					result = array[i];
					return result;
				}
			}
			result = tex;
			return result;
		}

		public static Rect IndentedRect(Rect source)
		{
			float indent = EditorGUI.indent;
			return new Rect(source.x + indent, source.y, source.width - indent, source.height);
		}

		public static Vector2 Vector2Field(Rect position, string label, Vector2 value)
		{
			return EditorGUI.Vector2Field(position, EditorGUIUtility.TempContent(label), value);
		}

		public static Vector2 Vector2Field(Rect position, GUIContent label, Vector2 value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 2);
			position.height = 16f;
			return EditorGUI.Vector2Field(position, value);
		}

		private static Vector2 Vector2Field(Rect position, Vector2 value)
		{
			EditorGUI.s_Vector2Floats[0] = value.x;
			EditorGUI.s_Vector2Floats[1] = value.y;
			position.height = 16f;
			EditorGUI.BeginChangeCheck();
			EditorGUI.MultiFloatField(position, EditorGUI.s_XYLabels, EditorGUI.s_Vector2Floats);
			if (EditorGUI.EndChangeCheck())
			{
				value.x = EditorGUI.s_Vector2Floats[0];
				value.y = EditorGUI.s_Vector2Floats[1];
			}
			return value;
		}

		public static Vector3 Vector3Field(Rect position, string label, Vector3 value)
		{
			return EditorGUI.Vector3Field(position, EditorGUIUtility.TempContent(label), value);
		}

		public static Vector3 Vector3Field(Rect position, GUIContent label, Vector3 value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 3);
			position.height = 16f;
			return EditorGUI.Vector3Field(position, value);
		}

		private static Vector3 Vector3Field(Rect position, Vector3 value)
		{
			EditorGUI.s_Vector3Floats[0] = value.x;
			EditorGUI.s_Vector3Floats[1] = value.y;
			EditorGUI.s_Vector3Floats[2] = value.z;
			position.height = 16f;
			EditorGUI.BeginChangeCheck();
			EditorGUI.MultiFloatField(position, EditorGUI.s_XYZLabels, EditorGUI.s_Vector3Floats);
			if (EditorGUI.EndChangeCheck())
			{
				value.x = EditorGUI.s_Vector3Floats[0];
				value.y = EditorGUI.s_Vector3Floats[1];
				value.z = EditorGUI.s_Vector3Floats[2];
			}
			return value;
		}

		private static void Vector2Field(Rect position, SerializedProperty property, GUIContent label)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 2);
			position.height = 16f;
			SerializedProperty serializedProperty = property.Copy();
			serializedProperty.Next(true);
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
		}

		private static void Vector3Field(Rect position, SerializedProperty property, GUIContent label)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 3);
			position.height = 16f;
			SerializedProperty serializedProperty = property.Copy();
			serializedProperty.Next(true);
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYZLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
		}

		private static void Vector4Field(Rect position, SerializedProperty property, GUIContent label)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 4);
			position.height = 16f;
			SerializedProperty serializedProperty = property.Copy();
			serializedProperty.Next(true);
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYZWLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
		}

		public static Vector4 Vector4Field(Rect position, string label, Vector4 value)
		{
			return EditorGUI.Vector4Field(position, EditorGUIUtility.TempContent(label), value);
		}

		public static Vector4 Vector4Field(Rect position, GUIContent label, Vector4 value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 4);
			position.height = 16f;
			return EditorGUI.Vector4FieldNoIndent(position, value);
		}

		private static Vector4 Vector4FieldNoIndent(Rect position, Vector4 value)
		{
			EditorGUI.s_Vector4Floats[0] = value.x;
			EditorGUI.s_Vector4Floats[1] = value.y;
			EditorGUI.s_Vector4Floats[2] = value.z;
			EditorGUI.s_Vector4Floats[3] = value.w;
			position.height = 16f;
			EditorGUI.BeginChangeCheck();
			EditorGUI.MultiFloatField(position, EditorGUI.s_XYZWLabels, EditorGUI.s_Vector4Floats);
			if (EditorGUI.EndChangeCheck())
			{
				value.x = EditorGUI.s_Vector4Floats[0];
				value.y = EditorGUI.s_Vector4Floats[1];
				value.z = EditorGUI.s_Vector4Floats[2];
				value.w = EditorGUI.s_Vector4Floats[3];
			}
			return value;
		}

		public static Vector2Int Vector2IntField(Rect position, string label, Vector2Int value)
		{
			return EditorGUI.Vector2IntField(position, EditorGUIUtility.TempContent(label), value);
		}

		public static Vector2Int Vector2IntField(Rect position, GUIContent label, Vector2Int value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 2);
			position.height = 16f;
			return EditorGUI.Vector2IntField(position, value);
		}

		private static Vector2Int Vector2IntField(Rect position, Vector2Int value)
		{
			EditorGUI.s_Vector2Ints[0] = value.x;
			EditorGUI.s_Vector2Ints[1] = value.y;
			position.height = 16f;
			EditorGUI.BeginChangeCheck();
			EditorGUI.MultiIntField(position, EditorGUI.s_XYLabels, EditorGUI.s_Vector2Ints);
			if (EditorGUI.EndChangeCheck())
			{
				value.x = EditorGUI.s_Vector2Ints[0];
				value.y = EditorGUI.s_Vector2Ints[1];
			}
			return value;
		}

		private static void Vector2IntField(Rect position, SerializedProperty property, GUIContent label)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 2);
			position.height = 16f;
			SerializedProperty serializedProperty = property.Copy();
			serializedProperty.Next(true);
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
		}

		public static Vector3Int Vector3IntField(Rect position, string label, Vector3Int value)
		{
			return EditorGUI.Vector3IntField(position, EditorGUIUtility.TempContent(label), value);
		}

		public static Vector3Int Vector3IntField(Rect position, GUIContent label, Vector3Int value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 3);
			position.height = 16f;
			return EditorGUI.Vector3IntField(position, value);
		}

		private static Vector3Int Vector3IntField(Rect position, Vector3Int value)
		{
			EditorGUI.s_Vector3Ints[0] = value.x;
			EditorGUI.s_Vector3Ints[1] = value.y;
			EditorGUI.s_Vector3Ints[2] = value.z;
			position.height = 16f;
			EditorGUI.BeginChangeCheck();
			EditorGUI.MultiIntField(position, EditorGUI.s_XYZLabels, EditorGUI.s_Vector3Ints);
			if (EditorGUI.EndChangeCheck())
			{
				value.x = EditorGUI.s_Vector3Ints[0];
				value.y = EditorGUI.s_Vector3Ints[1];
				value.z = EditorGUI.s_Vector3Ints[2];
			}
			return value;
		}

		private static void Vector3IntField(Rect position, SerializedProperty property, GUIContent label)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 3);
			position.height = 16f;
			SerializedProperty serializedProperty = property.Copy();
			serializedProperty.Next(true);
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYZLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
		}

		public static Rect RectField(Rect position, Rect value)
		{
			return EditorGUI.RectFieldNoIndent(EditorGUI.IndentedRect(position), value);
		}

		private static Rect RectFieldNoIndent(Rect position, Rect value)
		{
			position.height = 16f;
			EditorGUI.s_Vector2Floats[0] = value.x;
			EditorGUI.s_Vector2Floats[1] = value.y;
			EditorGUI.BeginChangeCheck();
			EditorGUI.MultiFloatField(position, EditorGUI.s_XYLabels, EditorGUI.s_Vector2Floats);
			if (EditorGUI.EndChangeCheck())
			{
				value.x = EditorGUI.s_Vector2Floats[0];
				value.y = EditorGUI.s_Vector2Floats[1];
			}
			position.y += 16f;
			EditorGUI.s_Vector2Floats[0] = value.width;
			EditorGUI.s_Vector2Floats[1] = value.height;
			EditorGUI.BeginChangeCheck();
			EditorGUI.MultiFloatField(position, EditorGUI.s_WHLabels, EditorGUI.s_Vector2Floats);
			if (EditorGUI.EndChangeCheck())
			{
				value.width = EditorGUI.s_Vector2Floats[0];
				value.height = EditorGUI.s_Vector2Floats[1];
			}
			return value;
		}

		public static Rect RectField(Rect position, string label, Rect value)
		{
			return EditorGUI.RectField(position, EditorGUIUtility.TempContent(label), value);
		}

		public static Rect RectField(Rect position, GUIContent label, Rect value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 2);
			return EditorGUI.RectFieldNoIndent(position, value);
		}

		private static void RectField(Rect position, SerializedProperty property, GUIContent label)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 2);
			position.height = 16f;
			SerializedProperty serializedProperty = property.Copy();
			serializedProperty.Next(true);
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
			position.y += 16f;
			EditorGUI.MultiPropertyField(position, EditorGUI.s_WHLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
		}

		public static RectInt RectIntField(Rect position, RectInt value)
		{
			position.height = 16f;
			EditorGUI.s_Vector2Ints[0] = value.x;
			EditorGUI.s_Vector2Ints[1] = value.y;
			EditorGUI.BeginChangeCheck();
			EditorGUI.MultiIntField(position, EditorGUI.s_XYLabels, EditorGUI.s_Vector2Ints);
			if (EditorGUI.EndChangeCheck())
			{
				value.x = EditorGUI.s_Vector2Ints[0];
				value.y = EditorGUI.s_Vector2Ints[1];
			}
			position.y += 16f;
			EditorGUI.s_Vector2Ints[0] = value.width;
			EditorGUI.s_Vector2Ints[1] = value.height;
			EditorGUI.BeginChangeCheck();
			EditorGUI.MultiIntField(position, EditorGUI.s_WHLabels, EditorGUI.s_Vector2Ints);
			if (EditorGUI.EndChangeCheck())
			{
				value.width = EditorGUI.s_Vector2Ints[0];
				value.height = EditorGUI.s_Vector2Ints[1];
			}
			return value;
		}

		public static RectInt RectIntField(Rect position, string label, RectInt value)
		{
			return EditorGUI.RectIntField(position, EditorGUIUtility.TempContent(label), value);
		}

		public static RectInt RectIntField(Rect position, GUIContent label, RectInt value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 2);
			position.height = 16f;
			return EditorGUI.RectIntField(position, value);
		}

		private static void RectIntField(Rect position, SerializedProperty property, GUIContent label)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 2);
			position.height = 16f;
			SerializedProperty serializedProperty = property.Copy();
			serializedProperty.Next(true);
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
			position.y += 16f;
			EditorGUI.MultiPropertyField(position, EditorGUI.s_WHLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
		}

		private static Rect DrawBoundsFieldLabelsAndAdjustPositionForValues(Rect position, bool drawOutside, GUIContent firstContent, GUIContent secondContent)
		{
			if (drawOutside)
			{
				position.xMin -= 53f;
			}
			GUI.Label(position, firstContent, EditorStyles.label);
			position.y += 16f;
			GUI.Label(position, secondContent, EditorStyles.label);
			position.y -= 16f;
			position.xMin += 53f;
			return position;
		}

		public static Bounds BoundsField(Rect position, Bounds value)
		{
			return EditorGUI.BoundsFieldNoIndent(EditorGUI.IndentedRect(position), value, false);
		}

		public static Bounds BoundsField(Rect position, string label, Bounds value)
		{
			return EditorGUI.BoundsField(position, EditorGUIUtility.TempContent(label), value);
		}

		public static Bounds BoundsField(Rect position, GUIContent label, Bounds value)
		{
			Bounds result;
			if (!EditorGUI.LabelHasContent(label))
			{
				result = EditorGUI.BoundsFieldNoIndent(EditorGUI.IndentedRect(position), value, false);
			}
			else
			{
				int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
				position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 3);
				if (EditorGUIUtility.wideMode)
				{
					position.y += 16f;
				}
				result = EditorGUI.BoundsFieldNoIndent(position, value, true);
			}
			return result;
		}

		private static Bounds BoundsFieldNoIndent(Rect position, Bounds value, bool isBelowLabel)
		{
			position.height = 16f;
			position = EditorGUI.DrawBoundsFieldLabelsAndAdjustPositionForValues(position, EditorGUIUtility.wideMode && isBelowLabel, EditorGUI.s_CenterLabel, EditorGUI.s_ExtentLabel);
			value.center = EditorGUI.Vector3Field(position, value.center);
			position.y += 16f;
			value.extents = EditorGUI.Vector3Field(position, value.extents);
			return value;
		}

		private static void BoundsField(Rect position, SerializedProperty property, GUIContent label)
		{
			bool flag = EditorGUI.LabelHasContent(label);
			if (flag)
			{
				int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
				position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 3);
				if (EditorGUIUtility.wideMode)
				{
					position.y += 16f;
				}
			}
			position.height = 16f;
			position = EditorGUI.DrawBoundsFieldLabelsAndAdjustPositionForValues(position, EditorGUIUtility.wideMode && flag, EditorGUI.s_CenterLabel, EditorGUI.s_ExtentLabel);
			SerializedProperty serializedProperty = property.Copy();
			serializedProperty.Next(true);
			serializedProperty.Next(true);
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYZLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
			serializedProperty.Next(true);
			position.y += 16f;
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYZLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
		}

		public static BoundsInt BoundsIntField(Rect position, BoundsInt value)
		{
			return EditorGUI.BoundsIntFieldNoIndent(EditorGUI.IndentedRect(position), value, false);
		}

		public static BoundsInt BoundsIntField(Rect position, string label, BoundsInt value)
		{
			return EditorGUI.BoundsIntField(position, EditorGUIUtility.TempContent(label), value);
		}

		public static BoundsInt BoundsIntField(Rect position, GUIContent label, BoundsInt value)
		{
			BoundsInt result;
			if (!EditorGUI.LabelHasContent(label))
			{
				result = EditorGUI.BoundsIntFieldNoIndent(EditorGUI.IndentedRect(position), value, false);
			}
			else
			{
				int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
				position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 3);
				if (EditorGUIUtility.wideMode)
				{
					position.y += 16f;
				}
				result = EditorGUI.BoundsIntFieldNoIndent(position, value, true);
			}
			return result;
		}

		private static BoundsInt BoundsIntFieldNoIndent(Rect position, BoundsInt value, bool isBelowLabel)
		{
			position.height = 16f;
			position = EditorGUI.DrawBoundsFieldLabelsAndAdjustPositionForValues(position, EditorGUIUtility.wideMode && isBelowLabel, EditorGUI.s_PositionLabel, EditorGUI.s_SizeLabel);
			value.position = EditorGUI.Vector3IntField(position, value.position);
			position.y += 16f;
			value.size = EditorGUI.Vector3IntField(position, value.size);
			return value;
		}

		private static void BoundsIntField(Rect position, SerializedProperty property, GUIContent label)
		{
			bool flag = EditorGUI.LabelHasContent(label);
			if (flag)
			{
				int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
				position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, 3);
				if (EditorGUIUtility.wideMode)
				{
					position.y += 16f;
				}
			}
			position.height = 16f;
			position = EditorGUI.DrawBoundsFieldLabelsAndAdjustPositionForValues(position, EditorGUIUtility.wideMode && flag, EditorGUI.s_PositionLabel, EditorGUI.s_SizeLabel);
			SerializedProperty serializedProperty = property.Copy();
			serializedProperty.Next(true);
			serializedProperty.Next(true);
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYZLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
			serializedProperty.Next(true);
			position.y += 16f;
			EditorGUI.MultiPropertyField(position, EditorGUI.s_XYZLabels, serializedProperty, EditorGUI.PropertyVisibility.All);
		}

		public static void MultiFloatField(Rect position, GUIContent label, GUIContent[] subLabels, float[] values)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, subLabels.Length);
			position.height = 16f;
			EditorGUI.MultiFloatField(position, subLabels, values);
		}

		public static void MultiFloatField(Rect position, GUIContent[] subLabels, float[] values)
		{
			EditorGUI.MultiFloatField(position, subLabels, values, 13f);
		}

		internal static void MultiFloatField(Rect position, GUIContent[] subLabels, float[] values, float labelWidth)
		{
			int num = values.Length;
			float num2 = (position.width - (float)(num - 1) * 2f) / (float)num;
			Rect position2 = new Rect(position);
			position2.width = num2;
			float labelWidth2 = EditorGUIUtility.labelWidth;
			int indentLevel = EditorGUI.indentLevel;
			EditorGUIUtility.labelWidth = labelWidth;
			EditorGUI.indentLevel = 0;
			for (int i = 0; i < values.Length; i++)
			{
				values[i] = EditorGUI.FloatField(position2, subLabels[i], values[i]);
				position2.x += num2 + 2f;
			}
			EditorGUIUtility.labelWidth = labelWidth2;
			EditorGUI.indentLevel = indentLevel;
		}

		public static void MultiIntField(Rect position, GUIContent[] subLabels, int[] values)
		{
			EditorGUI.MultiIntField(position, subLabels, values, 13f);
		}

		internal static void MultiIntField(Rect position, GUIContent[] subLabels, int[] values, float labelWidth)
		{
			int num = values.Length;
			float num2 = (position.width - (float)(num - 1) * 2f) / (float)num;
			Rect position2 = new Rect(position);
			position2.width = num2;
			float labelWidth2 = EditorGUIUtility.labelWidth;
			int indentLevel = EditorGUI.indentLevel;
			EditorGUIUtility.labelWidth = labelWidth;
			EditorGUI.indentLevel = 0;
			for (int i = 0; i < values.Length; i++)
			{
				values[i] = EditorGUI.IntField(position2, subLabels[i], values[i]);
				position2.x += num2 + 2f;
			}
			EditorGUIUtility.labelWidth = labelWidth2;
			EditorGUI.indentLevel = indentLevel;
		}

		public static void MultiPropertyField(Rect position, GUIContent[] subLabels, SerializedProperty valuesIterator, GUIContent label)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			position = EditorGUI.MultiFieldPrefixLabel(position, controlID, label, subLabels.Length);
			position.height = 16f;
			EditorGUI.MultiPropertyField(position, subLabels, valuesIterator);
		}

		public static void MultiPropertyField(Rect position, GUIContent[] subLabels, SerializedProperty valuesIterator)
		{
			EditorGUI.MultiPropertyField(position, subLabels, valuesIterator, EditorGUI.PropertyVisibility.OnlyVisible);
		}

		private static void MultiPropertyField(Rect position, GUIContent[] subLabels, SerializedProperty valuesIterator, EditorGUI.PropertyVisibility visibility)
		{
			EditorGUI.MultiPropertyField(position, subLabels, valuesIterator, visibility, 13f, null);
		}

		internal static void MultiPropertyField(Rect position, GUIContent[] subLabels, SerializedProperty valuesIterator, EditorGUI.PropertyVisibility visibility, float labelWidth, bool[] disabledMask)
		{
			int num = subLabels.Length;
			float num2 = (position.width - (float)(num - 1) * 2f) / (float)num;
			Rect position2 = new Rect(position);
			position2.width = num2;
			float labelWidth2 = EditorGUIUtility.labelWidth;
			int indentLevel = EditorGUI.indentLevel;
			EditorGUIUtility.labelWidth = labelWidth;
			EditorGUI.indentLevel = 0;
			for (int i = 0; i < subLabels.Length; i++)
			{
				if (disabledMask != null)
				{
					EditorGUI.BeginDisabled(disabledMask[i]);
				}
				EditorGUI.PropertyField(position2, valuesIterator, subLabels[i]);
				if (disabledMask != null)
				{
					EditorGUI.EndDisabled();
				}
				position2.x += num2 + 2f;
				if (visibility != EditorGUI.PropertyVisibility.All)
				{
					if (visibility == EditorGUI.PropertyVisibility.OnlyVisible)
					{
						valuesIterator.NextVisible(false);
					}
				}
				else
				{
					valuesIterator.Next(false);
				}
			}
			EditorGUIUtility.labelWidth = labelWidth2;
			EditorGUI.indentLevel = indentLevel;
		}

		internal static void PropertiesField(Rect position, GUIContent label, SerializedProperty[] properties, GUIContent[] propertyLabels, float propertyLabelsWidth)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			Rect position2 = EditorGUI.PrefixLabel(position, controlID, label);
			position2.height = 16f;
			float labelWidth = EditorGUIUtility.labelWidth;
			int indentLevel = EditorGUI.indentLevel;
			EditorGUIUtility.labelWidth = propertyLabelsWidth;
			EditorGUI.indentLevel = 0;
			for (int i = 0; i < properties.Length; i++)
			{
				EditorGUI.PropertyField(position2, properties[i], propertyLabels[i]);
				position2.y += 16f;
			}
			EditorGUI.indentLevel = indentLevel;
			EditorGUIUtility.labelWidth = labelWidth;
		}

		internal static int CycleButton(Rect position, int selected, GUIContent[] options, GUIStyle style)
		{
			if (selected >= options.Length || selected < 0)
			{
				selected = 0;
				GUI.changed = true;
			}
			if (GUI.Button(position, options[selected], style))
			{
				selected++;
				GUI.changed = true;
				if (selected >= options.Length)
				{
					selected = 0;
				}
			}
			return selected;
		}

		public static Color ColorField(Rect position, Color value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ColorHash, FocusType.Keyboard, position);
			return EditorGUI.DoColorField(EditorGUI.IndentedRect(position), controlID, value, true, true, false);
		}

		internal static Color ColorField(Rect position, Color value, bool showEyedropper, bool showAlpha)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ColorHash, FocusType.Keyboard, position);
			return EditorGUI.DoColorField(position, controlID, value, showEyedropper, showAlpha, false);
		}

		public static Color ColorField(Rect position, string label, Color value)
		{
			return EditorGUI.ColorField(position, EditorGUIUtility.TempContent(label), value);
		}

		public static Color ColorField(Rect position, GUIContent label, Color value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ColorHash, FocusType.Keyboard, position);
			return EditorGUI.DoColorField(EditorGUI.PrefixLabel(position, controlID, label), controlID, value, true, true, false);
		}

		internal static Color ColorField(Rect position, GUIContent label, Color value, bool showEyedropper, bool showAlpha)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ColorHash, FocusType.Keyboard, position);
			return EditorGUI.DoColorField(EditorGUI.PrefixLabel(position, controlID, label), controlID, value, showEyedropper, showAlpha, false);
		}

		[Obsolete("Use EditorGUI.ColorField(Rect position, GUIContent label, Color value, bool showEyedropper, bool showAlpha, bool hdr)")]
		public static Color ColorField(Rect position, GUIContent label, Color value, bool showEyedropper, bool showAlpha, bool hdr, ColorPickerHDRConfig hdrConfig)
		{
			return EditorGUI.ColorField(position, label, value, showEyedropper, showAlpha, hdr);
		}

		public static Color ColorField(Rect position, GUIContent label, Color value, bool showEyedropper, bool showAlpha, bool hdr)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ColorHash, FocusType.Keyboard, position);
			return EditorGUI.DoColorField(EditorGUI.PrefixLabel(position, controlID, label), controlID, value, showEyedropper, showAlpha, hdr);
		}

		private static Color DoColorField(Rect position, int id, Color value, bool showEyedropper, bool showAlpha, bool hdr)
		{
			Event current = Event.current;
			GUIStyle colorField = EditorStyles.colorField;
			Color color = value;
			value = ((!EditorGUI.showMixedValue) ? value : Color.white);
			EventType typeForControl = current.GetTypeForControl(id);
			Color result;
			switch (typeForControl)
			{
			case EventType.KeyDown:
				if (current.MainActionKeyForControl(id))
				{
					Event.current.Use();
					EditorGUI.showMixedValue = false;
					ColorPicker.Show(GUIView.current, value, showAlpha, hdr);
					GUIUtility.ExitGUI();
				}
				goto IL_418;
			case EventType.KeyUp:
			case EventType.ScrollWheel:
				IL_46:
				if (typeForControl == EventType.ValidateCommand)
				{
					string commandName = current.commandName;
					if (commandName != null)
					{
						if (!(commandName == "UndoRedoPerformed"))
						{
							if (commandName == "Copy" || commandName == "Paste")
							{
								current.Use();
							}
						}
						else if ((GUIUtility.keyboardControl == id || ColorPicker.originalKeyboardControl == id) && ColorPicker.visible)
						{
							ColorPicker.color = value;
						}
					}
					goto IL_418;
				}
				if (typeForControl == EventType.ExecuteCommand)
				{
					if (GUIUtility.keyboardControl == id || ColorPicker.originalKeyboardControl == id)
					{
						string commandName2 = current.commandName;
						if (commandName2 != null)
						{
							if (!(commandName2 == "EyeDropperUpdate"))
							{
								if (commandName2 == "EyeDropperClicked")
								{
									GUI.changed = true;
									HandleUtility.Repaint();
									Color lastPickedColor = EyeDropper.GetLastPickedColor();
									lastPickedColor.a = value.a;
									EditorGUI.s_ColorPickID = 0;
									result = lastPickedColor;
									return result;
								}
								if (!(commandName2 == "EyeDropperCancelled"))
								{
									if (commandName2 == "ColorPickerChanged")
									{
										GUI.changed = true;
										HandleUtility.Repaint();
										result = ColorPicker.color;
										return result;
									}
									if (!(commandName2 == "Copy"))
									{
										if (commandName2 == "Paste")
										{
											Color color2;
											if (ColorClipboard.TryGetColor(hdr, out color2))
											{
												if (!showAlpha)
												{
													color2.a = color.a;
												}
												color = color2;
												GUI.changed = true;
												current.Use();
											}
										}
									}
									else
									{
										ColorClipboard.SetColor(value);
										current.Use();
									}
								}
								else
								{
									HandleUtility.Repaint();
									EditorGUI.s_ColorPickID = 0;
								}
							}
							else
							{
								HandleUtility.Repaint();
							}
						}
					}
					goto IL_418;
				}
				if (typeForControl != EventType.MouseDown)
				{
					goto IL_418;
				}
				if (showEyedropper)
				{
					position.width -= 20f;
				}
				if (position.Contains(current.mousePosition))
				{
					int button = current.button;
					if (button != 0)
					{
						if (button == 1)
						{
							GUIUtility.keyboardControl = id;
							string[] options2 = new string[]
							{
								"Copy",
								"Paste"
							};
							bool[] enabled = new bool[]
							{
								true,
								ColorClipboard.HasColor()
							};
							EditorUtility.DisplayCustomMenu(position, options2, enabled, null, delegate(object data, string[] options, int selected)
							{
								if (selected == 0)
								{
									Event e = EditorGUIUtility.CommandEvent("Copy");
									GUIView.current.SendEvent(e);
								}
								else if (selected == 1)
								{
									Event e2 = EditorGUIUtility.CommandEvent("Paste");
									GUIView.current.SendEvent(e2);
								}
							}, null);
							result = color;
							return result;
						}
					}
					else
					{
						GUIUtility.keyboardControl = id;
						EditorGUI.showMixedValue = false;
						ColorPicker.Show(GUIView.current, value, showAlpha, hdr);
						GUIUtility.ExitGUI();
					}
				}
				if (showEyedropper)
				{
					position.width += 20f;
					if (position.Contains(current.mousePosition))
					{
						GUIUtility.keyboardControl = id;
						EyeDropper.Start(GUIView.current, true);
						EditorGUI.s_ColorPickID = id;
						GUIUtility.ExitGUI();
					}
				}
				goto IL_418;
			case EventType.Repaint:
			{
				Rect position2;
				if (showEyedropper)
				{
					position2 = colorField.padding.Remove(position);
				}
				else
				{
					position2 = position;
				}
				if (showEyedropper && EditorGUI.s_ColorPickID == id)
				{
					Color pickedColor = EyeDropper.GetPickedColor();
					pickedColor.a = value.a;
					EditorGUIUtility.DrawColorSwatch(position2, pickedColor, showAlpha, hdr);
				}
				else
				{
					EditorGUIUtility.DrawColorSwatch(position2, value, showAlpha, hdr);
				}
				if (showEyedropper)
				{
					colorField.Draw(position, GUIContent.none, id);
				}
				else
				{
					EditorStyles.colorPickerBox.Draw(position, GUIContent.none, id);
				}
				goto IL_418;
			}
			}
			goto IL_46;
			IL_418:
			result = color;
			return result;
		}

		public static AnimationCurve CurveField(Rect position, AnimationCurve value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_CurveHash, FocusType.Keyboard, position);
			return EditorGUI.DoCurveField(EditorGUI.IndentedRect(position), controlID, value, EditorGUI.kCurveColor, default(Rect), null);
		}

		public static AnimationCurve CurveField(Rect position, string label, AnimationCurve value)
		{
			return EditorGUI.CurveField(position, EditorGUIUtility.TempContent(label), value);
		}

		public static AnimationCurve CurveField(Rect position, GUIContent label, AnimationCurve value)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_CurveHash, FocusType.Keyboard, position);
			return EditorGUI.DoCurveField(EditorGUI.PrefixLabel(position, controlID, label), controlID, value, EditorGUI.kCurveColor, default(Rect), null);
		}

		public static AnimationCurve CurveField(Rect position, AnimationCurve value, Color color, Rect ranges)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_CurveHash, FocusType.Keyboard, position);
			return EditorGUI.DoCurveField(EditorGUI.IndentedRect(position), controlID, value, color, ranges, null);
		}

		public static AnimationCurve CurveField(Rect position, string label, AnimationCurve value, Color color, Rect ranges)
		{
			return EditorGUI.CurveField(position, EditorGUIUtility.TempContent(label), value, color, ranges);
		}

		public static AnimationCurve CurveField(Rect position, GUIContent label, AnimationCurve value, Color color, Rect ranges)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_CurveHash, FocusType.Keyboard, position);
			return EditorGUI.DoCurveField(EditorGUI.PrefixLabel(position, controlID, label), controlID, value, color, ranges, null);
		}

		public static void CurveField(Rect position, SerializedProperty property, Color color, Rect ranges)
		{
			EditorGUI.CurveField(position, property, color, ranges, null);
		}

		public static void CurveField(Rect position, SerializedProperty property, Color color, Rect ranges, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			int controlID = GUIUtility.GetControlID(EditorGUI.s_CurveHash, FocusType.Keyboard, position);
			EditorGUI.DoCurveField(EditorGUI.PrefixLabel(position, controlID, label), controlID, null, color, ranges, property);
			EditorGUI.EndProperty();
		}

		private static void SetCurveEditorWindowCurve(AnimationCurve value, SerializedProperty property, Color color)
		{
			if (property != null)
			{
				CurveEditorWindow.curve = ((!property.hasMultipleDifferentValues) ? property.animationCurveValue : new AnimationCurve());
			}
			else
			{
				CurveEditorWindow.curve = value;
			}
			CurveEditorWindow.color = color;
		}

		internal static AnimationCurve DoCurveField(Rect position, int id, AnimationCurve value, Color color, Rect ranges, SerializedProperty property)
		{
			Event current = Event.current;
			position.width = Mathf.Max(position.width, 2f);
			position.height = Mathf.Max(position.height, 2f);
			if (GUIUtility.keyboardControl == id && Event.current.type != EventType.Layout)
			{
				if (EditorGUI.s_CurveID != id)
				{
					EditorGUI.s_CurveID = id;
					if (CurveEditorWindow.visible)
					{
						EditorGUI.SetCurveEditorWindowCurve(value, property, color);
						EditorGUI.ShowCurvePopup(GUIView.current, ranges);
					}
				}
				else if (CurveEditorWindow.visible && Event.current.type == EventType.Repaint)
				{
					EditorGUI.SetCurveEditorWindowCurve(value, property, color);
					CurveEditorWindow.instance.Repaint();
				}
			}
			EventType typeForControl = current.GetTypeForControl(id);
			AnimationCurve result;
			switch (typeForControl)
			{
			case EventType.KeyDown:
				if (current.MainActionKeyForControl(id))
				{
					EditorGUI.s_CurveID = id;
					EditorGUI.SetCurveEditorWindowCurve(value, property, color);
					EditorGUI.ShowCurvePopup(GUIView.current, ranges);
					current.Use();
					GUIUtility.ExitGUI();
				}
				goto IL_26C;
			case EventType.KeyUp:
			case EventType.ScrollWheel:
				IL_DE:
				if (typeForControl == EventType.MouseDown)
				{
					if (position.Contains(current.mousePosition))
					{
						EditorGUI.s_CurveID = id;
						GUIUtility.keyboardControl = id;
						EditorGUI.SetCurveEditorWindowCurve(value, property, color);
						EditorGUI.ShowCurvePopup(GUIView.current, ranges);
						current.Use();
						GUIUtility.ExitGUI();
					}
					goto IL_26C;
				}
				if (typeForControl != EventType.ExecuteCommand)
				{
					goto IL_26C;
				}
				if (EditorGUI.s_CurveID == id)
				{
					string commandName = current.commandName;
					if (commandName != null)
					{
						if (commandName == "CurveChanged")
						{
							GUI.changed = true;
							AnimationCurvePreviewCache.ClearCache();
							HandleUtility.Repaint();
							if (property != null)
							{
								property.animationCurveValue = CurveEditorWindow.curve;
								if (property.hasMultipleDifferentValues)
								{
									Debug.LogError("AnimationCurve SerializedProperty hasMultipleDifferentValues is true after writing.");
								}
							}
							result = CurveEditorWindow.curve;
							return result;
						}
					}
				}
				goto IL_26C;
			case EventType.Repaint:
			{
				Rect position2 = position;
				position2.y += 1f;
				position2.height -= 1f;
				if (ranges != default(Rect))
				{
					EditorGUIUtility.DrawCurveSwatch(position2, value, property, color, EditorGUI.kCurveBGColor, ranges);
				}
				else
				{
					EditorGUIUtility.DrawCurveSwatch(position2, value, property, color, EditorGUI.kCurveBGColor);
				}
				EditorStyles.colorPickerBox.Draw(position2, GUIContent.none, id, false);
				goto IL_26C;
			}
			}
			goto IL_DE;
			IL_26C:
			result = value;
			return result;
		}

		private static void ShowCurvePopup(GUIView viewToUpdate, Rect ranges)
		{
			CurveEditorSettings curveEditorSettings = new CurveEditorSettings();
			if (ranges.width > 0f && ranges.height > 0f && ranges.width != float.PositiveInfinity && ranges.height != float.PositiveInfinity)
			{
				curveEditorSettings.hRangeMin = ranges.xMin;
				curveEditorSettings.hRangeMax = ranges.xMax;
				curveEditorSettings.vRangeMin = ranges.yMin;
				curveEditorSettings.vRangeMax = ranges.yMax;
			}
			CurveEditorWindow.instance.Show(GUIView.current, curveEditorSettings);
		}

		private static bool ValidTargetForIconSelection(UnityEngine.Object[] targets)
		{
			return (targets[0] as MonoScript || targets[0] as GameObject) && targets.Length == 1;
		}

		internal static void ObjectIconDropDown(Rect position, UnityEngine.Object[] targets, bool showLabelIcons, Texture2D nullIcon, SerializedProperty iconProperty)
		{
			if (EditorGUI.s_IconTextureInactive == null)
			{
				EditorGUI.s_IconTextureInactive = (Material)EditorGUIUtility.LoadRequired("Inspectors/InactiveGUI.mat");
			}
			if (Event.current.type == EventType.Repaint)
			{
				Texture2D texture2D = null;
				if (!iconProperty.hasMultipleDifferentValues)
				{
					texture2D = AssetPreview.GetMiniThumbnail(targets[0]);
				}
				if (texture2D == null)
				{
					texture2D = nullIcon;
				}
				Vector2 vector = new Vector2(position.width, position.height);
				if (texture2D)
				{
					vector.x = Mathf.Min((float)texture2D.width, vector.x);
					vector.y = Mathf.Min((float)texture2D.height, vector.y);
				}
				Rect position2 = new Rect(position.x + position.width / 2f - vector.x / 2f, position.y + position.height / 2f - vector.y / 2f, vector.x, vector.y);
				GameObject gameObject = targets[0] as GameObject;
				bool flag = gameObject && !EditorUtility.IsPersistent(targets[0]) && (!gameObject.activeSelf || !gameObject.activeInHierarchy);
				if (flag)
				{
					float imageAspect = (float)texture2D.width / (float)texture2D.height;
					Rect screenRect = default(Rect);
					Rect sourceRect = default(Rect);
					GUI.CalculateScaledTextureRects(position2, ScaleMode.ScaleToFit, imageAspect, ref screenRect, ref sourceRect);
					Graphics.DrawTexture(screenRect, texture2D, sourceRect, 0, 0, 0, 0, new Color(0.5f, 0.5f, 0.5f, 1f), EditorGUI.s_IconTextureInactive);
				}
				else
				{
					GUI.DrawTexture(position2, texture2D, ScaleMode.ScaleToFit);
				}
				if (EditorGUI.ValidTargetForIconSelection(targets))
				{
					if (EditorGUI.s_IconDropDown == null)
					{
						EditorGUI.s_IconDropDown = EditorGUIUtility.IconContent("Icon Dropdown");
					}
					GUIStyle.none.Draw(new Rect(Mathf.Max(position.x + 2f, position2.x - 6f), position2.yMax - position2.height * 0.2f, 13f, 8f), EditorGUI.s_IconDropDown, false, false, false, false);
				}
			}
			if (EditorGUI.DropdownButton(position, GUIContent.none, FocusType.Passive, GUIStyle.none))
			{
				if (EditorGUI.ValidTargetForIconSelection(targets))
				{
					if (IconSelector.ShowAtPosition(targets[0], position, showLabelIcons))
					{
						GUIUtility.ExitGUI();
					}
				}
			}
		}

		public static void InspectorTitlebar(Rect position, UnityEngine.Object[] targetObjs)
		{
			GUIStyle none = GUIStyle.none;
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TitlebarHash, FocusType.Keyboard, position);
			EditorGUI.DoInspectorTitlebar(position, controlID, true, targetObjs, none);
		}

		public static bool InspectorTitlebar(Rect position, bool foldout, UnityEngine.Object targetObj, bool expandable)
		{
			return EditorGUI.InspectorTitlebar(position, foldout, new UnityEngine.Object[]
			{
				targetObj
			}, expandable);
		}

		public static bool InspectorTitlebar(Rect position, bool foldout, UnityEngine.Object[] targetObjs, bool expandable)
		{
			GUIStyle inspectorTitlebar = EditorStyles.inspectorTitlebar;
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TitlebarHash, FocusType.Keyboard, position);
			EditorGUI.DoInspectorTitlebar(position, controlID, foldout, targetObjs, inspectorTitlebar);
			foldout = EditorGUI.DoObjectMouseInteraction(foldout, position, targetObjs, controlID);
			if (expandable)
			{
				Rect inspectorTitleBarObjectFoldoutRenderRect = EditorGUI.GetInspectorTitleBarObjectFoldoutRenderRect(position);
				EditorGUI.DoObjectFoldoutInternal(foldout, position, inspectorTitleBarObjectFoldoutRenderRect, targetObjs, controlID);
			}
			return foldout;
		}

		internal static void DoInspectorTitlebar(Rect position, int id, bool foldout, UnityEngine.Object[] targetObjs, GUIStyle baseStyle)
		{
			GUIStyle inspectorTitlebarText = EditorStyles.inspectorTitlebarText;
			GUIStyle iconButton = EditorStyles.iconButton;
			Vector2 vector = iconButton.CalcSize(EditorGUI.GUIContents.titleSettingsIcon);
			Rect rect = new Rect(position.x + (float)baseStyle.padding.left, position.y + (float)baseStyle.padding.top, 16f, 16f);
			Rect rect2 = new Rect(position.xMax - (float)baseStyle.padding.right - 2f - 16f, rect.y, vector.x, vector.y);
			Rect position2 = new Rect(rect.xMax + 2f + 2f + 16f, rect.y, 100f, rect.height);
			position2.xMax = rect2.xMin - 2f;
			Event current = Event.current;
			int num = -1;
			for (int i = 0; i < targetObjs.Length; i++)
			{
				UnityEngine.Object target = targetObjs[i];
				int objectEnabled = EditorUtility.GetObjectEnabled(target);
				if (num == -1)
				{
					num = objectEnabled;
				}
				else if (num != objectEnabled)
				{
					num = -2;
				}
			}
			if (num != -1)
			{
				bool flag = num != 0;
				EditorGUI.showMixedValue = (num == -2);
				Rect position3 = rect;
				position3.x = rect.xMax + 2f;
				EditorGUI.BeginChangeCheck();
				Color backgroundColor = GUI.backgroundColor;
				bool flag2 = AnimationMode.IsPropertyAnimated(targetObjs[0], EditorGUI.kEnabledPropertyName);
				if (flag2)
				{
					Color backgroundColor2 = AnimationMode.animatedPropertyColor;
					if (AnimationMode.InAnimationRecording())
					{
						backgroundColor2 = AnimationMode.recordedPropertyColor;
					}
					else if (AnimationMode.IsPropertyCandidate(targetObjs[0], EditorGUI.kEnabledPropertyName))
					{
						backgroundColor2 = AnimationMode.candidatePropertyColor;
					}
					backgroundColor2.a *= GUI.color.a;
					GUI.backgroundColor = backgroundColor2;
				}
				int controlID = GUIUtility.GetControlID(EditorGUI.s_TitlebarHash, FocusType.Keyboard, position);
				flag = EditorGUIInternal.DoToggleForward(position3, controlID, flag, GUIContent.none, EditorStyles.toggle);
				if (flag2)
				{
					GUI.backgroundColor = backgroundColor;
				}
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObjects(targetObjs, ((!flag) ? "Disable" : "Enable") + " Component" + ((targetObjs.Length <= 1) ? "" : "s"));
					for (int j = 0; j < targetObjs.Length; j++)
					{
						UnityEngine.Object target2 = targetObjs[j];
						EditorUtility.SetObjectEnabled(target2, flag);
					}
				}
				EditorGUI.showMixedValue = false;
				if (position3.Contains(Event.current.mousePosition))
				{
					if ((current.type == EventType.MouseDown && current.button == 1) || current.type == EventType.ContextClick)
					{
						SerializedObject serializedObject = new SerializedObject(targetObjs[0]);
						EditorGUI.DoPropertyContextMenu(serializedObject.FindProperty(EditorGUI.kEnabledPropertyName));
						current.Use();
					}
				}
			}
			Rect rectangle = rect2;
			rectangle.x -= 18f;
			rectangle = EditorGUIUtility.DrawEditorHeaderItems(rectangle, targetObjs);
			position2.xMax = rectangle.xMin - 2f;
			if (current.type == EventType.Repaint)
			{
				Texture2D miniThumbnail = AssetPreview.GetMiniThumbnail(targetObjs[0]);
				GUIStyle.none.Draw(rect, EditorGUIUtility.TempContent(miniThumbnail), false, false, false, false);
			}
			bool enabled = GUI.enabled;
			GUI.enabled = true;
			EventType type = current.type;
			if (type != EventType.MouseDown)
			{
				if (type == EventType.Repaint)
				{
					baseStyle.Draw(position, GUIContent.none, id, foldout);
					position = baseStyle.padding.Remove(position);
					inspectorTitlebarText.Draw(position2, EditorGUIUtility.TempContent(ObjectNames.GetInspectorTitle(targetObjs[0])), id, foldout);
					iconButton.Draw(rect2, EditorGUI.GUIContents.titleSettingsIcon, id, foldout);
				}
			}
			else if (rect2.Contains(current.mousePosition))
			{
				EditorUtility.DisplayObjectContextMenu(rect2, targetObjs, 0);
				current.Use();
			}
			GUI.enabled = enabled;
		}

		internal static bool ToggleTitlebar(Rect position, GUIContent label, bool foldout, ref bool toggleValue)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TitlebarHash, FocusType.Keyboard, position);
			GUIStyle inspectorTitlebar = EditorStyles.inspectorTitlebar;
			GUIStyle inspectorTitlebarText = EditorStyles.inspectorTitlebarText;
			GUIStyle foldout2 = EditorStyles.foldout;
			Rect position2 = new Rect(position.x + (float)inspectorTitlebar.padding.left, position.y + (float)inspectorTitlebar.padding.top, 16f, 16f);
			Rect position3 = new Rect(position2.xMax + 2f, position2.y, 200f, 16f);
			int controlID2 = GUIUtility.GetControlID(EditorGUI.s_TitlebarHash, FocusType.Keyboard, position);
			toggleValue = EditorGUIInternal.DoToggleForward(position2, controlID2, toggleValue, GUIContent.none, EditorStyles.toggle);
			if (Event.current.type == EventType.Repaint)
			{
				inspectorTitlebar.Draw(position, GUIContent.none, controlID, foldout);
				foldout2.Draw(EditorGUI.GetInspectorTitleBarObjectFoldoutRenderRect(position), GUIContent.none, controlID, foldout);
				position = inspectorTitlebar.padding.Remove(position);
				inspectorTitlebarText.Draw(position3, label, controlID, foldout);
			}
			return EditorGUIInternal.DoToggleForward(EditorGUI.IndentedRect(position), controlID, foldout, GUIContent.none, GUIStyle.none);
		}

		internal static bool FoldoutTitlebar(Rect position, GUIContent label, bool foldout, bool skipIconSpacing)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TitlebarHash, FocusType.Keyboard, position);
			if (Event.current.type == EventType.Repaint)
			{
				GUIStyle inspectorTitlebar = EditorStyles.inspectorTitlebar;
				GUIStyle inspectorTitlebarText = EditorStyles.inspectorTitlebarText;
				GUIStyle foldout2 = EditorStyles.foldout;
				Rect position2 = new Rect(position.x + (float)inspectorTitlebar.padding.left + 2f + (float)((!skipIconSpacing) ? 16 : 0), position.y + (float)inspectorTitlebar.padding.top, 200f, 16f);
				inspectorTitlebar.Draw(position, GUIContent.none, controlID, foldout);
				foldout2.Draw(EditorGUI.GetInspectorTitleBarObjectFoldoutRenderRect(position), GUIContent.none, controlID, foldout);
				position = inspectorTitlebar.padding.Remove(position);
				inspectorTitlebarText.Draw(position2, EditorGUIUtility.TempContent(label.text), controlID, foldout);
			}
			return EditorGUIInternal.DoToggleForward(EditorGUI.IndentedRect(position), controlID, foldout, GUIContent.none, GUIStyle.none);
		}

		[EditorHeaderItem(typeof(UnityEngine.Object), -1000)]
		internal static bool HelpIconButton(Rect position, UnityEngine.Object[] objs)
		{
			UnityEngine.Object @object = objs[0];
			bool flag = Unsupported.IsSourceBuild();
			bool flag2 = !flag;
			if (!flag2)
			{
				EditorCompilation.TargetAssemblyInfo[] targetAssemblies = EditorCompilationInterface.GetTargetAssemblies();
				string a = @object.GetType().Assembly.ToString();
				for (int i = 0; i < targetAssemblies.Length; i++)
				{
					if (a == targetAssemblies[i].Name)
					{
						flag2 = true;
						break;
					}
				}
			}
			bool flag3 = Help.HasHelpForObject(@object, flag2);
			bool result;
			if (flag3 || flag)
			{
				Color color = GUI.color;
				GUIContent gUIContent = new GUIContent(EditorGUI.GUIContents.helpIcon);
				string niceHelpNameForObject = Help.GetNiceHelpNameForObject(@object, flag2);
				if (flag && !flag3)
				{
					GUI.color = Color.yellow;
					bool flag4 = @object is MonoBehaviour;
					string arg = ((!flag4) ? "sealed partial class-" : "script-") + niceHelpNameForObject;
					gUIContent.tooltip = string.Format("Could not find Reference page for {0} ({1}).\nDocs for this object is missing or all docs are missing.\nThis warning only shows up in development builds.", niceHelpNameForObject, arg);
				}
				else
				{
					gUIContent.tooltip = string.Format("Open Reference for {0}.", niceHelpNameForObject);
				}
				GUIStyle iconButton = EditorStyles.iconButton;
				if (GUI.Button(position, gUIContent, iconButton))
				{
					Help.ShowHelpForObject(@object);
				}
				GUI.color = color;
				result = true;
			}
			else
			{
				result = false;
			}
			return result;
		}

		internal static bool FoldoutInternal(Rect position, bool foldout, GUIContent content, bool toggleOnLabelClick, GUIStyle style)
		{
			Rect rect = position;
			if (EditorGUIUtility.hierarchyMode)
			{
				int num = EditorStyles.foldout.padding.left - EditorStyles.label.padding.left;
				position.xMin -= (float)num;
			}
			int controlID = GUIUtility.GetControlID(EditorGUI.s_FoldoutHash, FocusType.Keyboard, position);
			EventType eventType = Event.current.type;
			if (!GUI.enabled && GUIClip.enabled && (Event.current.rawType == EventType.MouseDown || Event.current.rawType == EventType.MouseDrag || Event.current.rawType == EventType.MouseUp))
			{
				eventType = Event.current.rawType;
			}
			bool result;
			switch (eventType)
			{
			case EventType.MouseDown:
				if (position.Contains(Event.current.mousePosition) && Event.current.button == 0)
				{
					int num2 = controlID;
					GUIUtility.hotControl = num2;
					GUIUtility.keyboardControl = num2;
					Event.current.Use();
				}
				goto IL_386;
			case EventType.MouseUp:
				if (GUIUtility.hotControl == controlID)
				{
					GUIUtility.hotControl = 0;
					Event.current.Use();
					Rect rect2 = position;
					if (!toggleOnLabelClick)
					{
						rect2.width = (float)style.padding.left;
						rect2.x += EditorGUI.indent;
					}
					if (rect2.Contains(Event.current.mousePosition))
					{
						GUI.changed = true;
						result = !foldout;
						return result;
					}
				}
				goto IL_386;
			case EventType.MouseMove:
			case EventType.KeyUp:
			case EventType.ScrollWheel:
			case EventType.Layout:
				IL_D5:
				if (eventType != EventType.DragExited)
				{
					goto IL_386;
				}
				if (EditorGUI.s_DragUpdatedOverID == controlID)
				{
					EditorGUI.s_DragUpdatedOverID = 0;
					Event.current.Use();
				}
				goto IL_386;
			case EventType.MouseDrag:
				if (GUIUtility.hotControl == controlID)
				{
					Event.current.Use();
				}
				goto IL_386;
			case EventType.KeyDown:
				if (GUIUtility.keyboardControl == controlID)
				{
					KeyCode keyCode = Event.current.keyCode;
					if ((keyCode == KeyCode.LeftArrow && foldout) || (keyCode == KeyCode.RightArrow && !foldout))
					{
						foldout = !foldout;
						GUI.changed = true;
						Event.current.Use();
					}
				}
				goto IL_386;
			case EventType.Repaint:
			{
				EditorStyles.foldoutSelected.Draw(position, GUIContent.none, controlID, EditorGUI.s_DragUpdatedOverID == controlID);
				Rect position2 = new Rect(position.x + EditorGUI.indent, position.y, EditorGUIUtility.labelWidth - EditorGUI.indent, position.height);
				if (EditorGUI.showMixedValue && !foldout)
				{
					style.Draw(position2, content, controlID, foldout);
					EditorGUI.BeginHandleMixedValueContentColor();
					Rect position3 = rect;
					position3.xMin += EditorGUIUtility.labelWidth;
					EditorStyles.label.Draw(position3, EditorGUI.s_MixedValueContent, controlID, false);
					EditorGUI.EndHandleMixedValueContentColor();
				}
				else
				{
					style.Draw(position2, content, controlID, foldout);
				}
				goto IL_386;
			}
			case EventType.DragUpdated:
				if (EditorGUI.s_DragUpdatedOverID == controlID)
				{
					if (position.Contains(Event.current.mousePosition))
					{
						if ((double)Time.realtimeSinceStartup > EditorGUI.s_FoldoutDestTime)
						{
							foldout = true;
							Event.current.Use();
						}
					}
					else
					{
						EditorGUI.s_DragUpdatedOverID = 0;
					}
				}
				else if (position.Contains(Event.current.mousePosition))
				{
					EditorGUI.s_DragUpdatedOverID = controlID;
					EditorGUI.s_FoldoutDestTime = (double)Time.realtimeSinceStartup + 0.7;
					Event.current.Use();
				}
				goto IL_386;
			}
			goto IL_D5;
			IL_386:
			result = foldout;
			return result;
		}

		public static void ProgressBar(Rect position, float value, string text)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_ProgressBarHash, FocusType.Keyboard, position);
			Event current = Event.current;
			EventType typeForControl = current.GetTypeForControl(controlID);
			if (typeForControl == EventType.Repaint)
			{
				EditorStyles.progressBarBack.Draw(position, false, false, false, false);
				Rect position2 = new Rect(position);
				value = Mathf.Clamp01(value);
				position2.width *= value;
				EditorStyles.progressBarBar.Draw(position2, false, false, false, false);
				EditorStyles.progressBarText.Draw(position, text, false, false, false, false);
			}
		}

		public static void HelpBox(Rect position, string message, MessageType type)
		{
			GUI.Label(position, EditorGUIUtility.TempContent(message, EditorGUIUtility.GetHelpIcon(type)), EditorStyles.helpBox);
		}

		internal static bool LabelHasContent(GUIContent label)
		{
			return label == null || label.text != string.Empty || label.image != null;
		}

		private static void DrawTextDebugHelpers(Rect labelPosition)
		{
			Color color = GUI.color;
			GUI.color = Color.white;
			GUI.DrawTexture(new Rect(labelPosition.x, labelPosition.y, labelPosition.width, 4f), EditorGUIUtility.whiteTexture);
			GUI.color = Color.cyan;
			GUI.DrawTexture(new Rect(labelPosition.x, labelPosition.yMax - 4f, labelPosition.width, 4f), EditorGUIUtility.whiteTexture);
			GUI.color = color;
		}

		internal static void PrepareCurrentPrefixLabel(int controlId)
		{
			if (EditorGUI.s_HasPrefixLabel)
			{
				if (!string.IsNullOrEmpty(EditorGUI.s_PrefixLabel.text))
				{
					Color color = GUI.color;
					GUI.color = EditorGUI.s_PrefixGUIColor;
					EditorGUI.HandlePrefixLabel(EditorGUI.s_PrefixTotalRect, EditorGUI.s_PrefixRect, EditorGUI.s_PrefixLabel, controlId, EditorGUI.s_PrefixStyle);
					GUI.color = color;
				}
				EditorGUI.s_HasPrefixLabel = false;
			}
		}

		internal static void HandlePrefixLabelInternal(Rect totalPosition, Rect labelPosition, GUIContent label, int id, GUIStyle style)
		{
			if (id == 0 && label != null)
			{
				EditorGUI.s_PrefixLabel.text = label.text;
				EditorGUI.s_PrefixLabel.image = label.image;
				EditorGUI.s_PrefixLabel.tooltip = label.tooltip;
				EditorGUI.s_PrefixTotalRect = totalPosition;
				EditorGUI.s_PrefixRect = labelPosition;
				EditorGUI.s_PrefixStyle = style;
				EditorGUI.s_PrefixGUIColor = GUI.color;
				EditorGUI.s_HasPrefixLabel = true;
			}
			else
			{
				if (Highlighter.searchMode == HighlightSearchMode.PrefixLabel || Highlighter.searchMode == HighlightSearchMode.Auto)
				{
					Highlighter.Handle(totalPosition, label.text);
				}
				EventType type = Event.current.type;
				if (type != EventType.Repaint)
				{
					if (type == EventType.MouseDown)
					{
						if (Event.current.button == 0 && labelPosition.Contains(Event.current.mousePosition))
						{
							if (EditorGUIUtility.CanHaveKeyboardFocus(id))
							{
								GUIUtility.keyboardControl = id;
							}
							EditorGUIUtility.editingTextField = false;
							HandleUtility.Repaint();
						}
					}
				}
				else
				{
					labelPosition.width += 1f;
					style.DrawPrefixLabel(labelPosition, label, id);
				}
			}
		}

		public static Rect PrefixLabel(Rect totalPosition, GUIContent label)
		{
			return EditorGUI.PrefixLabel(totalPosition, 0, label, EditorStyles.label);
		}

		public static Rect PrefixLabel(Rect totalPosition, GUIContent label, GUIStyle style)
		{
			return EditorGUI.PrefixLabel(totalPosition, 0, label, style);
		}

		public static Rect PrefixLabel(Rect totalPosition, int id, GUIContent label)
		{
			return EditorGUI.PrefixLabel(totalPosition, id, label, EditorStyles.label);
		}

		public static Rect PrefixLabel(Rect totalPosition, int id, GUIContent label, GUIStyle style)
		{
			Rect result;
			if (!EditorGUI.LabelHasContent(label))
			{
				result = EditorGUI.IndentedRect(totalPosition);
			}
			else
			{
				Rect labelPosition = new Rect(totalPosition.x + EditorGUI.indent, totalPosition.y, EditorGUIUtility.labelWidth - EditorGUI.indent, 16f);
				Rect rect = new Rect(totalPosition.x + EditorGUIUtility.labelWidth, totalPosition.y, totalPosition.width - EditorGUIUtility.labelWidth, totalPosition.height);
				EditorGUI.HandlePrefixLabel(totalPosition, labelPosition, label, id, style);
				result = rect;
			}
			return result;
		}

		internal static Rect MultiFieldPrefixLabel(Rect totalPosition, int id, GUIContent label, int columns)
		{
			Rect result;
			if (!EditorGUI.LabelHasContent(label))
			{
				result = EditorGUI.IndentedRect(totalPosition);
			}
			else if (EditorGUIUtility.wideMode)
			{
				Rect labelPosition = new Rect(totalPosition.x + EditorGUI.indent, totalPosition.y, EditorGUIUtility.labelWidth - EditorGUI.indent, 16f);
				Rect rect = totalPosition;
				rect.xMin += EditorGUIUtility.labelWidth;
				if (columns > 1)
				{
					labelPosition.width -= 1f;
					rect.xMin -= 1f;
				}
				if (columns == 2)
				{
					float num = (rect.width - 4f) / 3f;
					rect.xMax -= num + 2f;
				}
				EditorGUI.HandlePrefixLabel(totalPosition, labelPosition, label, id);
				result = rect;
			}
			else
			{
				Rect labelPosition2 = new Rect(totalPosition.x + EditorGUI.indent, totalPosition.y, totalPosition.width - EditorGUI.indent, 16f);
				Rect rect2 = totalPosition;
				rect2.xMin += EditorGUI.indent + 15f;
				rect2.yMin += 16f;
				EditorGUI.HandlePrefixLabel(totalPosition, labelPosition2, label, id);
				result = rect2;
			}
			return result;
		}

		public static GUIContent BeginProperty(Rect totalPosition, GUIContent label, SerializedProperty property)
		{
			return EditorGUI.BeginPropertyInternal(totalPosition, label, property);
		}

		internal static GUIContent BeginPropertyInternal(Rect totalPosition, GUIContent label, SerializedProperty property)
		{
			Highlighter.HighlightIdentifier(totalPosition, property.propertyPath);
			if (EditorGUI.s_PendingPropertyKeyboardHandling != null)
			{
				EditorGUI.DoPropertyFieldKeyboardHandling(EditorGUI.s_PendingPropertyKeyboardHandling);
			}
			EditorGUI.s_PendingPropertyKeyboardHandling = property;
			if (property == null)
			{
				string message = ((label != null) ? (label.text + ": ") : "") + "SerializedProperty is null";
				EditorGUI.HelpBox(totalPosition, "null", MessageType.Error);
				throw new NullReferenceException(message);
			}
			EditorGUI.s_PropertyFieldTempContent.text = ((label != null) ? label.text : property.localizedDisplayName);
			EditorGUI.s_PropertyFieldTempContent.tooltip = ((!EditorGUI.isCollectingTooltips) ? null : ((label != null) ? label.tooltip : property.tooltip));
			string tooltip = ScriptAttributeUtility.GetHandler(property).tooltip;
			if (tooltip != null)
			{
				EditorGUI.s_PropertyFieldTempContent.tooltip = tooltip;
			}
			EditorGUI.s_PropertyFieldTempContent.image = ((label != null) ? label.image : null);
			if (Event.current.alt && property.serializedObject.inspectorMode != InspectorMode.Normal)
			{
				GUIContent arg_131_0 = EditorGUI.s_PropertyFieldTempContent;
				string propertyPath = property.propertyPath;
				EditorGUI.s_PropertyFieldTempContent.text = propertyPath;
				arg_131_0.tooltip = propertyPath;
			}
			bool boldDefaultFont = EditorGUIUtility.GetBoldDefaultFont();
			if (property.serializedObject.targetObjects.Length == 1 && property.isInstantiatedPrefab)
			{
				EditorGUIUtility.SetBoldDefaultFont(property.prefabOverride);
			}
			EditorGUI.s_PropertyStack.Push(new PropertyGUIData(property, totalPosition, boldDefaultFont, GUI.enabled, GUI.backgroundColor));
			if (GUIDebugger.active)
			{
				string targetTypeAssemblyQualifiedName = (!(property.serializedObject.targetObject != null)) ? null : property.serializedObject.targetObject.GetType().AssemblyQualifiedName;
				GUIDebugger.LogBeginProperty(targetTypeAssemblyQualifiedName, property.propertyPath, totalPosition);
			}
			EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
			if (property.isAnimated)
			{
				Color backgroundColor = AnimationMode.animatedPropertyColor;
				if (AnimationMode.InAnimationRecording())
				{
					backgroundColor = AnimationMode.recordedPropertyColor;
				}
				else if (property.isCandidate)
				{
					backgroundColor = AnimationMode.candidatePropertyColor;
				}
				backgroundColor.a *= GUI.backgroundColor.a;
				GUI.backgroundColor = backgroundColor;
			}
			GUI.enabled &= property.editable;
			return EditorGUI.s_PropertyFieldTempContent;
		}

		public static void EndProperty()
		{
			if (GUIDebugger.active)
			{
				GUIDebugger.LogEndProperty();
			}
			EditorGUI.showMixedValue = false;
			PropertyGUIData propertyGUIData = EditorGUI.s_PropertyStack.Pop();
			if (Event.current.type == EventType.ContextClick && propertyGUIData.totalPosition.Contains(Event.current.mousePosition))
			{
				EditorGUI.DoPropertyContextMenu(propertyGUIData.property);
			}
			EditorGUIUtility.SetBoldDefaultFont(propertyGUIData.wasBoldDefaultFont);
			GUI.enabled = propertyGUIData.wasEnabled;
			GUI.backgroundColor = propertyGUIData.color;
			if (EditorGUI.s_PendingPropertyKeyboardHandling != null)
			{
				EditorGUI.DoPropertyFieldKeyboardHandling(EditorGUI.s_PendingPropertyKeyboardHandling);
			}
			if (EditorGUI.s_PendingPropertyDelete != null && EditorGUI.s_PropertyStack.Count == 0)
			{
				if (EditorGUI.s_PendingPropertyDelete.propertyPath == propertyGUIData.property.propertyPath)
				{
					propertyGUIData.property.DeleteCommand();
				}
				else
				{
					EditorGUI.s_PendingPropertyDelete.DeleteCommand();
				}
				EditorGUI.s_PendingPropertyDelete = null;
			}
		}

		private static void DoPropertyFieldKeyboardHandling(SerializedProperty property)
		{
			if (Event.current.type == EventType.ExecuteCommand || Event.current.type == EventType.ValidateCommand)
			{
				if (GUIUtility.keyboardControl == EditorGUIUtility.s_LastControlID && (Event.current.commandName == "Delete" || Event.current.commandName == "SoftDelete"))
				{
					if (Event.current.type == EventType.ExecuteCommand)
					{
						EditorGUI.s_PendingPropertyDelete = property.Copy();
					}
					Event.current.Use();
				}
				if (GUIUtility.keyboardControl == EditorGUIUtility.s_LastControlID && Event.current.commandName == "Duplicate")
				{
					if (Event.current.type == EventType.ExecuteCommand)
					{
						property.DuplicateCommand();
					}
					Event.current.Use();
				}
			}
			EditorGUI.s_PendingPropertyKeyboardHandling = null;
		}

		internal static void LayerMaskField(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.LayerMaskField(position, property, label, EditorStyles.layerMaskField);
		}

		internal static void LayerMaskField(Rect position, SerializedProperty property, GUIContent label, GUIStyle style)
		{
			uint arg_28_1 = property.layerMaskBits;
			if (EditorGUI.<>f__mg$cache2 == null)
			{
				EditorGUI.<>f__mg$cache2 = new EditorUtility.SelectMenuItemFunction(EditorGUI.SetLayerMaskValueDelegate);
			}
			EditorGUI.LayerMaskField(position, arg_28_1, property, label, style, EditorGUI.<>f__mg$cache2);
		}

		internal static void LayerMaskField(Rect position, uint layers, GUIContent label, EditorUtility.SelectMenuItemFunction callback)
		{
			EditorGUI.LayerMaskField(position, layers, null, label, EditorStyles.layerMaskField, callback);
		}

		internal static void LayerMaskField(Rect position, uint layers, SerializedProperty property, GUIContent label, GUIStyle style, EditorUtility.SelectMenuItemFunction callback)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_LayerMaskField, FocusType.Keyboard, position);
			if (label != null)
			{
				position = EditorGUI.PrefixLabel(position, controlID, label);
			}
			Event current = Event.current;
			if (current.type == EventType.Repaint)
			{
				if (EditorGUI.showMixedValue)
				{
					EditorGUI.BeginHandleMixedValueContentColor();
					style.Draw(position, EditorGUI.s_MixedValueContent, controlID, false);
					EditorGUI.EndHandleMixedValueContentColor();
				}
				else
				{
					style.Draw(position, EditorGUIUtility.TempContent(SerializedProperty.GetLayerMaskStringValue(layers)), controlID, false);
				}
			}
			else if ((current.type == EventType.MouseDown && position.Contains(current.mousePosition)) || current.MainActionKeyForControl(controlID))
			{
				SerializedProperty item = (property == null) ? null : property.serializedObject.FindProperty(property.propertyPath);
				Tuple<SerializedProperty, uint> userData = new Tuple<SerializedProperty, uint>(item, layers);
				EditorUtility.DisplayCustomMenu(position, SerializedProperty.GetLayerMaskNames(layers), (property == null || !property.hasMultipleDifferentValues) ? SerializedProperty.GetLayerMaskSelectedIndex(layers) : new int[0], callback, userData);
				Event.current.Use();
				GUIUtility.keyboardControl = controlID;
			}
		}

		internal static void SetLayerMaskValueDelegate(object userData, string[] options, int selected)
		{
			Tuple<SerializedProperty, uint> tuple = (Tuple<SerializedProperty, uint>)userData;
			if (tuple.Item1 != null)
			{
				tuple.Item1.ToggleLayerMaskAtIndex(selected);
				tuple.Item1.serializedObject.ApplyModifiedProperties();
				tuple.Item2 = tuple.Item1.layerMaskBits;
			}
		}

		internal static void ShowRepaints()
		{
			if (Unsupported.IsDeveloperMode())
			{
				Color backgroundColor = GUI.backgroundColor;
				GUI.backgroundColor = new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value, 1f);
				Texture2D background = EditorStyles.radioButton.normal.background;
				Vector2 position = new Vector2((float)background.width, (float)background.height);
				GUI.Label(new Rect(Vector2.zero, EditorGUIUtility.PixelsToPoints(position)), string.Empty, EditorStyles.radioButton);
				GUI.backgroundColor = backgroundColor;
			}
		}

		internal static void DrawTextureAlphaInternal(Rect position, Texture image, ScaleMode scaleMode, float imageAspect, float mipLevel)
		{
			EditorGUI.DrawPreviewTextureInternal(position, image, EditorGUI.alphaMaterial, scaleMode, imageAspect, mipLevel);
		}

		internal static void DrawTextureTransparentInternal(Rect position, Texture image, ScaleMode scaleMode, float imageAspect, float mipLevel)
		{
			if (imageAspect == 0f && image == null)
			{
				Debug.LogError("Please specify an image or a imageAspect");
			}
			else
			{
				if (imageAspect == 0f)
				{
					imageAspect = (float)image.width / (float)image.height;
				}
				EditorGUI.DrawTransparencyCheckerTexture(position, scaleMode, imageAspect);
				if (image != null)
				{
					EditorGUI.DrawPreviewTexture(position, image, EditorGUI.transparentMaterial, scaleMode, imageAspect, mipLevel);
				}
			}
		}

		internal static void DrawTransparencyCheckerTexture(Rect position, ScaleMode scaleMode, float imageAspect)
		{
			Rect position2 = default(Rect);
			Rect rect = default(Rect);
			GUI.CalculateScaledTextureRects(position, scaleMode, imageAspect, ref position2, ref rect);
			GUI.DrawTextureWithTexCoords(position2, EditorGUI.transparentCheckerTexture, new Rect(position2.width * -0.5f / (float)EditorGUI.transparentCheckerTexture.width, position2.height * -0.5f / (float)EditorGUI.transparentCheckerTexture.height, position2.width / (float)EditorGUI.transparentCheckerTexture.width, position2.height / (float)EditorGUI.transparentCheckerTexture.height), false);
		}

		internal static void DrawPreviewTextureInternal(Rect position, Texture image, Material mat, ScaleMode scaleMode, float imageAspect, float mipLevel)
		{
			if (Event.current.type == EventType.Repaint)
			{
				if (imageAspect == 0f)
				{
					imageAspect = (float)image.width / (float)image.height;
				}
				if (mat == null)
				{
					mat = EditorGUI.GetMaterialForSpecialTexture(image, EditorGUI.colorMaterial);
				}
				mat.SetFloat("_Mip", mipLevel);
				RenderTexture renderTexture = image as RenderTexture;
				bool flag = renderTexture != null && renderTexture.bindTextureMS;
				if (flag)
				{
					RenderTextureDescriptor descriptor = renderTexture.descriptor;
					descriptor.bindMS = false;
					descriptor.msaaSamples = 1;
					RenderTexture temporary = RenderTexture.GetTemporary(descriptor);
					temporary.Create();
					renderTexture.ResolveAntiAliasedSurface(temporary);
					image = temporary;
				}
				Rect screenRect = default(Rect);
				Rect sourceRect = default(Rect);
				GUI.CalculateScaledTextureRects(position, scaleMode, imageAspect, ref screenRect, ref sourceRect);
				Texture2D texture2D = image as Texture2D;
				if (texture2D != null && TextureUtil.GetUsageMode(image) == TextureUsageMode.AlwaysPadded)
				{
					sourceRect.width *= (float)texture2D.width / (float)TextureUtil.GetGPUWidth(texture2D);
					sourceRect.height *= (float)texture2D.height / (float)TextureUtil.GetGPUHeight(texture2D);
				}
				Graphics.DrawTexture(screenRect, image, sourceRect, 0, 0, 0, 0, GUI.color, mat);
				if (flag)
				{
					RenderTexture.ReleaseTemporary(image as RenderTexture);
				}
			}
		}

		internal static Material GetMaterialForSpecialTexture(Texture t, Material defaultMat = null)
		{
			Material result;
			if (t == null)
			{
				result = null;
			}
			else
			{
				TextureUsageMode usageMode = TextureUtil.GetUsageMode(t);
				TextureFormat textureFormat = TextureUtil.GetTextureFormat(t);
				if (usageMode == TextureUsageMode.RealtimeLightmapRGBM || usageMode == TextureUsageMode.BakedLightmapRGBM || usageMode == TextureUsageMode.RGBMEncoded)
				{
					result = EditorGUI.lightmapRGBMMaterial;
				}
				else if (usageMode == TextureUsageMode.BakedLightmapDoubleLDR)
				{
					result = EditorGUI.lightmapDoubleLDRMaterial;
				}
				else if (usageMode == TextureUsageMode.BakedLightmapFullHDR)
				{
					result = EditorGUI.lightmapFullHDRMaterial;
				}
				else if (usageMode == TextureUsageMode.NormalmapDXT5nm || (usageMode == TextureUsageMode.NormalmapPlain && textureFormat == TextureFormat.BC5))
				{
					result = EditorGUI.normalmapMaterial;
				}
				else if (TextureUtil.IsAlphaOnlyTextureFormat(textureFormat))
				{
					result = EditorGUI.alphaMaterial;
				}
				else
				{
					result = defaultMat;
				}
			}
			return result;
		}

		private static Material GetPreviewMaterial(ref Material m, string shaderPath)
		{
			if (m == null)
			{
				m = new Material(EditorGUIUtility.LoadRequired(shaderPath) as Shader);
				m.hideFlags = HideFlags.HideAndDontSave;
			}
			return m;
		}

		private static void SetExpandedRecurse(SerializedProperty property, bool expanded)
		{
			SerializedProperty serializedProperty = property.Copy();
			serializedProperty.isExpanded = expanded;
			int depth = serializedProperty.depth;
			while (serializedProperty.NextVisible(true) && serializedProperty.depth > depth)
			{
				if (serializedProperty.hasVisibleChildren)
				{
					serializedProperty.isExpanded = expanded;
				}
			}
		}

		internal static float GetSinglePropertyHeight(SerializedProperty property, GUIContent label)
		{
			float result;
			if (property == null)
			{
				result = 16f;
			}
			else
			{
				result = EditorGUI.GetPropertyHeight(property.propertyType, label);
			}
			return result;
		}

		public static float GetPropertyHeight(SerializedPropertyType type, GUIContent label)
		{
			float result;
			if (type == SerializedPropertyType.Vector3 || type == SerializedPropertyType.Vector2 || type == SerializedPropertyType.Vector4 || type == SerializedPropertyType.Vector3Int || type == SerializedPropertyType.Vector2Int)
			{
				result = ((EditorGUI.LabelHasContent(label) && !EditorGUIUtility.wideMode) ? 16f : 0f) + 16f;
			}
			else if (type == SerializedPropertyType.Rect || type == SerializedPropertyType.RectInt)
			{
				result = ((EditorGUI.LabelHasContent(label) && !EditorGUIUtility.wideMode) ? 16f : 0f) + 32f;
			}
			else if (type == SerializedPropertyType.Bounds || type == SerializedPropertyType.BoundsInt)
			{
				result = (EditorGUI.LabelHasContent(label) ? 16f : 0f) + 32f;
			}
			else
			{
				result = 16f;
			}
			return result;
		}

		internal static float GetPropertyHeightInternal(SerializedProperty property, GUIContent label, bool includeChildren)
		{
			return ScriptAttributeUtility.GetHandler(property).GetHeight(property, label, includeChildren);
		}

		public static bool CanCacheInspectorGUI(SerializedProperty property)
		{
			return ScriptAttributeUtility.GetHandler(property).CanCacheInspectorGUI(property);
		}

		internal static bool HasVisibleChildFields(SerializedProperty property)
		{
			SerializedPropertyType propertyType = property.propertyType;
			bool result;
			switch (propertyType)
			{
			case SerializedPropertyType.Vector2:
			case SerializedPropertyType.Vector3:
			case SerializedPropertyType.Rect:
			case SerializedPropertyType.Bounds:
				goto IL_4E;
			case SerializedPropertyType.Vector4:
			case SerializedPropertyType.ArraySize:
			case SerializedPropertyType.Character:
			case SerializedPropertyType.AnimationCurve:
				IL_30:
				switch (propertyType)
				{
				case SerializedPropertyType.Vector2Int:
				case SerializedPropertyType.Vector3Int:
				case SerializedPropertyType.RectInt:
				case SerializedPropertyType.BoundsInt:
					goto IL_4E;
				default:
					result = property.hasVisibleChildren;
					return result;
				}
				break;
			}
			goto IL_30;
			IL_4E:
			result = false;
			return result;
		}

		internal static bool PropertyFieldInternal(Rect position, SerializedProperty property, GUIContent label, bool includeChildren)
		{
			return ScriptAttributeUtility.GetHandler(property).OnGUI(position, property, label, includeChildren);
		}

		internal static bool DefaultPropertyField(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginPropertyInternal(position, label, property);
			SerializedPropertyType propertyType = property.propertyType;
			bool flag = false;
			if (!EditorGUI.HasVisibleChildFields(property))
			{
				switch (propertyType)
				{
				case SerializedPropertyType.Integer:
				{
					EditorGUI.BeginChangeCheck();
					long longValue = EditorGUI.LongField(position, label, property.longValue);
					if (EditorGUI.EndChangeCheck())
					{
						property.longValue = longValue;
					}
					goto IL_382;
				}
				case SerializedPropertyType.Boolean:
				{
					EditorGUI.BeginChangeCheck();
					bool boolValue = EditorGUI.Toggle(position, label, property.boolValue);
					if (EditorGUI.EndChangeCheck())
					{
						property.boolValue = boolValue;
					}
					goto IL_382;
				}
				case SerializedPropertyType.Float:
				{
					EditorGUI.BeginChangeCheck();
					bool flag2 = property.type == "float";
					double doubleValue = (!flag2) ? EditorGUI.DoubleField(position, label, property.doubleValue) : ((double)EditorGUI.FloatField(position, label, property.floatValue));
					if (EditorGUI.EndChangeCheck())
					{
						property.doubleValue = doubleValue;
					}
					goto IL_382;
				}
				case SerializedPropertyType.String:
				{
					EditorGUI.BeginChangeCheck();
					string stringValue = EditorGUI.TextField(position, label, property.stringValue);
					if (EditorGUI.EndChangeCheck())
					{
						property.stringValue = stringValue;
					}
					goto IL_382;
				}
				case SerializedPropertyType.Color:
				{
					EditorGUI.BeginChangeCheck();
					Color colorValue = EditorGUI.ColorField(position, label, property.colorValue);
					if (EditorGUI.EndChangeCheck())
					{
						property.colorValue = colorValue;
					}
					goto IL_382;
				}
				case SerializedPropertyType.ObjectReference:
					EditorGUI.ObjectFieldInternal(position, property, null, label, EditorStyles.objectField);
					goto IL_382;
				case SerializedPropertyType.LayerMask:
					EditorGUI.LayerMaskField(position, property, label);
					goto IL_382;
				case SerializedPropertyType.Enum:
					EditorGUI.Popup(position, property, label);
					goto IL_382;
				case SerializedPropertyType.Vector2:
					EditorGUI.Vector2Field(position, property, label);
					goto IL_382;
				case SerializedPropertyType.Vector3:
					EditorGUI.Vector3Field(position, property, label);
					goto IL_382;
				case SerializedPropertyType.Vector4:
					EditorGUI.Vector4Field(position, property, label);
					goto IL_382;
				case SerializedPropertyType.Rect:
					EditorGUI.RectField(position, property, label);
					goto IL_382;
				case SerializedPropertyType.ArraySize:
				{
					EditorGUI.BeginChangeCheck();
					int intValue = EditorGUI.ArraySizeField(position, label, property.intValue, EditorStyles.numberField);
					if (EditorGUI.EndChangeCheck())
					{
						property.intValue = intValue;
					}
					goto IL_382;
				}
				case SerializedPropertyType.Character:
				{
					char[] value = new char[]
					{
						(char)property.intValue
					};
					bool changed = GUI.changed;
					GUI.changed = false;
					string text = EditorGUI.TextField(position, label, new string(value));
					if (GUI.changed)
					{
						if (text.Length == 1)
						{
							property.intValue = (int)text[0];
						}
						else
						{
							GUI.changed = false;
						}
					}
					GUI.changed |= changed;
					goto IL_382;
				}
				case SerializedPropertyType.AnimationCurve:
				{
					int controlID = GUIUtility.GetControlID(EditorGUI.s_CurveHash, FocusType.Keyboard, position);
					EditorGUI.DoCurveField(EditorGUI.PrefixLabel(position, controlID, label), controlID, null, EditorGUI.kCurveColor, default(Rect), property);
					goto IL_382;
				}
				case SerializedPropertyType.Bounds:
					EditorGUI.BoundsField(position, property, label);
					goto IL_382;
				case SerializedPropertyType.Gradient:
				{
					int controlID2 = GUIUtility.GetControlID(EditorGUI.s_CurveHash, FocusType.Keyboard, position);
					EditorGUI.DoGradientField(EditorGUI.PrefixLabel(position, controlID2, label), controlID2, null, property, false);
					goto IL_382;
				}
				case SerializedPropertyType.FixedBufferSize:
					EditorGUI.IntField(position, label, property.intValue);
					goto IL_382;
				case SerializedPropertyType.Vector2Int:
					EditorGUI.Vector2IntField(position, property, label);
					goto IL_382;
				case SerializedPropertyType.Vector3Int:
					EditorGUI.Vector3IntField(position, property, label);
					goto IL_382;
				case SerializedPropertyType.RectInt:
					EditorGUI.RectIntField(position, property, label);
					goto IL_382;
				case SerializedPropertyType.BoundsInt:
					EditorGUI.BoundsIntField(position, property, label);
					goto IL_382;
				}
				int controlID3 = GUIUtility.GetControlID(EditorGUI.s_GenericField, FocusType.Keyboard, position);
				EditorGUI.PrefixLabel(position, controlID3, label);
				IL_382:;
			}
			else
			{
				Event @event = new Event(Event.current);
				flag = property.isExpanded;
				bool flag3 = flag;
				using (new EditorGUI.DisabledScope(!property.editable))
				{
					GUIStyle style = (DragAndDrop.activeControlID != -10) ? EditorStyles.foldout : EditorStyles.foldoutPreDrop;
					flag3 = EditorGUI.Foldout(position, flag, EditorGUI.s_PropertyFieldTempContent, true, style);
				}
				if (flag && property.isArray && property.arraySize > property.serializedObject.maxArraySizeForMultiEditing && property.serializedObject.isEditingMultipleObjects)
				{
					Rect position2 = position;
					position2.xMin += EditorGUIUtility.labelWidth - EditorGUI.indent;
					GUIContent arg_475_0 = EditorGUI.s_ArrayMultiInfoContent;
					string text2 = string.Format(EditorGUI.s_ArrayMultiInfoFormatString, property.serializedObject.maxArraySizeForMultiEditing);
					EditorGUI.s_ArrayMultiInfoContent.tooltip = text2;
					arg_475_0.text = text2;
					EditorGUI.LabelField(position2, GUIContent.none, EditorGUI.s_ArrayMultiInfoContent, EditorStyles.helpBox);
				}
				if (flag3 != flag)
				{
					if (Event.current.alt)
					{
						EditorGUI.SetExpandedRecurse(property, flag3);
					}
					else
					{
						property.isExpanded = flag3;
					}
				}
				flag = flag3;
				int s_LastControlID = EditorGUIUtility.s_LastControlID;
				EventType type = @event.type;
				if (type != EventType.DragExited)
				{
					if (type == EventType.DragUpdated || type == EventType.DragPerform)
					{
						if (position.Contains(@event.mousePosition) && GUI.enabled)
						{
							UnityEngine.Object[] objectReferences = DragAndDrop.objectReferences;
							UnityEngine.Object[] array = new UnityEngine.Object[1];
							bool flag4 = false;
							UnityEngine.Object[] array2 = objectReferences;
							for (int i = 0; i < array2.Length; i++)
							{
								UnityEngine.Object @object = array2[i];
								array[0] = @object;
								UnityEngine.Object object2 = EditorGUI.ValidateObjectFieldAssignment(array, null, property, EditorGUI.ObjectFieldValidatorOptions.None);
								if (object2 != null)
								{
									DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
									if (@event.type == EventType.DragPerform)
									{
										property.AppendFoldoutPPtrValue(object2);
										flag4 = true;
										DragAndDrop.activeControlID = 0;
									}
									else
									{
										DragAndDrop.activeControlID = s_LastControlID;
									}
								}
							}
							if (flag4)
							{
								GUI.changed = true;
								DragAndDrop.AcceptDrag();
							}
						}
					}
				}
				else if (GUI.enabled)
				{
					HandleUtility.Repaint();
				}
			}
			EditorGUI.EndProperty();
			return flag;
		}

		internal static void DrawLegend(Rect position, Color color, string label, bool enabled)
		{
			position = new Rect(position.x + 2f, position.y + 2f, position.width - 2f, position.height - 2f);
			Color backgroundColor = GUI.backgroundColor;
			if (enabled)
			{
				GUI.backgroundColor = color;
			}
			else
			{
				GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.45f);
			}
			GUI.Label(position, label, "ProfilerPaneSubLabel");
			GUI.backgroundColor = backgroundColor;
		}

		internal static string TextFieldDropDown(Rect position, string text, string[] dropDownElement)
		{
			return EditorGUI.TextFieldDropDown(position, GUIContent.none, text, dropDownElement);
		}

		internal static string TextFieldDropDown(Rect position, GUIContent label, string text, string[] dropDownElement)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TextFieldDropDownHash, FocusType.Keyboard, position);
			return EditorGUI.DoTextFieldDropDown(EditorGUI.PrefixLabel(position, controlID, label), controlID, text, dropDownElement, false);
		}

		internal static string DelayedTextFieldDropDown(Rect position, string text, string[] dropDownElement)
		{
			return EditorGUI.DelayedTextFieldDropDown(position, GUIContent.none, text, dropDownElement);
		}

		internal static string DelayedTextFieldDropDown(Rect position, GUIContent label, string text, string[] dropDownElement)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TextFieldDropDownHash, FocusType.Keyboard, position);
			return EditorGUI.DoTextFieldDropDown(EditorGUI.PrefixLabel(position, controlID, label), controlID, text, dropDownElement, true);
		}

		public static bool DropdownButton(Rect position, GUIContent content, FocusType focusType)
		{
			return EditorGUI.DropdownButton(position, content, focusType, "MiniPullDown");
		}

		public static bool DropdownButton(Rect position, GUIContent content, FocusType focusType, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_DropdownButtonHash, focusType, position);
			return EditorGUI.DropdownButton(controlID, position, content, style);
		}

		internal static bool DropdownButton(int id, Rect position, GUIContent content, GUIStyle style)
		{
			Event current = Event.current;
			EventType type = current.type;
			bool result;
			if (type != EventType.Repaint)
			{
				if (type != EventType.MouseDown)
				{
					if (type == EventType.KeyDown)
					{
						if (GUIUtility.keyboardControl == id && current.character == ' ')
						{
							Event.current.Use();
							result = true;
							return result;
						}
					}
				}
				else if (position.Contains(current.mousePosition) && current.button == 0)
				{
					Event.current.Use();
					result = true;
					return result;
				}
			}
			else if (EditorGUI.showMixedValue)
			{
				EditorGUI.BeginHandleMixedValueContentColor();
				style.Draw(position, EditorGUI.s_MixedValueContent, id, false);
				EditorGUI.EndHandleMixedValueContentColor();
			}
			else
			{
				style.Draw(position, content, id, false);
			}
			result = false;
			return result;
		}

		private static int EnumFlagsToInt(EditorGUI.EnumData enumData, Enum enumValue)
		{
			int result;
			if (enumData.unsigned)
			{
				if (enumData.underlyingType == typeof(uint))
				{
					result = (int)Convert.ToUInt32(enumValue);
				}
				else if (enumData.underlyingType == typeof(ushort))
				{
					ushort num = Convert.ToUInt16(enumValue);
					result = ((num != 65535) ? ((int)num) : -1);
				}
				else
				{
					byte b = Convert.ToByte(enumValue);
					result = ((b != 255) ? ((int)b) : -1);
				}
			}
			else
			{
				result = Convert.ToInt32(enumValue);
			}
			return result;
		}

		private static Enum IntToEnumFlags(Type enumType, int value)
		{
			EditorGUI.EnumData nonObsoleteEnumData = EditorGUI.GetNonObsoleteEnumData(enumType);
			Enum result;
			if (nonObsoleteEnumData.unsigned)
			{
				if (nonObsoleteEnumData.underlyingType == typeof(uint))
				{
					uint num = (uint)value;
					result = (Enum.Parse(enumType, num.ToString()) as Enum);
				}
				else if (nonObsoleteEnumData.underlyingType == typeof(ushort))
				{
					result = (Enum.Parse(enumType, ((ushort)value).ToString()) as Enum);
				}
				else
				{
					result = (Enum.Parse(enumType, ((byte)value).ToString()) as Enum);
				}
			}
			else
			{
				result = (Enum.Parse(enumType, value.ToString()) as Enum);
			}
			return result;
		}

		internal static int AdvancedPopup(Rect rect, int selectedIndex, string[] displayedOptions)
		{
			return StatelessAdvancedDropdown.SearchablePopup(rect, selectedIndex, displayedOptions, "MiniPullDown");
		}

		internal static int AdvancedPopup(Rect rect, int selectedIndex, string[] displayedOptions, GUIStyle style)
		{
			return StatelessAdvancedDropdown.SearchablePopup(rect, selectedIndex, displayedOptions, style);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		public static Enum EnumMaskField(Rect position, Enum enumValue)
		{
			return EditorGUI.EnumMaskField(position, enumValue, EditorStyles.popup);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		public static Enum EnumMaskField(Rect position, Enum enumValue, GUIStyle style)
		{
			return EditorGUI.EnumMaskFieldInternal(position, enumValue, style);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		public static Enum EnumMaskField(Rect position, string label, Enum enumValue)
		{
			return EditorGUI.EnumMaskField(position, label, enumValue, EditorStyles.popup);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		public static Enum EnumMaskField(Rect position, string label, Enum enumValue, GUIStyle style)
		{
			return EditorGUI.EnumMaskFieldInternal(position, EditorGUIUtility.TempContent(label), enumValue, style);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		public static Enum EnumMaskField(Rect position, GUIContent label, Enum enumValue)
		{
			return EditorGUI.EnumMaskField(position, label, enumValue, EditorStyles.popup);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		public static Enum EnumMaskField(Rect position, GUIContent label, Enum enumValue, GUIStyle style)
		{
			return EditorGUI.EnumMaskFieldInternal(position, label, enumValue, style);
		}

		[Obsolete("EnumMaskPopup has been deprecated. Use EnumFlagsField instead.")]
		public static Enum EnumMaskPopup(Rect position, string label, Enum selected)
		{
			return EditorGUI.EnumMaskPopup(position, label, selected, EditorStyles.popup);
		}

		[Obsolete("EnumMaskPopup has been deprecated. Use EnumFlagsField instead.")]
		public static Enum EnumMaskPopup(Rect position, string label, Enum selected, GUIStyle style)
		{
			int num;
			bool flag;
			return EditorGUI.EnumMaskPopup(position, label, selected, out num, out flag, style);
		}

		[Obsolete("EnumMaskPopup has been deprecated. Use EnumFlagsField instead.")]
		public static Enum EnumMaskPopup(Rect position, GUIContent label, Enum selected)
		{
			return EditorGUI.EnumMaskPopup(position, label, selected, EditorStyles.popup);
		}

		[Obsolete("EnumMaskPopup has been deprecated. Use EnumFlagsField instead.")]
		public static Enum EnumMaskPopup(Rect position, GUIContent label, Enum selected, GUIStyle style)
		{
			int num;
			bool flag;
			return EditorGUI.EnumMaskPopup(position, label, selected, out num, out flag, style);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		private static Enum EnumMaskField(Rect position, GUIContent label, Enum enumValue, GUIStyle style, out int changedFlags, out bool changedToValue)
		{
			return EditorGUI.DoEnumMaskField(position, label, enumValue, style, out changedFlags, out changedToValue);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		private static Enum EnumMaskFieldInternal(Rect position, Enum enumValue, GUIStyle style)
		{
			Type type = enumValue.GetType();
			if (!type.IsEnum)
			{
				throw new ArgumentException("Parameter enumValue must be of type System.Enum", "enumValue");
			}
			IEnumerable<string> arg_46_0 = Enum.GetNames(type);
			if (EditorGUI.<>f__mg$cache3 == null)
			{
				EditorGUI.<>f__mg$cache3 = new Func<string, string>(ObjectNames.NicifyVariableName);
			}
			string[] flagNames = arg_46_0.Select(EditorGUI.<>f__mg$cache3).ToArray<string>();
			int value = MaskFieldGUIDeprecated.DoMaskField(EditorGUI.IndentedRect(position), GUIUtility.GetControlID(EditorGUI.s_MaskField, FocusType.Keyboard, position), Convert.ToInt32(enumValue), flagNames, style);
			return EditorGUI.IntToEnumFlags(type, value);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		private static Enum EnumMaskFieldInternal(Rect position, GUIContent label, Enum enumValue, GUIStyle style)
		{
			Type type = enumValue.GetType();
			if (!type.IsEnum)
			{
				throw new ArgumentException("Parameter enumValue must be of type System.Enum", "enumValue");
			}
			int controlID = GUIUtility.GetControlID(EditorGUI.s_MaskField, FocusType.Keyboard, position);
			Rect position2 = EditorGUI.PrefixLabel(position, controlID, label);
			position.xMax = position2.x;
			IEnumerable<string> arg_6A_0 = Enum.GetNames(type);
			if (EditorGUI.<>f__mg$cache4 == null)
			{
				EditorGUI.<>f__mg$cache4 = new Func<string, string>(ObjectNames.NicifyVariableName);
			}
			string[] flagNames = arg_6A_0.Select(EditorGUI.<>f__mg$cache4).ToArray<string>();
			int value = MaskFieldGUIDeprecated.DoMaskField(position2, controlID, Convert.ToInt32(enumValue), flagNames, style);
			return EditorGUI.IntToEnumFlags(type, value);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		private static Enum DoEnumMaskField(Rect position, GUIContent label, Enum enumValue, GUIStyle style, out int changedFlags, out bool changedToValue)
		{
			Type type = enumValue.GetType();
			if (!type.IsEnum)
			{
				throw new ArgumentException("Parameter enumValue must be of type System.Enum", "enumValue");
			}
			int controlID = GUIUtility.GetControlID(EditorGUI.s_MaskField, FocusType.Keyboard, position);
			IEnumerable<string> arg_53_0 = Enum.GetNames(type);
			if (EditorGUI.<>f__mg$cache5 == null)
			{
				EditorGUI.<>f__mg$cache5 = new Func<string, string>(ObjectNames.NicifyVariableName);
			}
			string[] flagNames = arg_53_0.Select(EditorGUI.<>f__mg$cache5).ToArray<string>();
			int value = MaskFieldGUIDeprecated.DoMaskField(EditorGUI.PrefixLabel(position, controlID, label), controlID, Convert.ToInt32(enumValue), flagNames, style, out changedFlags, out changedToValue);
			return EditorGUI.IntToEnumFlags(type, value);
		}

		[Obsolete("EnumMaskField has been deprecated. Use EnumFlagsField instead.")]
		private static Enum EnumMaskPopup(Rect position, string label, Enum selected, out int changedFlags, out bool changedToValue, GUIStyle style)
		{
			return EditorGUI.EnumMaskPopup(position, EditorGUIUtility.TempContent(label), selected, out changedFlags, out changedToValue, style);
		}

		[Obsolete("EnumMaskPopup has been deprecated. Use EnumFlagsField instead.")]
		internal static Enum EnumMaskPopup(Rect position, GUIContent label, Enum selected, out int changedFlags, out bool changedToValue, GUIStyle style)
		{
			return EditorGUI.EnumMaskPopupInternal(position, label, selected, out changedFlags, out changedToValue, style);
		}

		[Obsolete("EnumMaskPopup has been deprecated. Use EnumFlagsField instead.")]
		private static Enum EnumMaskPopupInternal(Rect position, GUIContent label, Enum selected, out int changedFlags, out bool changedToValue, GUIStyle style)
		{
			return EditorGUI.EnumMaskField(position, label, selected, style, out changedFlags, out changedToValue);
		}

		internal static float AngularDial(Rect rect, GUIContent label, float angle, Texture thumbTexture, GUIStyle background, GUIStyle thumb)
		{
			int controlID = GUIUtility.GetControlID(FocusType.Passive);
			Event current = Event.current;
			if (label != null && label != GUIContent.none)
			{
				rect = EditorGUI.PrefixLabel(rect, controlID, label);
			}
			float num = Mathf.Min(rect.width, rect.height);
			Vector2 vector = (thumb != null && thumb != GUIStyle.none) ? thumb.CalcSize(GUIContent.Temp(thumbTexture)) : Vector2.zero;
			float num2 = Mathf.Max(vector.x, vector.y);
			float result;
			switch (current.GetTypeForControl(controlID))
			{
			case EventType.MouseDown:
				if (rect.Contains(current.mousePosition))
				{
					Vector2 vector2 = current.mousePosition - rect.center;
					float num3 = Mathf.Sqrt(vector2.x * vector2.x + vector2.y * vector2.y);
					if (num3 < num * 0.5f && num3 > num * 0.5f - num2)
					{
						result = EditorGUI.UseAngularDialEventAndGetAngle(controlID, current, rect.center, angle);
						return result;
					}
				}
				break;
			case EventType.MouseUp:
				if (GUIUtility.hotControl == controlID)
				{
					GUIUtility.hotControl = 0;
					result = EditorGUI.UseAngularDialEventAndGetAngle(controlID, current, rect.center, angle);
					return result;
				}
				break;
			case EventType.MouseDrag:
				if (GUIUtility.hotControl == controlID)
				{
					result = EditorGUI.UseAngularDialEventAndGetAngle(controlID, current, rect.center, angle);
					return result;
				}
				break;
			case EventType.Repaint:
			{
				bool isHover = false;
				if (rect.Contains(current.mousePosition))
				{
					Vector2 vector3 = current.mousePosition - rect.center;
					float num4 = Mathf.Sqrt(vector3.x * vector3.x + vector3.y * vector3.y);
					isHover = (num4 < num * 0.5f && num4 > num * 0.5f - num2);
				}
				bool isActive = GUIUtility.hotControl == controlID;
				if (background != null && background != GUIStyle.none)
				{
					background.Draw(rect, isHover, isActive, false, false);
				}
				if (thumb != null && thumb != GUIStyle.none)
				{
					float d = num * 0.5f - num2 * 0.5f;
					float f = -0.0174532924f * angle;
					Vector2 center = new Vector2(Mathf.Cos(f), Mathf.Sin(f)) * d + rect.center;
					Vector2 size = thumb.CalcSize(GUIContent.none);
					if (thumb.fixedWidth == 0f)
					{
						size.x = Mathf.Max(size.x, num2);
					}
					if (thumb.fixedHeight == 0f)
					{
						size.y = Mathf.Max(size.y, num2);
					}
					Rect position = new Rect
					{
						size = size,
						center = center
					};
					position.center = center;
					thumb.Draw(position, thumbTexture, position.Contains(current.mousePosition), isActive, false, false);
				}
				break;
			}
			}
			result = angle;
			return result;
		}

		private static float UseAngularDialEventAndGetAngle(int id, Event evt, Vector2 center, float angle)
		{
			GUIUtility.hotControl = ((evt.type != EventType.MouseUp) ? id : 0);
			GUIUtility.keyboardControl = 0;
			GUI.changed = true;
			evt.Use();
			Vector2 normalized = (evt.mousePosition - center).normalized;
			float target = -57.29578f * Mathf.Acos(normalized.x) * Mathf.Sign(Vector2.Dot(Vector2.up, normalized));
			return angle + Mathf.DeltaAngle(angle, target);
		}

		internal static bool ButtonWithRotatedIcon(Rect rect, GUIContent guiContent, float iconAngle, bool mouseDownButton, GUIStyle style)
		{
			bool result;
			if (mouseDownButton)
			{
				result = EditorGUI.DropdownButton(rect, GUIContent.Temp(guiContent.text, guiContent.tooltip), FocusType.Passive, style);
			}
			else
			{
				result = GUI.Button(rect, GUIContent.Temp(guiContent.text, guiContent.tooltip), style);
			}
			if (Event.current.type == EventType.Repaint && guiContent.image != null)
			{
				Vector2 iconSize = EditorGUIUtility.GetIconSize();
				if (iconSize == Vector2.zero)
				{
					iconSize.x = (iconSize.y = rect.height - (float)style.padding.vertical);
				}
				Rect position = new Rect(rect.x + (float)style.padding.left - 3f - iconSize.x, rect.y + (float)style.padding.top + 1f, iconSize.x, iconSize.y);
				if (iconAngle == 0f)
				{
					GUI.DrawTexture(position, guiContent.image);
				}
				else
				{
					Matrix4x4 matrix = GUI.matrix;
					GUIUtility.RotateAroundPivot(iconAngle, position.center);
					GUI.DrawTexture(position, guiContent.image);
					GUI.matrix = matrix;
				}
			}
			return result;
		}

		internal static Gradient GradientField(Rect position, Gradient gradient)
		{
			return EditorGUI.GradientField(position, gradient, false);
		}

		internal static Gradient GradientField(Rect position, Gradient gradient, bool hdr)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_GradientHash, FocusType.Keyboard, position);
			return EditorGUI.DoGradientField(position, controlID, gradient, null, hdr);
		}

		internal static Gradient GradientField(string label, Rect position, Gradient gradient)
		{
			return EditorGUI.GradientField(EditorGUIUtility.TempContent(label), position, gradient);
		}

		internal static Gradient GradientField(GUIContent label, Rect position, Gradient gradient)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_GradientHash, FocusType.Keyboard, position);
			return EditorGUI.DoGradientField(EditorGUI.PrefixLabel(position, controlID, label), controlID, gradient, null, false);
		}

		internal static Gradient GradientField(Rect position, SerializedProperty gradient)
		{
			return EditorGUI.GradientField(position, gradient, false);
		}

		internal static Gradient GradientField(Rect position, SerializedProperty gradient, bool hdr)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_GradientHash, FocusType.Keyboard, position);
			return EditorGUI.DoGradientField(position, controlID, null, gradient, hdr);
		}

		internal static Gradient GradientField(string label, Rect position, SerializedProperty property)
		{
			return EditorGUI.GradientField(EditorGUIUtility.TempContent(label), position, property);
		}

		internal static Gradient GradientField(GUIContent label, Rect position, SerializedProperty property)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_GradientHash, FocusType.Keyboard, position);
			return EditorGUI.DoGradientField(EditorGUI.PrefixLabel(position, controlID, label), controlID, null, property, false);
		}

		internal static Gradient DoGradientField(Rect position, int id, Gradient value, SerializedProperty property, bool hdr)
		{
			Event current = Event.current;
			EventType typeForControl = current.GetTypeForControl(id);
			Gradient result;
			switch (typeForControl)
			{
			case EventType.KeyDown:
				if (GUIUtility.keyboardControl == id && (current.keyCode == KeyCode.Space || current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
				{
					Event.current.Use();
					Gradient newGradient = (property == null) ? value : property.gradientValue;
					GradientPicker.Show(newGradient, hdr);
					GUIUtility.ExitGUI();
				}
				goto IL_22C;
			case EventType.KeyUp:
			case EventType.ScrollWheel:
				IL_27:
				if (typeForControl != EventType.ValidateCommand)
				{
					if (typeForControl != EventType.ExecuteCommand)
					{
						if (typeForControl != EventType.MouseDown)
						{
							goto IL_22C;
						}
						if (position.Contains(current.mousePosition))
						{
							if (current.button == 0)
							{
								EditorGUI.s_GradientID = id;
								GUIUtility.keyboardControl = id;
								Gradient newGradient2 = (property == null) ? value : property.gradientValue;
								GradientPicker.Show(newGradient2, hdr);
								GUIUtility.ExitGUI();
							}
							else if (current.button == 1)
							{
								if (property != null)
								{
									GradientContextMenu.Show(property.Copy());
								}
							}
						}
						goto IL_22C;
					}
					else
					{
						if (EditorGUI.s_GradientID == id && current.commandName == "GradientPickerChanged")
						{
							GUI.changed = true;
							GradientPreviewCache.ClearCache();
							HandleUtility.Repaint();
							if (property != null)
							{
								property.gradientValue = GradientPicker.gradient;
							}
							result = GradientPicker.gradient;
							return result;
						}
						goto IL_22C;
					}
				}
				else
				{
					if (EditorGUI.s_GradientID == id && current.commandName == "UndoRedoPerformed")
					{
						if (property != null)
						{
							GradientPicker.SetCurrentGradient(property.gradientValue);
						}
						GradientPreviewCache.ClearCache();
						result = value;
						return result;
					}
					goto IL_22C;
				}
				break;
			case EventType.Repaint:
			{
				Rect position2 = new Rect(position.x + 1f, position.y + 1f, position.width - 2f, position.height - 2f);
				if (property != null)
				{
					GradientEditor.DrawGradientSwatch(position2, property, Color.white);
				}
				else
				{
					GradientEditor.DrawGradientSwatch(position2, value, Color.white);
				}
				EditorStyles.colorPickerBox.Draw(position, GUIContent.none, id);
				goto IL_22C;
			}
			}
			goto IL_27;
			IL_22C:
			result = value;
			return result;
		}

		internal static Color32 HexColorTextField(Rect rect, GUIContent label, Color32 color, bool showAlpha)
		{
			return EditorGUI.HexColorTextField(rect, label, color, showAlpha, EditorStyles.textField);
		}

		internal static Color32 HexColorTextField(Rect rect, GUIContent label, Color32 color, bool showAlpha, GUIStyle style)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_TextFieldHash, FocusType.Keyboard, rect);
			return EditorGUI.DoHexColorTextField(controlID, EditorGUI.PrefixLabel(rect, controlID, label), color, showAlpha, style);
		}

		internal static Color32 DoHexColorTextField(int id, Rect rect, Color32 color, bool showAlpha, GUIStyle style)
		{
			string text = (!showAlpha) ? ColorUtility.ToHtmlStringRGB(color) : ColorUtility.ToHtmlStringRGBA(color);
			EditorGUI.BeginChangeCheck();
			bool flag;
			string str = EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, id, rect, text, style, "0123456789ABCDEFabcdef", out flag, false, false, false);
			if (EditorGUI.EndChangeCheck())
			{
				EditorGUI.s_RecycledEditor.text = EditorGUI.s_RecycledEditor.text.ToUpper();
				Color color2;
				if (ColorUtility.TryParseHtmlString("#" + str, out color2))
				{
					color = new Color(color2.r, color2.g, color2.b, (!showAlpha) ? ((float)color.a) : color2.a);
				}
			}
			return color;
		}

		internal static bool Button(Rect position, GUIContent content)
		{
			return EditorGUI.Button(position, content, EditorStyles.miniButton);
		}

		internal static bool Button(Rect position, GUIContent content, GUIStyle style)
		{
			Event current = Event.current;
			EventType type = current.type;
			bool result;
			if (type == EventType.MouseDown || type == EventType.MouseUp)
			{
				if (current.button != 0)
				{
					result = false;
					return result;
				}
			}
			result = GUI.Button(position, content, style);
			return result;
		}

		internal static bool IconButton(int id, Rect position, GUIContent content, GUIStyle style)
		{
			GUIUtility.CheckOnGUI();
			bool result;
			switch (Event.current.GetTypeForControl(id))
			{
			case EventType.MouseDown:
				if (position.Contains(Event.current.mousePosition))
				{
					GUIUtility.hotControl = id;
					Event.current.Use();
					result = true;
					return result;
				}
				result = false;
				return result;
			case EventType.MouseUp:
				if (GUIUtility.hotControl == id)
				{
					GUIUtility.hotControl = 0;
					Event.current.Use();
					result = position.Contains(Event.current.mousePosition);
					return result;
				}
				result = false;
				return result;
			case EventType.MouseDrag:
				if (position.Contains(Event.current.mousePosition))
				{
					GUIUtility.hotControl = id;
					Event.current.Use();
					result = true;
					return result;
				}
				break;
			case EventType.Repaint:
				style.Draw(position, content, id);
				break;
			}
			result = false;
			return result;
		}

		internal static float WidthResizer(Rect position, float width, float minWidth, float maxWidth)
		{
			bool flag;
			return EditorGUI.Resizer.Resize(position, width, minWidth, maxWidth, true, out flag);
		}

		internal static float WidthResizer(Rect position, float width, float minWidth, float maxWidth, out bool hasControl)
		{
			return EditorGUI.Resizer.Resize(position, width, minWidth, maxWidth, true, out hasControl);
		}

		internal static float HeightResizer(Rect position, float height, float minHeight, float maxHeight)
		{
			bool flag;
			return EditorGUI.Resizer.Resize(position, height, minHeight, maxHeight, false, out flag);
		}

		internal static float HeightResizer(Rect position, float height, float minHeight, float maxHeight, out bool hasControl)
		{
			return EditorGUI.Resizer.Resize(position, height, minHeight, maxHeight, false, out hasControl);
		}

		internal static Vector2 MouseDeltaReader(Rect position, bool activated)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_MouseDeltaReaderHash, FocusType.Passive, position);
			Event current = Event.current;
			EventType typeForControl = current.GetTypeForControl(controlID);
			Vector2 result;
			if (typeForControl != EventType.MouseDown)
			{
				if (typeForControl != EventType.MouseDrag)
				{
					if (typeForControl == EventType.MouseUp)
					{
						if (GUIUtility.hotControl == controlID && current.button == 0)
						{
							GUIUtility.hotControl = 0;
							current.Use();
						}
					}
				}
				else if (GUIUtility.hotControl == controlID)
				{
					Vector2 a = GUIClip.Unclip(current.mousePosition);
					Vector2 vector = a - EditorGUI.s_MouseDeltaReaderLastPos;
					EditorGUI.s_MouseDeltaReaderLastPos = a;
					current.Use();
					result = vector;
					return result;
				}
			}
			else if (activated && GUIUtility.hotControl == 0 && position.Contains(current.mousePosition) && current.button == 0)
			{
				GUIUtility.hotControl = controlID;
				GUIUtility.keyboardControl = 0;
				EditorGUI.s_MouseDeltaReaderLastPos = GUIClip.Unclip(current.mousePosition);
				current.Use();
			}
			result = Vector2.zero;
			return result;
		}

		internal static bool ButtonWithDropdownList(string buttonName, string[] buttonNames, GenericMenu.MenuFunction2 callback, params GUILayoutOption[] options)
		{
			GUIContent content = EditorGUIUtility.TempContent(buttonName);
			return EditorGUI.ButtonWithDropdownList(content, buttonNames, callback, options);
		}

		internal static bool ButtonWithDropdownList(GUIContent content, string[] buttonNames, GenericMenu.MenuFunction2 callback, params GUILayoutOption[] options)
		{
			Rect rect = GUILayoutUtility.GetRect(content, EditorStyles.dropDownList, options);
			Rect rect2 = rect;
			rect2.xMin = rect2.xMax - 20f;
			bool result;
			if (Event.current.type == EventType.MouseDown && rect2.Contains(Event.current.mousePosition))
			{
				GenericMenu genericMenu = new GenericMenu();
				for (int num = 0; num != buttonNames.Length; num++)
				{
					genericMenu.AddItem(new GUIContent(buttonNames[num]), false, callback, num);
				}
				genericMenu.DropDown(rect);
				Event.current.Use();
				result = false;
			}
			else
			{
				result = GUI.Button(rect, content, EditorStyles.dropDownList);
			}
			return result;
		}

		internal static void GameViewSizePopup(Rect buttonRect, GameViewSizeGroupType groupType, int selectedIndex, IGameViewSizeMenuUser gameView, GUIStyle guiStyle)
		{
			GameViewSizeGroup group = ScriptableSingleton<GameViewSizes>.instance.GetGroup(groupType);
			string t = "";
			if (selectedIndex >= 0 && selectedIndex < group.GetTotalCount())
			{
				t = group.GetGameViewSize(selectedIndex).displayText;
			}
			if (EditorGUI.DropdownButton(buttonRect, GUIContent.Temp(t), FocusType.Passive, guiStyle))
			{
				GameViewSizesMenuItemProvider itemProvider = new GameViewSizesMenuItemProvider(groupType);
				GameViewSizeMenu windowContent = new GameViewSizeMenu(itemProvider, selectedIndex, new GameViewSizesMenuModifyItemUI(), gameView);
				PopupWindow.Show(buttonRect, windowContent);
			}
		}

		public static void DrawRect(Rect rect, Color color)
		{
			if (Event.current.type == EventType.Repaint)
			{
				Color color2 = GUI.color;
				GUI.color *= color;
				GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
				GUI.color = color2;
			}
		}

		internal static void DrawDelimiterLine(Rect rect)
		{
			EditorGUI.DrawRect(rect, EditorGUI.kSplitLineSkinnedColor.color);
		}

		internal static void DrawOutline(Rect rect, float size, Color color)
		{
			if (Event.current.type == EventType.Repaint)
			{
				Color color2 = GUI.color;
				GUI.color *= color;
				GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, size), EditorGUIUtility.whiteTexture);
				GUI.DrawTexture(new Rect(rect.x, rect.yMax - size, rect.width, size), EditorGUIUtility.whiteTexture);
				GUI.DrawTexture(new Rect(rect.x, rect.y + 1f, size, rect.height - 2f * size), EditorGUIUtility.whiteTexture);
				GUI.DrawTexture(new Rect(rect.xMax - size, rect.y + 1f, size, rect.height - 2f * size), EditorGUIUtility.whiteTexture);
				GUI.color = color2;
			}
		}

		internal static float Knob(Rect position, Vector2 knobSize, float currentValue, float start, float end, string unit, Color backgroundColor, Color activeColor, bool showValue, int id)
		{
			EditorGUI.KnobContext knobContext = new EditorGUI.KnobContext(position, knobSize, currentValue, start, end, unit, backgroundColor, activeColor, showValue, id);
			return knobContext.Handle();
		}

		internal static float OffsetKnob(Rect position, float currentValue, float start, float end, float median, string unit, Color backgroundColor, Color activeColor, GUIStyle knob, int id)
		{
			return 0f;
		}

		internal static UnityEngine.Object DoObjectField(Rect position, Rect dropRect, int id, UnityEngine.Object obj, Type objType, SerializedProperty property, EditorGUI.ObjectFieldValidator validator, bool allowSceneObjects)
		{
			return EditorGUI.DoObjectField(position, dropRect, id, obj, objType, property, validator, allowSceneObjects, EditorStyles.objectField);
		}

		internal static void PingObjectOrShowPreviewOnClick(UnityEngine.Object targetObject, Rect position)
		{
			if (!(targetObject == null))
			{
				Event current = Event.current;
				if (!current.shift && !current.control)
				{
					EditorGUIUtility.PingObject(targetObject);
				}
				else if (targetObject is Texture)
				{
					PopupWindowWithoutFocus.Show(new RectOffset(6, 3, 0, 3).Add(position), new ObjectPreviewPopup(targetObject), new PopupLocationHelper.PopupLocation[]
					{
						PopupLocationHelper.PopupLocation.Left,
						PopupLocationHelper.PopupLocation.Below,
						PopupLocationHelper.PopupLocation.Right
					});
				}
			}
		}

		private static UnityEngine.Object AssignSelectedObject(SerializedProperty property, EditorGUI.ObjectFieldValidator validator, Type objectType, Event evt)
		{
			UnityEngine.Object[] references = new UnityEngine.Object[]
			{
				ObjectSelector.GetCurrentObject()
			};
			UnityEngine.Object @object = validator(references, objectType, property, EditorGUI.ObjectFieldValidatorOptions.None);
			if (property != null)
			{
				property.objectReferenceValue = @object;
			}
			GUI.changed = true;
			evt.Use();
			return @object;
		}

		internal static UnityEngine.Object DoObjectField(Rect position, Rect dropRect, int id, UnityEngine.Object obj, Type objType, SerializedProperty property, EditorGUI.ObjectFieldValidator validator, bool allowSceneObjects, GUIStyle style)
		{
			if (validator == null)
			{
				if (EditorGUI.<>f__mg$cache6 == null)
				{
					EditorGUI.<>f__mg$cache6 = new EditorGUI.ObjectFieldValidator(EditorGUI.ValidateObjectFieldAssignment);
				}
				validator = EditorGUI.<>f__mg$cache6;
			}
			Event current = Event.current;
			EventType eventType = current.type;
			if (!GUI.enabled && GUIClip.enabled && Event.current.rawType == EventType.MouseDown)
			{
				eventType = Event.current.rawType;
			}
			bool flag = EditorGUIUtility.HasObjectThumbnail(objType);
			EditorGUI.ObjectFieldVisualType objectFieldVisualType = EditorGUI.ObjectFieldVisualType.IconAndText;
			if (flag && position.height <= 18f && position.width <= 32f)
			{
				objectFieldVisualType = EditorGUI.ObjectFieldVisualType.MiniPreview;
			}
			else if (flag && position.height > 16f)
			{
				objectFieldVisualType = EditorGUI.ObjectFieldVisualType.LargePreview;
			}
			Vector2 iconSize = EditorGUIUtility.GetIconSize();
			if (objectFieldVisualType == EditorGUI.ObjectFieldVisualType.IconAndText)
			{
				EditorGUIUtility.SetIconSize(new Vector2(12f, 12f));
			}
			else if (objectFieldVisualType == EditorGUI.ObjectFieldVisualType.LargePreview)
			{
				EditorGUIUtility.SetIconSize(new Vector2(64f, 64f));
			}
			UnityEngine.Object result;
			switch (eventType)
			{
			case EventType.KeyDown:
				if (GUIUtility.keyboardControl == id)
				{
					if (current.keyCode == KeyCode.Backspace || current.keyCode == KeyCode.Delete)
					{
						if (property != null)
						{
							property.objectReferenceValue = null;
						}
						else
						{
							obj = null;
						}
						GUI.changed = true;
						current.Use();
					}
					if (current.MainActionKeyForControl(id))
					{
						ObjectSelector.get.Show(obj, objType, property, allowSceneObjects);
						ObjectSelector.get.objectSelectorID = id;
						current.Use();
						GUIUtility.ExitGUI();
					}
				}
				goto IL_66E;
			case EventType.KeyUp:
			case EventType.ScrollWheel:
			case EventType.Layout:
				IL_119:
				if (eventType != EventType.ExecuteCommand)
				{
					if (eventType == EventType.DragExited)
					{
						if (GUI.enabled)
						{
							HandleUtility.Repaint();
						}
						goto IL_66E;
					}
					if (eventType != EventType.MouseDown)
					{
						goto IL_66E;
					}
					if (Event.current.button != 0)
					{
						goto IL_66E;
					}
					if (position.Contains(Event.current.mousePosition))
					{
						Rect rect;
						switch (objectFieldVisualType)
						{
						case EditorGUI.ObjectFieldVisualType.IconAndText:
						case EditorGUI.ObjectFieldVisualType.MiniPreview:
							rect = new Rect(position.xMax - 15f, position.y, 15f, position.height);
							break;
						case EditorGUI.ObjectFieldVisualType.LargePreview:
							rect = new Rect(position.xMax - 36f, position.yMax - 14f, 36f, 14f);
							break;
						default:
							throw new ArgumentOutOfRangeException();
						}
						EditorGUIUtility.editingTextField = false;
						if (rect.Contains(Event.current.mousePosition))
						{
							if (GUI.enabled)
							{
								GUIUtility.keyboardControl = id;
								ObjectSelector.get.Show(obj, objType, property, allowSceneObjects);
								ObjectSelector.get.objectSelectorID = id;
								current.Use();
								GUIUtility.ExitGUI();
							}
						}
						else
						{
							UnityEngine.Object @object = (property == null) ? obj : property.objectReferenceValue;
							Component component = @object as Component;
							if (component)
							{
								@object = component.gameObject;
							}
							if (EditorGUI.showMixedValue)
							{
								@object = null;
							}
							if (Event.current.clickCount == 1)
							{
								GUIUtility.keyboardControl = id;
								EditorGUI.PingObjectOrShowPreviewOnClick(@object, position);
								current.Use();
							}
							else if (Event.current.clickCount == 2)
							{
								if (@object)
								{
									AssetDatabase.OpenAsset(@object);
									GUIUtility.ExitGUI();
								}
								current.Use();
							}
						}
					}
					goto IL_66E;
				}
				else
				{
					string commandName = current.commandName;
					if (commandName == "ObjectSelectorUpdated" && ObjectSelector.get.objectSelectorID == id && GUIUtility.keyboardControl == id && (property == null || !property.isScript))
					{
						result = EditorGUI.AssignSelectedObject(property, validator, objType, current);
						return result;
					}
					if (!(commandName == "ObjectSelectorClosed") || ObjectSelector.get.objectSelectorID != id || GUIUtility.keyboardControl != id || property == null || !property.isScript)
					{
						goto IL_66E;
					}
					if (ObjectSelector.get.GetInstanceID() == 0)
					{
						current.Use();
						goto IL_66E;
					}
					result = EditorGUI.AssignSelectedObject(property, validator, objType, current);
					return result;
				}
				break;
			case EventType.Repaint:
			{
				GUIContent gUIContent;
				if (EditorGUI.showMixedValue)
				{
					gUIContent = EditorGUI.s_MixedValueContent;
				}
				else if (property != null)
				{
					gUIContent = EditorGUIUtility.TempContent(property.objectReferenceStringValue, AssetPreview.GetMiniThumbnail(property.objectReferenceValue));
					obj = property.objectReferenceValue;
					if (obj != null)
					{
						UnityEngine.Object[] references = new UnityEngine.Object[]
						{
							obj
						};
						if (EditorSceneManager.preventCrossSceneReferences && EditorGUI.CheckForCrossSceneReferencing(obj, property.serializedObject.targetObject))
						{
							if (!EditorApplication.isPlaying)
							{
								gUIContent = EditorGUIUtility.TempContent("Scene mismatch (cross scene references not supported)");
							}
							else
							{
								gUIContent.text += string.Format(" ({0})", EditorGUI.GetGameObjectFromObject(obj).scene.name);
							}
						}
						else if (validator(references, objType, property, EditorGUI.ObjectFieldValidatorOptions.ExactObjectTypeValidation) == null)
						{
							gUIContent = EditorGUIUtility.TempContent("Type mismatch");
						}
					}
				}
				else
				{
					gUIContent = EditorGUIUtility.ObjectContent(obj, objType);
				}
				switch (objectFieldVisualType)
				{
				case EditorGUI.ObjectFieldVisualType.IconAndText:
					EditorGUI.BeginHandleMixedValueContentColor();
					style.Draw(position, gUIContent, id, DragAndDrop.activeControlID == id);
					EditorGUI.EndHandleMixedValueContentColor();
					break;
				case EditorGUI.ObjectFieldVisualType.LargePreview:
					EditorGUI.DrawObjectFieldLargeThumb(position, id, obj, gUIContent);
					break;
				case EditorGUI.ObjectFieldVisualType.MiniPreview:
					EditorGUI.DrawObjectFieldMiniThumb(position, id, obj, gUIContent);
					break;
				default:
					throw new ArgumentOutOfRangeException();
				}
				goto IL_66E;
			}
			case EventType.DragUpdated:
			case EventType.DragPerform:
				if (dropRect.Contains(Event.current.mousePosition) && GUI.enabled)
				{
					UnityEngine.Object[] objectReferences = DragAndDrop.objectReferences;
					UnityEngine.Object object2 = validator(objectReferences, objType, property, EditorGUI.ObjectFieldValidatorOptions.None);
					if (object2 != null)
					{
						if (!allowSceneObjects && !EditorUtility.IsPersistent(object2))
						{
							object2 = null;
						}
					}
					if (object2 != null)
					{
						DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
						if (eventType == EventType.DragPerform)
						{
							if (property != null)
							{
								property.objectReferenceValue = object2;
							}
							else
							{
								obj = object2;
							}
							GUI.changed = true;
							DragAndDrop.AcceptDrag();
							DragAndDrop.activeControlID = 0;
						}
						else
						{
							DragAndDrop.activeControlID = id;
						}
						Event.current.Use();
					}
				}
				goto IL_66E;
			}
			goto IL_119;
			IL_66E:
			EditorGUIUtility.SetIconSize(iconSize);
			result = obj;
			return result;
		}

		private static void DrawObjectFieldLargeThumb(Rect position, int id, UnityEngine.Object obj, GUIContent content)
		{
			GUIStyle objectFieldThumb = EditorStyles.objectFieldThumb;
			objectFieldThumb.Draw(position, GUIContent.none, id, DragAndDrop.activeControlID == id);
			if (obj != null && !EditorGUI.showMixedValue)
			{
				bool flag = obj is Cubemap;
				bool flag2 = obj is Sprite;
				Rect position2 = objectFieldThumb.padding.Remove(position);
				if (flag || flag2)
				{
					Texture2D assetPreview = AssetPreview.GetAssetPreview(obj);
					if (assetPreview != null)
					{
						if (flag2 || assetPreview.alphaIsTransparency)
						{
							EditorGUI.DrawTextureTransparent(position2, assetPreview);
						}
						else
						{
							EditorGUI.DrawPreviewTexture(position2, assetPreview);
						}
					}
					else
					{
						position2.x += (position2.width - (float)content.image.width) / 2f;
						position2.y += (position2.height - (float)content.image.width) / 2f;
						GUIStyle.none.Draw(position2, content.image, false, false, false, false);
						HandleUtility.Repaint();
					}
				}
				else
				{
					Texture2D texture2D = content.image as Texture2D;
					if (texture2D != null && texture2D.alphaIsTransparency)
					{
						EditorGUI.DrawTextureTransparent(position2, texture2D);
					}
					else
					{
						EditorGUI.DrawPreviewTexture(position2, content.image);
					}
				}
			}
			else
			{
				GUIStyle gUIStyle = objectFieldThumb.name + "Overlay";
				EditorGUI.BeginHandleMixedValueContentColor();
				gUIStyle.Draw(position, content, id);
				EditorGUI.EndHandleMixedValueContentColor();
			}
			GUIStyle gUIStyle2 = objectFieldThumb.name + "Overlay2";
			gUIStyle2.Draw(position, EditorGUIUtility.TempContent("Select"), id);
		}

		private static void DrawObjectFieldMiniThumb(Rect position, int id, UnityEngine.Object obj, GUIContent content)
		{
			GUIStyle objectFieldMiniThumb = EditorStyles.objectFieldMiniThumb;
			position.width = 32f;
			EditorGUI.BeginHandleMixedValueContentColor();
			bool isHover = obj != null;
			bool on = DragAndDrop.activeControlID == id;
			bool hasKeyboardFocus = GUIUtility.keyboardControl == id;
			objectFieldMiniThumb.Draw(position, isHover, false, on, hasKeyboardFocus);
			EditorGUI.EndHandleMixedValueContentColor();
			if (obj != null && !EditorGUI.showMixedValue)
			{
				Rect position2 = new Rect(position.x + 1f, position.y + 1f, position.height - 2f, position.height - 2f);
				Texture2D texture2D = content.image as Texture2D;
				if (texture2D != null && texture2D.alphaIsTransparency)
				{
					EditorGUI.DrawTextureTransparent(position2, texture2D);
				}
				else
				{
					EditorGUI.DrawPreviewTexture(position2, content.image);
				}
				if (position2.Contains(Event.current.mousePosition))
				{
					GUI.Label(position2, GUIContent.Temp(string.Empty, "Ctrl + Click to show preview"));
				}
			}
		}

		internal static UnityEngine.Object DoDropField(Rect position, int id, Type objType, EditorGUI.ObjectFieldValidator validator, bool allowSceneObjects, GUIStyle style)
		{
			if (validator == null)
			{
				if (EditorGUI.<>f__mg$cache7 == null)
				{
					EditorGUI.<>f__mg$cache7 = new EditorGUI.ObjectFieldValidator(EditorGUI.ValidateObjectFieldAssignment);
				}
				validator = EditorGUI.<>f__mg$cache7;
			}
			Event current = Event.current;
			EventType eventType = current.type;
			if (!GUI.enabled && GUIClip.enabled && Event.current.rawType == EventType.MouseDown)
			{
				eventType = Event.current.rawType;
			}
			UnityEngine.Object result;
			switch (eventType)
			{
			case EventType.Repaint:
				style.Draw(position, GUIContent.none, id, DragAndDrop.activeControlID == id);
				goto IL_161;
			case EventType.Layout:
				IL_79:
				if (eventType != EventType.DragExited)
				{
					goto IL_161;
				}
				if (GUI.enabled)
				{
					HandleUtility.Repaint();
				}
				goto IL_161;
			case EventType.DragUpdated:
			case EventType.DragPerform:
				if (position.Contains(Event.current.mousePosition) && GUI.enabled)
				{
					UnityEngine.Object[] objectReferences = DragAndDrop.objectReferences;
					UnityEngine.Object @object = validator(objectReferences, objType, null, EditorGUI.ObjectFieldValidatorOptions.None);
					if (@object != null)
					{
						if (!allowSceneObjects && !EditorUtility.IsPersistent(@object))
						{
							@object = null;
						}
					}
					if (@object != null)
					{
						DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
						if (eventType == EventType.DragPerform)
						{
							GUI.changed = true;
							DragAndDrop.AcceptDrag();
							DragAndDrop.activeControlID = 0;
							Event.current.Use();
							result = @object;
							return result;
						}
						DragAndDrop.activeControlID = id;
						Event.current.Use();
					}
				}
				goto IL_161;
			}
			goto IL_79;
			IL_161:
			result = null;
			return result;
		}

		internal static void SliderWithTexture(Rect position, GUIContent label, SerializedProperty property, float sliderMin, float sliderMax, float power, Texture2D sliderBackground)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			EditorGUI.BeginChangeCheck();
			string formatString = (property.propertyType != SerializedPropertyType.Integer) ? EditorGUI.kFloatFieldFormatString : EditorGUI.kIntFieldFormatString;
			float floatValue = EditorGUI.SliderWithTexture(position, label, property.floatValue, sliderMin, sliderMax, formatString, sliderMin, sliderMax, power, sliderBackground);
			if (EditorGUI.EndChangeCheck())
			{
				property.floatValue = floatValue;
			}
			EditorGUI.EndProperty();
		}

		internal static float SliderWithTexture(Rect rect, GUIContent label, float sliderValue, float sliderMin, float sliderMax, string formatString, Texture2D sliderBackground, params GUILayoutOption[] options)
		{
			return EditorGUI.SliderWithTexture(rect, label, sliderValue, sliderMin, sliderMax, formatString, sliderMin, sliderMax, 1f, sliderBackground);
		}

		internal static float SliderWithTexture(Rect position, GUIContent label, float sliderValue, float sliderMin, float sliderMax, string formatString, float textFieldMin, float textFieldMax, float power, Texture2D sliderBackground)
		{
			int controlID = GUIUtility.GetControlID(EditorGUI.s_SliderHash, FocusType.Keyboard, position);
			Rect position2 = EditorGUI.PrefixLabel(position, controlID, label);
			Rect dragZonePosition = (!EditorGUI.LabelHasContent(label)) ? default(Rect) : EditorGUIUtility.DragZoneRect(position);
			return EditorGUI.DoSlider(position2, dragZonePosition, controlID, sliderValue, sliderMin, sliderMax, formatString, textFieldMin, textFieldMax, power, "ColorPickerSliderBackground", "ColorPickerHorizThumb", sliderBackground);
		}

		internal static void TargetChoiceField(Rect position, SerializedProperty property, GUIContent label)
		{
			if (EditorGUI.<>f__mg$cache8 == null)
			{
				EditorGUI.<>f__mg$cache8 = new TargetChoiceHandler.TargetChoiceMenuFunction(TargetChoiceHandler.SetToValueOfTarget);
			}
			EditorGUI.TargetChoiceField(position, property, label, EditorGUI.<>f__mg$cache8);
		}

		internal static void TargetChoiceField(Rect position, SerializedProperty property, GUIContent label, TargetChoiceHandler.TargetChoiceMenuFunction func)
		{
			EditorGUI.BeginProperty(position, label, property);
			position = EditorGUI.PrefixLabel(position, 0, label);
			EditorGUI.BeginHandleMixedValueContentColor();
			if (GUI.Button(position, EditorGUI.mixedValueContent, EditorStyles.popup))
			{
				GenericMenu genericMenu = new GenericMenu();
				TargetChoiceHandler.AddSetToValueOfTargetMenuItems(genericMenu, property, func);
				genericMenu.DropDown(position);
			}
			EditorGUI.EndHandleMixedValueContentColor();
			EditorGUI.EndProperty();
		}

		internal static string DoTextFieldDropDown(Rect rect, int id, string text, string[] dropDownElements, bool delayed)
		{
			Rect position = new Rect(rect.x, rect.y, rect.width - EditorStyles.textFieldDropDown.fixedWidth, rect.height);
			Rect rect2 = new Rect(position.xMax, position.y, EditorStyles.textFieldDropDown.fixedWidth, rect.height);
			if (delayed)
			{
				text = EditorGUI.DelayedTextField(position, text, EditorStyles.textFieldDropDownText);
			}
			else
			{
				bool flag;
				text = EditorGUI.DoTextField(EditorGUI.s_RecycledEditor, id, position, text, EditorStyles.textFieldDropDownText, null, out flag, false, false, false);
			}
			EditorGUI.BeginChangeCheck();
			int indentLevel = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;
			Rect arg_C7_0 = rect2;
			string arg_C7_1 = "";
			int arg_C7_2 = -1;
			string[] arg_C7_3;
			if (dropDownElements.Length > 0)
			{
				arg_C7_3 = dropDownElements;
			}
			else
			{
				(arg_C7_3 = new string[1])[0] = "--empty--";
			}
			int num = EditorGUI.Popup(arg_C7_0, arg_C7_1, arg_C7_2, arg_C7_3, EditorStyles.textFieldDropDown);
			if (EditorGUI.EndChangeCheck() && dropDownElements.Length > 0)
			{
				text = dropDownElements[num];
			}
			EditorGUI.indentLevel = indentLevel;
			return text;
		}
	}
}
