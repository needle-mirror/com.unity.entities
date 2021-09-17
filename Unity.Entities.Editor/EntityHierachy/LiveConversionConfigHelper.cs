using System;

namespace Unity.Entities.Editor
{
    static class LiveConversionConfigHelper
    {
        static readonly Action<Action> k_AddHandler;
        static readonly Action<Action> k_RemoveHandler;
        static readonly Func<bool> k_GetLiveConversionEnabledInEditMode;
        static readonly Action<bool> k_SetLiveConversionEnabledInEditMode;

        static LiveConversionConfigHelper()
        {
            var type = Type.GetType("Unity.Scenes.Editor.SubSceneInspectorUtility, Unity.Scenes.Editor");
            if (type == null)
                return;

            var property = type.GetProperty("LiveConversionEnabled");
            if (property == null)
                return;

            var @event = type.GetEvent("LiveConversionModeChanged");
            if (@event == null)
                return;

            k_AddHandler = (Action<Action>)@event.AddMethod.CreateDelegate(typeof(Action<Action>));
            k_RemoveHandler = (Action<Action>)@event.RemoveMethod.CreateDelegate(typeof(Action<Action>));
            k_GetLiveConversionEnabledInEditMode = (Func<bool>)property.GetMethod.CreateDelegate(typeof(Func<bool>));
            k_SetLiveConversionEnabledInEditMode = (Action<bool>)property.SetMethod.CreateDelegate(typeof(Action<bool>));
        }

        internal static bool IsProperlyInitialized => k_GetLiveConversionEnabledInEditMode != null
                                                      && k_SetLiveConversionEnabledInEditMode != null
                                                      && k_AddHandler != null
                                                      && k_RemoveHandler != null;

        public static bool LiveConversionEnabledInEditMode
        {
            get => k_GetLiveConversionEnabledInEditMode();
            set => k_SetLiveConversionEnabledInEditMode(value);
        }

        public static event Action LiveConversionEnabledChanged
        {
            add => k_AddHandler(value);
            remove => k_RemoveHandler(value);
        }
    }
}
