using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities.Properties;
using UnityEngine;
using Unity.Properties;
using Unity.Mathematics;
using UnityEditor;

namespace Unity.Entities.Editor
{

    public class EntityIMGUIVisitor : PropertyVisitor
        , IPrimitivePropertyVisitor
        , ICustomVisitPrimitives
        , ICustomVisit<Unity.Mathematics.quaternion>
        , ICustomVisit<Unity.Mathematics.float2>
        , ICustomVisit<Unity.Mathematics.float3>
        , ICustomVisit<Unity.Mathematics.float4>
        , ICustomVisit<Unity.Mathematics.float4x4>
        , ICustomVisit<Unity.Mathematics.float3x3>
        , ICustomVisit<Unity.Mathematics.float2x2>
    {
        private static HashSet<Type> _primitiveTypes = new HashSet<Type>();

        static EntityIMGUIVisitor()
        {
            foreach (var it in typeof(EntityIMGUIVisitor).GetInterfaces())
            {
                if (it.IsGenericType && typeof(ICustomVisit<>) == it.GetGenericTypeDefinition())
                {
                    var genArgs = it.GetGenericArguments();
                    if (genArgs.Length == 1)
                    {
                        _primitiveTypes.Add(genArgs[0]);
                    }
                }
            }
            foreach (var it in typeof(PropertyVisitor).GetInterfaces())
            {
                if (it.IsGenericType && typeof(ICustomVisit<>) == it.GetGenericTypeDefinition())
                {
                    var genArgs = it.GetGenericArguments();
                    if (genArgs.Length == 1)
                    {
                        _primitiveTypes.Add(genArgs[0]);
                    }
                }
            }
        }

        public HashSet<Type> SupportedPrimitiveTypes()
        {
            return _primitiveTypes;
        }

        private class ComponentState
        {
            public ComponentState()
            {
                Showing = true;
            }
            public bool Showing { get; set; }
        }
        private Dictionary<string, ComponentState> _states = new Dictionary<string, ComponentState>();
        private PropertyPath _currentPath = new PropertyPath();

        protected override void Visit<TValue>(TValue value)
        {
            GUILayout.Label(Property.Name);
        }

        public override void VisitEnum<TContainer, TValue>(ref TContainer container, VisitContext<TValue> context)
        {
            VisitSetup(ref container, ref context);

            var t = typeof(TValue);
            if (t.IsEnum)
            {
                var options = Enum.GetNames(t).ToArray();
                EditorGUILayout.Popup(
                    t.Name,
                    Array.FindIndex(options, name => name == context.Value.ToString()),
                    options);
            }
        }

        public override bool BeginContainer<TContainer, TValue>(ref TContainer container, VisitContext<TValue> context)
        {
            VisitSetup(ref container, ref context);
            EditorGUI.indentLevel++;

            _currentPath.Push(Property.Name, context.Index);

            if (typeof(TValue) == typeof(StructProxy))
            {
                ComponentState state;
                if (!_states.ContainsKey(_currentPath.ToString()))
                {
                    _states[_currentPath.ToString()] = new ComponentState();
                }
                state = _states[_currentPath.ToString()];

                state.Showing = EditorGUILayout.Foldout(state.Showing, context.Property.Name);

                return state.Showing;
            }
            return true;
        }

        public override void EndContainer<TContainer, TValue>(ref TContainer container, VisitContext<TValue> context)
        {
            VisitSetup(ref container, ref context);
            _currentPath.Pop();

            EditorGUI.indentLevel--;
        }

        public override bool BeginList<TContainer, TValue>(ref TContainer container, VisitContext<TValue> context)
        {
            VisitSetup(ref container, ref context);
            return true;
        }

        public override void EndList<TContainer, TValue>(ref TContainer container, VisitContext<TValue> context)
        {
            VisitSetup(ref container, ref context);
        }

        void ICustomVisit<Unity.Mathematics.quaternion>.CustomVisit(Unity.Mathematics.quaternion q)
        {
            EditorGUILayout.Vector4Field(Property.Name, new Vector4(q.value.x, q.value.y, q.value.z, q.value.w));
        }

        void ICustomVisit<float2>.CustomVisit(float2 f)
        {
            EditorGUILayout.Vector2Field(Property.Name, (Vector2) f);
        }

        void ICustomVisit<float3>.CustomVisit(float3 f)
        {
            EditorGUILayout.Vector3Field(Property.Name, (float3)f);
        }

        void ICustomVisit<float4>.CustomVisit(float4 f)
        {
            EditorGUILayout.Vector4Field(Property.Name, (float4)f);
        }

        void ICustomVisit<float2x2>.CustomVisit(float2x2 f)
        {
            GUILayout.Label(Property.Name);
            EditorGUILayout.Vector2Field("", (Vector2)f.c0);
            EditorGUILayout.Vector2Field("", (Vector2)f.c1);
        }

        void ICustomVisit<float3x3>.CustomVisit(float3x3 f)
        {
            GUILayout.Label(Property.Name);
            EditorGUILayout.Vector3Field("", (Vector3)f.c0);
            EditorGUILayout.Vector3Field("", (Vector3)f.c1);
            EditorGUILayout.Vector3Field("", (Vector3)f.c2);
        }

        void ICustomVisit<float4x4>.CustomVisit(float4x4 f)
        {
            GUILayout.Label(Property.Name);
            EditorGUILayout.Vector4Field("", (Vector4)f.c0);
            EditorGUILayout.Vector4Field("", (Vector4)f.c1);
            EditorGUILayout.Vector4Field("", (Vector4)f.c2);
            EditorGUILayout.Vector4Field("", (Vector4)f.c3);
        }

        #region ICustomVisitPrimitives

        void ICustomVisit<sbyte>.CustomVisit(sbyte f)
        {
            DoField(Property, f, (label, val) => (sbyte)Mathf.Clamp(EditorGUILayout.IntField(label, val), sbyte.MinValue, sbyte.MaxValue));
        }

        void ICustomVisit<short>.CustomVisit(short f)
        {
            DoField(Property, f, (label, val) => (short)Mathf.Clamp(EditorGUILayout.IntField(label, val), short.MinValue, short.MaxValue));
        }

        void ICustomVisit<int>.CustomVisit(int f)
        {
            DoField(Property, f, (label, val) => EditorGUILayout.IntField(label, val));
        }

        void ICustomVisit<long>.CustomVisit(long f)
        {
            DoField(Property, f, (label, val) => EditorGUILayout.LongField(label, val));
        }

        void ICustomVisit<byte>.CustomVisit(byte f)
        {
            DoField(Property, f, (label, val) => (byte)Mathf.Clamp(EditorGUILayout.IntField(label, val), byte.MinValue, byte.MaxValue));
        }

        void ICustomVisit<ushort>.CustomVisit(ushort f)
        {
            DoField(Property, f, (label, val) => (ushort)Mathf.Clamp(EditorGUILayout.IntField(label, val), ushort.MinValue, ushort.MaxValue));
        }

        void ICustomVisit<uint>.CustomVisit(uint f)
        {
            DoField(Property, f, (label, val) => (uint)Mathf.Clamp(EditorGUILayout.LongField(label, val), uint.MinValue, uint.MaxValue));
        }

        void ICustomVisit<ulong>.CustomVisit(ulong f)
        {
            DoField(Property, f, (label, val) =>
            {
                var text = EditorGUILayout.TextField(label, val.ToString());
                ulong num;
                ulong.TryParse(text, out num);
                return num;
            });
        }

        void ICustomVisit<float>.CustomVisit(float f)
        {
            DoField(Property, f, (label, val) => EditorGUILayout.FloatField(label, val));
        }

        void ICustomVisit<double>.CustomVisit(double f)
        {
            DoField(Property, f, (label, val) => EditorGUILayout.DoubleField(label, val));
        }

        void ICustomVisit<bool>.CustomVisit(bool f)
        {
            DoField(Property, f, (label, val) => EditorGUILayout.Toggle(label, val));
        }

        void ICustomVisit<char>.CustomVisit(char f)
        {
            DoField(Property, f, (label, val) =>
            {
                var text = EditorGUILayout.TextField(label, val.ToString());
                var c = (string.IsNullOrEmpty(text) ? '\0' : text[0]);
                return c;
            });
        }

        void ICustomVisit<string>.CustomVisit(string f)
        {
            if (Property == null)
            {
                return;
            }
            GUILayout.Label(f, EditorStyles.boldLabel);
        }
        #endregion

        private void DoField<TValue>(IProperty property, TValue value, Func<GUIContent, TValue, TValue> onGUI)
        {
            if (property == null)
            {
                return;
            }

            var previous = value;
            onGUI(new GUIContent(property.Name), previous);

#if ENABLE_PROPERTY_SET
            var T = property.GetType();
            var typedProperty = Convert.ChangeType(property, T);

            if (!property.IsReadOnly && typedProperty != null)
            {
                // TODO doesn not work, ref container & container access
                T.GetMethod("SetValue").Invoke(property, new object[] { container, v });
            }
#endif
        }
    }
}
